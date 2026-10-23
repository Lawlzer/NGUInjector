using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal static class ProtectedApplicationChecks
    {
        private static int _assertions;

        private static void Assert(bool condition, string name)
        {
            _assertions++;
            if (!condition) throw new Exception(name);
        }

        private static FocusCharacter Fresh()
        {
            Main.Settings = new FocusSettings();
            Main.reference = new Main();
            Main.IronPillBloodReserved = false;
            var c = Main.Character = new FocusCharacter {idleEnergy = 1000, GoldPerSecond = 1000};
            c.magic.idleMagic = 700;
            c.buttons.brokenTimeMachine.interactable = c.buttons.wandoos.interactable = true;
            c.buttons.augmentation.interactable = c.buttons.advancedTraining.interactable = c.buttons.bloodMagic.interactable = true;
            for (var i = 0; i < 8; i++) c.bloodMagicController.normalSpeedDividers[i] = .001f;
            CustomStageAllocator.ResetResourceTargets();
            return c;
        }

        private static CustomPlanStage NguStage()
        {
            return new CustomPlanStage {
                EnergyFocus = (int) CustomStageFocus.EnergyNgu,
                MagicFocus = (int) CustomStageFocus.MagicNgu
            };
        }

        private static bool Eligible(CustomPlan plan, CustomPlanStage stage)
        {
            return CustomStageAllocator.StageApplicationEligible(plan, stage, 90, 120);
        }

        private static string Apply(CustomPlan plan, CustomPlanStage stage)
        {
            // Preserve the exact six-argument application boundary used by reflection callers.
            return CustomStageAllocator.ApplyStage(plan, stage, 90, 0, "protected-application-checks", 120);
        }

        internal static int Run()
        {
            var settings = Main.Settings;
            var character = Main.Character;
            var owner = Main.reference;
            var reserved = Main.IronPillBloodReserved;
            _assertions = 0;
            try
            {
                var plan = new CustomPlan {AutoIronPillEnabled = false};
                var c = Fresh();
                var stage = NguStage();
                Assert(Eligible(plan, stage), "both available managed NGU lanes are eligible");
                Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0 && c.idleEnergy == 1000 && c.magic.idleMagic == 700,
                    "eligibility does not recall or allocate resources");
                Apply(plan, stage);
                Assert(CustomStageAllocator.LastStageApplicationSucceeded, "a fully returned eligible pass issues its receipt");
                Assert(c.NGU.skills.Sum(skill => skill.energy) == 1000 && c.NGU.magicSkills[0].magic == 700,
                    "receipt checks leave actual allocation intact");

                foreach (var energy in new[] {true, false})
                foreach (var explicitChain in new[] {true, false})
                {
                    c = Fresh();
                    stage = explicitChain ? new CustomPlanStage {EnergyPriorities = new[] {"NGU"}, MagicPriorities = new[] {"NGU"}} : NguStage();
                    if (energy) Main.Settings.ManageEnergy = false;
                    else Main.Settings.ManageMagic = false;
                    Assert(!Eligible(plan, stage), "any requested unmanaged lane blocks eligibility: " + energy + "/" + explicitChain);
                    Apply(plan, stage);
                    Assert(!CustomStageAllocator.LastStageApplicationSucceeded, "skipping a requested lane never issues a receipt");
                    Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0
                        && (energy ? c.NGU.magicSkills.Sum(x => x.magic) > 0 : c.NGU.skills.Sum(x => x.energy) > 0),
                        "exact desired differences service only the managed lane without native recalls");
                }

                foreach (var energy in new[] {true, false})
                {
                    c = Fresh();
                    Main.Settings.ManageEnergy = energy;
                    Main.Settings.ManageMagic = !energy;
                    stage = new CustomPlanStage {
                        EnergyFocus = energy ? (int) CustomStageFocus.EnergyNgu : 0,
                        MagicFocus = energy ? 0 : (int) CustomStageFocus.MagicNgu
                    };
                    Apply(plan, stage);
                    Assert(CustomStageAllocator.LastStageApplicationSucceeded, "unrequested unmanaged lane does not block the requested lane");
                    Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0, "neither requested nor empty lanes use native recall as an allocation strategy");
                }

                c = Fresh();
                stage = new CustomPlanStage();
                Assert(!Eligible(plan, stage), "two empty chains cannot earn protected time");
                Assert(Apply(plan, stage).Contains("no work was requested") && !CustomStageAllocator.LastStageApplicationSucceeded,
                    "empty work is blocked and reported instead of becoming an implicit wait");
                Main.IronPillBloodReserved = true;
                Assert(Eligible(plan, stage), "reservation creates real requested magic work even when both saved chains are empty");
                Main.Settings.ManageMagic = false;
                Assert(!Eligible(plan, stage), "effective reserved magic lane must also be managed");

                foreach (var energy in new[] {true, false})
                {
                    var locked = new[] {
                        new {Focus = CustomStageFocus.TimeMachine, Gate = "TM button", Lock = (Action<FocusCharacter>)(x => x.buttons.brokenTimeMachine.interactable = false)},
                        new {Focus = CustomStageFocus.TimeMachine, Gate = "TM challenge", Lock = (Action<FocusCharacter>)(x => x.challenges.timeMachineChallenge.inChallenge = true)},
                        new {Focus = energy ? CustomStageFocus.EnergyNgu : CustomStageFocus.MagicNgu, Gate = "NGU button", Lock = (Action<FocusCharacter>)(x => x.buttons.ngu.interactable = false)},
                        new {Focus = CustomStageFocus.Wandoos, Gate = "WAN button", Lock = (Action<FocusCharacter>)(x => x.buttons.wandoos.interactable = false)},
                        new {Focus = CustomStageFocus.Wandoos, Gate = "WAN disabled", Lock = (Action<FocusCharacter>)(x => x.wandoos98.disabled = true)},
                        new {Focus = energy ? CustomStageFocus.Augments : CustomStageFocus.BloodMagic, Gate = "AUG/RIT button", Lock = (Action<FocusCharacter>)(x => { x.buttons.augmentation.interactable = false; x.buttons.bloodMagic.interactable = false; })},
                        new {Focus = energy ? CustomStageFocus.AdvancedTraining : CustomStageFocus.BloodMagic, Gate = "AT/RIT button", Lock = (Action<FocusCharacter>)(x => { x.buttons.advancedTraining.interactable = false; x.buttons.bloodMagic.interactable = false; })}
                    };
                    foreach (var test in locked)
                    {
                        c = Fresh();
                        // Explicit fallback may work, but must not replace the declared main focus.
                        var fallback = test.Gate == "NGU button" ? "WAN" : "NGU";
                        stage = new CustomPlanStage {
                            EnergyFocus = energy ? (int) test.Focus : 0,
                            MagicFocus = energy ? 0 : (int) test.Focus,
                            EnergyPriorities = energy ? new[] {fallback} : null,
                            MagicPriorities = energy ? null : new[] {fallback}
                        };
                        Assert(Eligible(plan, stage), "unlocked primary is eligible: " + test.Gate);
                        test.Lock(c);
                        Assert(!Eligible(plan, stage), "locked declared primary blocks fallback credit: " + test.Gate);
                        Apply(plan, stage);
                        Assert(!CustomStageAllocator.LastStageApplicationSucceeded, "fallback cannot issue a primary receipt: " + test.Gate);
                        Assert(energy ? c.idleEnergy < 1000 : c.magic.idleMagic < 700, "fallback still allocates: " + test.Gate);
                    }

                    c = Fresh();
                    stage = new CustomPlanStage {
                        EnergyPriorities = energy ? new[] {"TM", "NGU"} : null,
                        MagicPriorities = energy ? null : new[] {"TM", "NGU"}
                    };
                    c.challenges.timeMachineChallenge.inChallenge = true;
                    Apply(plan, stage);
                    Assert(!CustomStageAllocator.LastStageApplicationSucceeded, "focus None uses the explicit first token, not its working fallback");

                    c = Fresh();
                    stage = NguStage();
                    var weights = energy ? Main.Settings.EnergyNguWeights : Main.Settings.MagicNguWeights;
                    if (energy) Main.Settings.EnergyNguWeights = new int[9];
                    else Main.Settings.MagicNguWeights = new int[7];
                    Assert(!Eligible(plan, stage), "generic NGU needs at least one positive weight in each requested lane");
                    stage = new CustomPlanStage {
                        EnergyPriorities = energy ? new[] {"NGU-0"} : null,
                        MagicPriorities = energy ? null : new[] {"NGU-0"}
                    };
                    Assert(!Eligible(plan, stage), "explicit NGU singleton cannot bypass global exclusions for stage credit");
                    if (energy) Main.Settings.EnergyNguWeights = weights;
                    else Main.Settings.MagicNguWeights = weights;
                    plan = new CustomPlan {AutoIronPillEnabled = false};
                }

                c = Fresh();
                stage = new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.Augments};
                c.challenges.noAugsChallenge.inChallenge = true;
                Assert(!Eligible(plan, stage), "no-augs challenge blocks the augment primary");
                stage = new CustomPlanStage {EnergyPriorities = new[] {"AUG-1", "NGU"}};
                c.challenges.noAugsChallenge.inChallenge = false;
                c.augmentsController.augments[0].UpgradeLocked = true;
                Assert(!Eligible(plan, stage), "explicit locked upgrade cannot count through its fallback");
                stage = new CustomPlanStage {MagicPriorities = new[] {"RIT-0", "NGU"}};
                c.bloodMagic.ritual[0].boss = c.bossID;
                Assert(!Eligible(plan, stage), "explicit boss-locked ritual cannot count through its fallback");

                c = Fresh();
                stage = new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.Wandoos};
                c.buttons.wandoos.interactable = false;
                Apply(plan, stage);
                Assert(c.advancedTraining.energy[3] + c.advancedTraining.energy[4] == 1000 && !CustomStageAllocator.LastStageApplicationSucceeded,
                    "Wandoos AT phase cannot disguise its locked Wandoos primary");

                c = Fresh();
                stage = NguStage();
                foreach (var skill in c.NGU.skills.Concat(c.NGU.magicSkills)) { skill.target = 1; skill.level = 1; }
                stage.EnergyPriorities = stage.MagicPriorities = new[] {"NGU"};
                Apply(plan, stage);
                Assert(CustomStageAllocator.LastStageApplicationSucceeded && c.idleEnergy == 1000 && c.magic.idleMagic == 700,
                    "valid completed NGU targets issue a receipt with no positive transfer");
                stage = new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.TimeMachine, MagicFocus = (int) CustomStageFocus.TimeMachine,
                    EnergyPriorities = new[] {"TM"}, MagicPriorities = new[] {"TM"}};
                c.machine.speedTarget = c.machine.multiTarget = 1;
                c.machine.levelSpeed = c.machine.levelGoldMulti = 1;
                Apply(plan, stage);
                Assert(CustomStageAllocator.LastStageApplicationSucceeded && c.machine.speedEnergy == 0 && c.machine.goldMultiMagic == 0,
                    "valid completed Time Machine targets issue a receipt with no positive transfer");
                stage = new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.AdvancedTraining,
                    EnergyPriorities = new[] {"AT"}, CompletionMode = (int) CustomStageCompletionMode.AdvancedTrainingStats,
                    TargetAdvancedTrainingPower = 10, TargetAdvancedTrainingToughness = 5};
                c.advancedTraining.level[1] = 10;
                c.advancedTraining.level[0] = 5;
                Apply(plan, stage);
                Assert(CustomStageAllocator.LastStageApplicationSucceeded && c.advancedTraining.energy.Sum() == 0,
                    "stage-complete AT targets remain eligible after resolution removes all primary bars");

                foreach (var energy in new[] {true, false})
                {
                    Fresh();
                    Apply(plan, NguStage());
                    c = Fresh();
                    stage = NguStage();
                    (energy ? c.NGUController.NGU[4] : c.NGUController.NGUMagic[0]).ThrowOnUpdate = true;
                    var failed = false;
                    try { Apply(plan, stage); }
                    catch (InvalidOperationException error) { failed = error.Message == "NGU UI unavailable"; }
                    Assert(!failed && CustomStageAllocator.LastStageApplicationSucceeded
                        && CustomStageAllocator.LastStageResourceOutcome.Disposition == MutationDisposition.ModelAppliedRenderFailed,
                        "verified complete accounting permits stage credit despite presentation-only failure: " + energy);
                    Assert(energy || c.NGU.skills.Sum(skill => skill.energy) == 1000, "magic failure occurs after successful energy allocation");
                }

                c = Fresh();
                stage = new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.TimeMachine, EnergyPriorities = new[] {"NGU"}};
                c.challenges = null;
                Apply(plan, stage);
                Assert(c.NGU.skills.Sum(skill => skill.energy) == 1000 && !CustomStageAllocator.LastStageApplicationSucceeded,
                    "an eligibility-only exception fails closed without changing fallback allocation");

                c = Fresh();
                stage = new CustomPlanStage {MagicPriorities = new[] {"RIT"}};
                c.bloodMagic = null;
                Assert(!Eligible(plan, stage), "eligibility exceptions fail closed");
                Assert(!CustomStageAllocator.LastStageApplicationSucceeded, "pure eligibility never issues an application receipt");
                c = Fresh();
                Apply(plan, NguStage());
                Assert(CustomStageAllocator.LastStageApplicationSucceeded, "receipt recovers after an actual successful pass");
                Assert(Apply(plan, null) == "No custom stage active." && !CustomStageAllocator.LastStageApplicationSucceeded,
                    "early null-stage return clears a previous successful receipt");
                Apply(plan, NguStage());
                var planFailed = false;
                try { Apply(null, NguStage()); }
                catch (InvalidOperationException) { planFailed = true; }
                Assert(planFailed && !CustomStageAllocator.LastStageApplicationSucceeded, "early allocation exception clears a previous receipt");
                CheckAppliedTransitions();
                Console.WriteLine("PASS: protected stage application: " + _assertions + " assertions.");
                return _assertions;
            }
            finally
            {
                Main.Settings = settings;
                Main.Character = character;
                Main.reference = owner;
                Main.IronPillBloodReserved = reserved;
                CustomStageAllocator.ResetResourceTargets();
            }
        }

        private static void CheckAppliedTransitions()
        {
            var c = Fresh();
            var owner = Main.reference;
            var first = NguStage();
            var second = NguStage();
            first.DurationSeconds = second.DurationSeconds = 10;
            var plan = new CustomPlan {Name = "receipts", AutoIronPillEnabled = false, StageLoopStartIndex = -1,
                Stages = new[] {first, second}};
            owner.SetTimingForChecks(100, 0);
            var active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageIndex == 0 && active.StageElapsedSeconds == 0 && active.RemainingSeconds == 10,
                "late activation selects fixed work with no historical rebirth credit");
            owner.SetTimingForChecks(110, 10);
            active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageElapsedSeconds == 0, "selection reads never manufacture an application receipt");

            Main.Settings.ManageMagic = false;
            owner.ApplyStageForChecks(plan, active);
            Assert(!CustomStageAllocator.LastStageApplicationSucceeded, "partial managed allocation has no coordinator receipt");
            owner.SetTimingForChecks(120, 20);
            active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageElapsedSeconds == 0, "partial allocation cannot arm fixed credit");
            Main.Settings.ManageMagic = true;
            c.NGUController.NGUMagic[0].ThrowOnUpdate = true;
            var failed = false;
            try { owner.ApplyStageForChecks(plan, active); }
            catch (InvalidOperationException) { failed = true; }
            Assert(!failed && CustomStageAllocator.LastStageApplicationSucceeded
                && CustomStageAllocator.LastStageResourceOutcome.Disposition == MutationDisposition.ModelAppliedRenderFailed,
                "complete verified accounting arms the coordinator even when its native renderer fails");
            c.NGUController.NGUMagic[0].ThrowOnUpdate = false;
            owner.Blocked = true;
            owner.SetTimingForChecks(125, 25);
            active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageElapsedSeconds == 0, "an independent ownership block still disarms credit despite verified model accounting");
            owner.Blocked = false;

            owner.ApplyStageForChecks(plan, active);
            owner.SetTimingForChecks(130, 27);
            active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageElapsedSeconds == 2 && active.RemainingSeconds == 8,
                "successful real allocation credits only the slower independent clock");
            owner.InvalidateCustomRuntimePlanCache();
            owner.SetTimingForChecks(135, 32);
            active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageElapsedSeconds == 2, "cache invalidation preserves earned work but leaves it unarmed");
            owner.Blocked = true;
            owner.ApplyStageForChecks(plan, active);
            Assert(CustomStageAllocator.LastStageApplicationSucceeded, "fixture has a real receipt while coordinator ownership is blocked");
            owner.SetTimingForChecks(140, 37);
            active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageElapsedSeconds == 2, "receipt cannot bypass coordinator ownership");
            owner.Blocked = false;
            owner.ApplyStageForChecks(plan, active);
            owner.SetTimingForChecks(145, 42);
            active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageIndex == 0 && active.StageElapsedSeconds == 7, "short fixed row cannot resync before earning its full duration");
            var stale = active;
            owner.SetTimingForChecks(148, 45);
            active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageIndex == 1 && active.StageElapsedSeconds == 0 && active.RemainingSeconds == 10,
                "completed applied fixed work selects the next row without spilling credit");
            owner.SetTimingForChecks(150, 47);
            var state = owner.SchedulerStateForChecks;
            for (var i = 0; i < 3; i++) CustomStageAllocator.GetSchedulerSnapshot(plan, 150);
            Assert(state == owner.SchedulerStateForChecks, "unapplied fixed snapshots leave actual scheduler state unchanged");
            active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageElapsedSeconds == 0, "new fixed selection stays unarmed until its own actual application");
            owner.SetTimingForChecks(152, 49);
            active = CustomStageAllocator.GetActiveStage(plan);
            Assert(active.StageElapsedSeconds == 0, "snapshot and selection polling cannot arm future fixed credit");
            failed = false;
            try { owner.ApplyStageForChecks(plan, stale); }
            catch (InvalidOperationException) { failed = true; }
            Assert(failed, "a stale selection cannot issue a receipt for the new row");
            owner.ApplyStageForChecks(plan, active);
            owner.SetTimingForChecks(162, 59);
            CustomStageAllocator.GetActiveStage(plan);
            var snapshot = CustomStageAllocator.GetSchedulerSnapshot(plan, 162);
            Assert(snapshot.SchedulerMode == "stages" && snapshot.ScheduleComplete && snapshot.ProtectedWorkComplete
                && snapshot.StageElapsedSeconds == 10 && snapshot.ProtectedSecondsRemaining == 0,
                "both fixed rows complete only after their own successful allocator applications");
        }
    }
}
