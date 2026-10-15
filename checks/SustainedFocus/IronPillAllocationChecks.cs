using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal static class IronPillAllocationChecks
    {
        private static int _assertions;
        private static void Equal<T>(T expected, T actual, string name)
        {
            _assertions++;
            if (!Equals(expected, actual)) throw new Exception(name + ": expected " + expected + ", got " + actual);
        }

        private static FocusCharacter Allocate(CustomPlanStage stage, bool saving)
        {
            var c = new FocusCharacter();
            Main.Character = c;
            Main.IronPillBloodReserved = saving;
            c.idleEnergy = 1000;
            c.magic.idleMagic = 1000;
            c.GoldPerSecond = 1000;
            c.buttons.bloodMagic.interactable = c.buttons.wandoos.interactable = true;
            c.buttons.advancedTraining.interactable = true;
            for (var i = 0; i < 8; i++) c.bloodMagicController.normalSpeedDividers[i] = .001f;
            CustomStageAllocator.ResetResourceTargets();
            CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 0, 0, "pill-allocation-check");
            return c;
        }

        internal static int Run()
        {
            try
            {
                var stage = new CustomPlanStage {
                    EnergyFocus = (int) CustomStageFocus.EnergyNgu,
                    MagicFocus = (int) CustomStageFocus.MagicNgu,
                    GearGoal = (int) GearOptimizationGoal.AllNgu,
                    MagicPriorities = new[] {"NGU", "RIT-0", "RIT-1", "WAN"}
                };
                var saved = stage.MagicPriorities.ToArray();
                var ordinary = Allocate(stage, false);
                Equal(1000L, ordinary.NGU.magicSkills[0].magic, "ordinary stage allocates magic to NGU first");
                var saving = Allocate(stage, true);
                Equal(51L, saving.bloodMagic.ritual.Sum(r => r.magic), "saving promotes one real optimized ritual at allocation boundary");
                Equal(949L, saving.NGU.magicSkills[0].magic, "NGU gets ritual overflow only");
                Equal(1, saving.bloodMagic.ritual.Count(r => r.magic > 0), "explicit RIT-n entries cannot add extra gold consumers");
                Equal(7, saving.bloodMagic.ritual.FindIndex(r => r.magic > 0), "real optimizer chooses its deterministic best ritual");
                Equal(string.Join(",", ordinary.NGU.skills.Select(x => x.energy)), string.Join(",", saving.NGU.skills.Select(x => x.energy)), "overlay leaves stage energy allocation identical");
                Equal(1000L, saving.NGU.skills.Sum(x => x.energy), "active energy stage continues running");
                Equal(1, saving.EnergyRecalls, "one ordinary stage energy pass, not extra prep recall");
                Equal((int) GearOptimizationGoal.AllNgu, stage.GearGoal, "allocation overlay does not replace gear goal");
                Equal(string.Join(",", saved), string.Join(",", stage.MagicPriorities), "allocation keeps saved array immutable");
                stage.MagicFocus = (int) CustomStageFocus.Wandoos;
                saving = Allocate(stage, true);
                Equal(2L, saving.wandoos98.wandoosMagic, "Wandoos caps WAN before ritual");
                Equal(51L, saving.bloodMagic.ritual.Sum(r => r.magic), "Wandoos overflow reaches RIT second");
                Equal(947L, saving.NGU.magicSkills[0].magic, "Wandoos remaining magic retains original NGU fallback");
                // Reapply in the same context, as the quick refresh does. No extra ritual or pool growth.
                CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 0, 0, "pill-allocation-check");
                Equal(1000L, saving.wandoos98.wandoosMagic + saving.bloodMagic.ritual.Sum(r => r.magic) + saving.NGU.magicSkills.Sum(x => x.magic) + saving.magic.idleMagic, "quick refresh conserves the entire magic pool");
                Equal(1, saving.bloodMagic.ritual.Count(r => r.magic > 0), "quick refresh retains one gold consumer");
                Equal(2L, saving.wandoos98.wandoosMagic, "quick refresh cannot bypass Wandoos overlay");
                Equal(51L, saving.bloodMagic.ritual.Sum(r => r.magic), "quick refresh cannot bypass ritual overlay");
                Main.IronPillBloodReserved = false;
                CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 0, 0, "pill-allocation-check");
                Equal(1000L, saving.NGU.magicSkills[0].magic, "ending reservation restores original allocation order");
                Equal(0L, saving.bloodMagic.ritual.Sum(r => r.magic), "ending reservation releases ritual magic");
                Equal(0L, saving.wandoos98.wandoosMagic, "saved NGU-first order restored even on Wandoos focus override");
                stage.CompletionMode = (int) CustomStageCompletionMode.AdvancedTrainingStats;
                stage.TargetAdvancedTrainingPower = 100;
                stage.TargetAdvancedTrainingToughness = 200;
                stage.EnergyPriorities = new[] {"AT"};
                saving = Allocate(stage, true);
                Equal(1000L, saving.advancedTraining.energy.Sum(), "AT energy still allocates while blood is reserved");
                Equal(0L, saving.advancedTraining.levelTarget[1], "AT power menu target restored after stage-target allocation");
                Equal(0L, saving.advancedTraining.levelTarget[0], "AT toughness menu target restored after stage-target allocation");
                Equal(100L, stage.TargetAdvancedTrainingPower, "reservation leaves configured AT power target intact");
                Equal(200L, stage.TargetAdvancedTrainingToughness, "reservation leaves configured AT toughness target intact");
                return _assertions;
            }
            finally { Main.IronPillBloodReserved = false; }
        }
    }
}
