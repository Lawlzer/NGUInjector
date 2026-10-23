using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal static class PairSharingChecks
    {
        private static int _assertions;

        private static void Assert(bool condition, string name)
        {
            _assertions++;
            if (!condition) throw new Exception(name);
        }

        private static FocusCharacter Allocate(string[] priorities, Action<FocusCharacter> setup = null, long budget = 1000)
        {
            var c = new FocusCharacter {idleEnergy = budget};
            Main.Character = c;
            c.buttons.advancedTraining.interactable = c.buttons.augmentation.interactable = true;
            for (var i = 0; i < 7; i++)
                c.augmentsController.normalAugSpeedDividers[i] = c.augmentsController.normalUpgradeSpeedDividers[i] = 1;
            setup?.Invoke(c);
            NguSharingChecks.ApplyPriorities(priorities, true, 5);
            Assert(c.idleEnergy + c.advancedTraining.energy.Sum() + c.augments.augs.Sum(a => a.augEnergy + a.upgradeEnergy)
                + c.NGU.skills.Sum(s => s.energy) + c.wandoos98.wandoosEnergy == budget, "paired allocation conserves energy");
            return c;
        }

        internal static int Run()
        {
            _assertions = 0;
            foreach (var pair in new[] {new[] {3, 4}, new[] {4, 3}, new[] {1, 0}, new[] {0, 1}})
            {
                var c = Allocate(pair.Select(i => "AT-" + i).Concat(new[] {"NGU"}).ToArray(), budget: 1001);
                Assert(c.advancedTraining.energy[pair[0]] == 501 && c.advancedTraining.energy[pair[1]] == 500,
                    "related AT bars share equally with a stable odd remainder");
                Assert(c.NGU.skills.All(s => s.energy == 0), "an underfunded AT pair retains priority over NGUs");
            }
            var character = Allocate(new[] {"AT"});
            Assert(character.advancedTraining.energy[1] == 500 && character.advancedTraining.energy[0] == 500,
                "generic AT expands into an equally shared adventure pair");
            character = Allocate(new[] {"AT-3", "AT-4"}, c => c.advancedTraining.levelTarget[3] = 1);
            Assert(character.advancedTraining.energy[3] == 51 && character.advancedTraining.energy[4] == 949,
                "a capped AT share redistributes to its partner");
            character = Allocate(new[] {"AT-3", "AT-4"}, c => c.advancedTraining.levelTarget[3] = -1);
            Assert(character.advancedTraining.energy[3] == 0 && character.advancedTraining.energy[4] == 1000,
                "a disabled AT bar leaves its share to its partner");
            character = Allocate(new[] {"AT-3", "AT-4", "NGU"}, c =>
                c.advancedTraining.levelTarget[3] = c.advancedTraining.levelTarget[4] = 1);
            Assert(character.advancedTraining.energy[3] == 51 && character.advancedTraining.energy[4] == 51
                && character.NGU.skills.Sum(s => s.energy) == 898, "fully capped AT pairs spill to the next priority");
            character = Allocate(new[] {"AT-3", "NGU", "AT-4"});
            Assert(character.advancedTraining.energy[3] == 1000 && character.advancedTraining.energy[4] == 0,
                "separated explicit AT targets preserve intentional ordering");
            character = Allocate(new[] {"WAN", "AT-3", "AT-4", "AUG"}, c => c.buttons.wandoos.interactable = true);
            Assert(character.wandoos98.wandoosEnergy == 2 && character.advancedTraining.energy[3] == 499
                && character.advancedTraining.energy[4] == 499, "AT shares only the remaining budget and never evaluates an unreachable optimizer");
            character = Allocate(new[] {"AT-3", "AT-4", "AT-3", "AT-4", "NGU"}, c =>
                c.advancedTraining.levelTarget[3] = c.advancedTraining.levelTarget[4] = 1);
            Assert(character.advancedTraining.energy[3] == 51 && character.advancedTraining.energy[4] == 51,
                "paired AT targets cannot be allocated twice");

            for (var index = 0; index < 7; index++)
            foreach (var reverse in new[] {false, true})
            {
                var tokens = new[] {"AUG-" + index * 2, "AUG-" + (index * 2 + 1)};
                if (reverse) Array.Reverse(tokens);
                character = Allocate(tokens);
                Assert(character.augments.augs[index].augEnergy == 500 && character.augments.augs[index].upgradeEnergy == 500,
                    "each augment/upgrade pair shares equally in either order");
            }
            character = Allocate(new[] {"AUG-0", "AUG-1"}, c =>
            {
                c.augments.augs[0].augmentTarget = 1;
                c.augmentsController.normalAugSpeedDividers[0] = .001f;
            });
            Assert(character.augments.augs[0].augEnergy == 51 && character.augments.augs[0].upgradeEnergy == 949,
                "a capped augment redistributes to its upgrade");
            character = Allocate(new[] {"AUG-0", "AUG-1"}, c => c.augmentsController.augments[0].UpgradeLocked = true);
            Assert(character.augments.augs[0].augEnergy == 1000 && character.augments.augs[0].upgradeEnergy == 0,
                "a locked upgrade leaves energy to its augment");
            character = Allocate(new[] {"AUG-0", "AUG-1", "NGU"}, c => c.realGold = 0);
            Assert(character.augments.augs[0].augEnergy == 0 && character.augments.augs[0].upgradeEnergy == 0
                && character.NGU.skills.Sum(s => s.energy) == 1000, "unaffordable pairs leave energy to later priorities");
            character = Allocate(new[] {"AUG-0", "AUG-3"});
            Assert(character.augments.augs[0].augEnergy == 1000 && character.augments.augs[1].upgradeEnergy == 0,
                "different augment pairs are not combined");
            character = Allocate(new[] {"AT-3", "AT-4"}, budget: 1);
            Assert(character.advancedTraining.energy[3] == 1 && character.advancedTraining.energy[4] == 0,
                "tiny budgets are conserved without forcing an impossible second share");

            character = Allocate(new string[0]);
            Main.reference = new Main();
            CustomStageAllocator.ApplyStage(new CustomPlan(), new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.Wandoos},
                250, stageDurationSeconds: 300);
            Assert(character.advancedTraining.energy[3] == 500 && character.advancedTraining.energy[4] == 500,
                "default Wandoos AT phase expands into an equally shared pair");

            // Use the real stage resolver while substituting only the optimizer's pair selection.
            AugmentOptimizer.Selected = new AugmentOptimizerResult {Success = true, UsesUpgrade = true, AugmentIndex = 2};
            try
            {
                foreach (var generic in new[] {false, true})
                {
                    character = Allocate(new string[0]);
                    var stage = new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.Augments,
                        EnergyPriorities = generic ? new[] {"AUG", "NGU"} : new[] {"AT-3", "AT-4", "NGU"}};
                    CustomStageAllocator.ResetResourceTargets();
                    CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 300);
                    Assert(generic ? character.augments.augs[2].augEnergy == 500 && character.augments.augs[2].upgradeEnergy == 500
                        : character.advancedTraining.energy[3] == 500 && character.advancedTraining.energy[4] == 500,
                        "real stage allocation preserves both Wandoos AT and optimizer-selected augment pairing");
                }
            }
            finally { AugmentOptimizer.Selected = null; CustomStageAllocator.ResetResourceTargets(); }
            return _assertions;
        }
    }
}
