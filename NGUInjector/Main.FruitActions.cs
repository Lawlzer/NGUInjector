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
            if (_harvestActionClaim != null)
                return FiniteActionSetupVerified(_harvestActionClaim, _harvestActionSetup);
            var id = ActionIdentity("harvest-setup", "ygg-harvest", "harvest");
            var claim = BeginExecutionClaim(id.Owner, id.Acquisition,
                ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.Inventory | ExecutionDomain.FiniteAction,
                () => true);
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
            return RunFruitHarvestAction(fruitId, requireMaxed, forceEat, setupClaim, selectedSetup, null, null);
        }

        private bool RunFruitHarvestAction(int fruitId, bool requireMaxed, bool forceEat, ExecutionClaim setupClaim,
            SelectedSetupRequirements selectedSetup, string opportunity, int? originalHarvests)
        {
            if (Character?.yggdrasil?.fruits == null || fruitId < 0 || fruitId >= Character.yggdrasil.fruits.Count) return false;
            var fruit = Character.yggdrasil.fruits[fruitId];
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
            var result = RunFiniteAction(action, grant);
            // This is only completion of the SAME preparation step, never a fresh
            // native receipt. Boss entry still needs current exact setup/authority.
            return result.Verified || opportunity != null && result.PreviouslyCommitted && result.Committed && result.Durable && setupClaim != null
                && selectedSetup != null && FiniteActionSetupVerified(setupClaim, selectedSetup);
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
            var fruit = Character.yggdrasil.fruits[fruitId];
            var energy = Character.yggdrasilController.usesEnergy[fruitId];
            if (energy ? !Settings.ManageEnergy : !Settings.ManageMagic) return false;
            var id = ActionIdentity("fruit-activate:" + fruitId, "fruit-activation", ActionNumber(fruit.harvests));
            var footprint = ExecutionDomain.FiniteAction | (energy ? ExecutionDomain.Energy : ExecutionDomain.Magic);
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
