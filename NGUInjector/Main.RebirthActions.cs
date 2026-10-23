using System;
using System.Linq;
using System.Reflection;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private ExecutionClaim _rebirthActionClaim;
        private SelectedSetupRequirements _rebirthActionSetup;
        internal bool RebirthFinitePrerequisitesResolved
        {
            get
            {
                try
                {
                    var acquisition = "rebirth:" + ActionNumber(Character.stats.rebirthNumber);
                    var id = ActionIdentity("rebirth", "rebirth", "engage");
                    var dependencies = 511 & ~(int)ExecutionDomain.FiniteAction;
                    if (!FiniteActions.FlushDependentResults(id, acquisition, dependencies)
                        || FiniteActions.HasUnresolvedDependencies(id, acquisition, dependencies)) return false;
                    return !FiniteActions.Records.Any(x => x.Identity.Acquisition == acquisition
                        && (x.Identity.Kind.StartsWith("fruit-harvest:", StringComparison.Ordinal)
                            || x.Identity.Kind.StartsWith("blood-", StringComparison.Ordinal))
                        && (x.State == FiniteActionState.Unknown || x.State == FiniteActionState.Invocation
                            || x.State == FiniteActionState.Committed && x.Dirty));
                }
                catch { return false; }
            }
        }

        internal bool PrepareFinalRebirthAction()
        {
            if (_rebirthActionClaim != null) return FiniteActionSetupVerified(_rebirthActionClaim, _rebirthActionSetup);
            var id = ActionIdentity("rebirth", "rebirth", "engage");
            var claim = BeginExecutionClaim(id.Owner, id.Acquisition, ExecutionDomain.FiniteAction | ExecutionDomain.NativeSettings
                | ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.Inventory, () => true);
            _rebirthActionClaim = claim;
            try
            {
                var support = CaptureSupportState().Selection;
                var change = Character.buttons.beards.interactable;
                if (change)
                {
                    if (Character.beards.disabled || Character.allBeards.capBeards() < 1)
                        throw new InvalidOperationException("Rebirth requires an available NUMBER beard slot.");
                    support = SelectActionSupport(new[] { 2 }, support.Diggers.Select(x => x.Id).ToArray());
                    if (!support.Beards.Contains(2)) throw new InvalidOperationException("Required NUMBER beard does not fit.");
                }
                _rebirthActionSetup = CreateFiniteActionSetup(claim, LoadoutManager.CaptureExactLoadout(), support, false, change,
                    change ? new[] { 2 } : new int[0]);
                ApplyExecutionClaim(claim, _rebirthActionSetup);
                return FiniteActionSetupVerified(claim, _rebirthActionSetup);
            }
            catch { ReleaseFinalRebirthAction(); throw; }
        }

        internal void ReleaseFinalRebirthAction()
        {
            var claim = _rebirthActionClaim;
            _rebirthActionClaim = null; _rebirthActionSetup = null;
            if (claim != null) EndExecutionClaim(claim);
        }

        internal bool RefreshFinalRebirthMultipliers()
        {
            var claim = _rebirthActionClaim;
            if (claim == null) return false;
            var grant = ResolveExecutionGrant(claim.Owner, claim.Acquisition, claim.Domains,
                () => FiniteActionSetupVerified(claim, _rebirthActionSetup), claim);
            foreach (var method in new[] { "calculateTimeMulti", "calculateNextMultis" })
            {
                if (!VerifyExecutionPrerequisites(grant, claim.Domains, () => FiniteActionSetupVerified(claim, _rebirthActionSetup))) return false;
                var callback = Character.rebirth.GetType().GetPrivateMethod(method);
                if (callback == null) return false;
                callback.Invoke(Character.rebirth, null);
                if (!ValidateExecutionGrant(grant, claim.Domains)) return false;
            }
            return true;
        }

        private sealed class NativeRebirthAction : FiniteAction
        {
            internal long Rebirth;
            internal MethodInfo Method;
            internal Func<bool> SetupVerified;
            internal override bool Prerequisites() => SetupVerified() && Character.stats.rebirthNumber == Rebirth
                && reference.RebirthFinitePrerequisitesResolved
                && Character.rebirthTime.totalseconds > Character.rebirth.minRebirthTime()
                && !Character.bossController.isFighting && !Character.bossController.nukeBoss
                && (!Character.buttons.beards.interactable || !Character.beards.disabled && Character.beards.activeBeards.Contains(2));
            internal override string Observe() => ActionNumber(Character.stats.rebirthNumber) + "|" + ActionNumber(Character.rebirthTime.totalseconds);
            internal override void Invoke() => Method.Invoke(Character.rebirth, null);
            internal override MutationDisposition Classify(string before, string after, Exception error) => Character.stats.rebirthNumber == Rebirth + 1
                && Character.rebirthTime.totalseconds == 0d
                    ? error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed : MutationDisposition.UnknownOutcome;
        }

        internal bool InvokeFinalRebirthAction(string method)
        {
            var claim = _rebirthActionClaim;
            if (claim == null) return false;
            var callback = Character.rebirth.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SingleOrDefault(x => x.Name == method && x.GetParameters().Length == 0);
            if (callback == null) return false;
            // Normal and challenge engagement share one identity: a failed/unknown
            // challenge must never fall through into a second normal rebirth.
            var id = ActionIdentity("rebirth", claim.Owner, "engage");
            var action = new NativeRebirthAction { Identity = id, Footprint = claim.Domains, Rebirth = Character.stats.rebirthNumber,
                DependencyDomains = (ExecutionDomain)(511 & ~(int)ExecutionDomain.FiniteAction),
                Method = callback, SetupVerified = () => FiniteActionSetupVerified(claim, _rebirthActionSetup) };
            var result = RunFiniteAction(action, ResolveExecutionGrant(id.Owner, id.Acquisition, action.Footprint,
                () => ExecutionClaimCurrent(claim), claim));
            // A verified native rebirth deliberately retires the old epoch/grant.
            return result.Committed && result.Durable && Character.stats.rebirthNumber == action.Rebirth + 1;
        }
    }
}
