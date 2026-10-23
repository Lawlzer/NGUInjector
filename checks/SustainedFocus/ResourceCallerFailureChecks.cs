using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal static class ResourceCallerFailureChecks
    {
        private static int _assertions;
        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new Exception(message);
        }

        internal static int Run()
        {
            var settings = Main.Settings;
            var character = Main.Character;
            var owner = Main.reference;
            try
            {
                Main.Settings = new FocusSettings();
                Main.reference = new Main();
                foreach (var mode in new[] {"idle", "best", "pinned"})
                {
                    var c = Main.Character = new FocusCharacter {idleEnergy = 1000, GoldPerSecond = 1000};
                    c.magic.curMagic = c.magic.idleMagic = 700;
                    c.buttons.bloodMagic.interactable = true;
                    for (var i = 0; i < 8; i++) c.bloodMagicController.normalSpeedDividers[i] = 1;
                    var selected = mode == "pinned" ? BloodMagicOptimizer.OptimizeTarget(0, 700) : BloodMagicOptimizer.OptimizeBest(700);
                    Assert(selected.Success, mode + ": pure blood planner selects a feasible exact target");
                    var desired = new long[c.bloodMagic.ritual.Count]; desired[selected.RitualIndex] = 700;
                    c.bloodMagicController.bloodMagics[selected.RitualIndex].ThrowOnText = true;
                    var intent = new ResourceSetupIntent { Owner = "blood:" + mode, Magic = true,
                        Desired = new System.Collections.Generic.Dictionary<ResourceTarget, long[]> { { ResourceTarget.BloodRitual, desired } },
                        Authorized = () => ReferenceEquals(Main.Character, c) };
                    var result = Main.reference.ReconcileStageResourceSetup(intent);
                    Assert(result.Disposition == MutationDisposition.ModelAppliedRenderFailed && result.Error.Message.Contains("Blood UI unavailable"),
                        mode + ": shared typed actuator distinguishes verified model from rendering failure");
                    Assert(c.magic.idleMagic == 0 && c.bloodMagic.ritual.Sum(x => x.magic) == 700,
                        mode + ": blood transfer is committed and conserved before failed menu refresh");
                    Assert(c.idleEnergy == 1000 && c.EnergyRecalls == 0, mode + ": blood caller leaves energy untouched");
                    c.bloodMagicController.bloodMagics[selected.RitualIndex].ThrowOnText = false;
                    result = Main.reference.ReconcileStageResourceSetup(intent);
                    Assert(result.Verified && result.ModelWrites == 0 && c.magic.idleMagic + c.bloodMagic.ritual.Sum(x => x.magic) == 700,
                        mode + ": presentation retry does not repeat a committed resource effect");
                }

                var stageCharacter = Main.Character = new FocusCharacter {idleEnergy = 1000};
                stageCharacter.magic.idleMagic = 577;
                stageCharacter.wandoos98.wandoosMagic = 123;
                stageCharacter.buttons.advancedTraining.interactable = true;
                stageCharacter.wandoos98Controller.ThrowOnUpdate = true;
                var stage = new CustomPlanStage {EnergyPriorities = new[] {"AT-1"}};
                var failed = false;
                try { CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 300); }
                catch (InvalidOperationException error) { failed = error.Message == "Wandoos UI unavailable"; }
                Assert(!failed && CustomStageAllocator.LastStageApplicationSucceeded
                    && CustomStageAllocator.LastStageResourceOutcome.Disposition == MutationDisposition.ModelAppliedRenderFailed,
                    "AT entry verifies complete desired accounting and reports a presentation-only failure");
                Assert(stageCharacter.wandoos98.wandoosMagic == 0 && stageCharacter.magic.idleMagic == 700,
                    "AT entry recall conserves magic before a throwing Wandoos refresh");
                Assert(stageCharacter.curEnergy == 1000 && stageCharacter.EnergyRecalls == 0 && stageCharacter.advancedTraining.energy[1] > 0,
                    "AT and removal of conflicting W commit together without a full energy recall");
                stageCharacter.wandoos98Controller.ThrowOnUpdate = false;
                CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 300);
                Assert(CustomStageAllocator.LastStageApplicationSucceeded && stageCharacter.advancedTraining.energy[1] > 0
                    && stageCharacter.curEnergy == 1000 && stageCharacter.magic.idleMagic == 700,
                    "AT entry retry recovers with both resource totals intact");
                Console.WriteLine("PASS: resource caller failures: 4 cases, " + _assertions + " assertions.");
                return _assertions;
            }
            finally
            {
                CustomStageAllocator.ResetResourceTargets();
                Main.Settings = settings;
                Main.Character = character;
                Main.reference = owner;
            }
        }
    }
}
