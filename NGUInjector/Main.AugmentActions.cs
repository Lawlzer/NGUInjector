using System;
using System.Collections.Generic;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal AugmentOptimizerResult RequestAugmentAllocation(AugmentOptimizerResult selected)
        {
            if (!selected.Success) return selected;
            var claim = BeginExecutionClaim("explicit-augment", "augment:" + selected.AugmentIndex,
                ExecutionDomain.Energy, () => Settings.ManageEnergy);
            try
            {
                var augment = new long[Character.augments.augs.Length];
                var upgrade = new long[augment.Length];
                augment[selected.AugmentIndex] = selected.EnergyToAugment;
                upgrade[selected.AugmentIndex] = selected.EnergyToUpgrade;
                SetExecutionResourcePolicy(claim, new ResourceSetupIntent { Owner = claim.Owner, Energy = true,
                    Desired = new Dictionary<ResourceTarget, long[]> { { ResourceTarget.Augment, augment }, { ResourceTarget.AugmentUpgrade, upgrade } },
                    Authorized = () => ExecutionClaimCurrent(claim) });
                var outcome = ReconcileExecutionResourceClaim(claim);
                var state = Character.augments.augs[selected.AugmentIndex];
                if (outcome?.Verified != true || state.augEnergy != selected.EnergyToAugment || state.upgradeEnergy != selected.EnergyToUpgrade)
                {
                    selected.Success = false;
                    selected.Message = outcome?.Reason ?? "Augment allocation waiting for exact shared execution.";
                }
                return selected;
            }
            // The command changes present intent once. A later active baseline may
            // supersede it; no historical allocation map or private transfer exists.
            finally { EndExecutionClaim(claim, false); }
        }
    }
}
