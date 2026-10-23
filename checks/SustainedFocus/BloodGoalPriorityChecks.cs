using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal static class BloodGoalPriorityChecks
    {
        private static int _assertions;
        private static void Equal<T>(T expected, T actual, string name)
        {
            _assertions++;
            if (!Equals(expected, actual)) throw new Exception(name + ": expected " + expected + ", got " + actual);
        }

        private static CustomPlanStage CasualStage()
        {
            return new CustomPlanStage {
                EnergyFocus = (int) CustomStageFocus.EnergyNgu,
                MagicFocus = (int) CustomStageFocus.MagicNgu,
                GearGoal = (int) GearOptimizationGoal.EnergyNgu
            };
        }

        private static FocusCharacter Fresh()
        {
            Main.reference = new Main();
            Main.Settings = new FocusSettings {
                CustomAutoBloodSpellsEnabled = true,
                CustomBloodSpaghettiGoalPercent = 15,
                CustomBloodCounterfeitGoalPercent = 100,
                CustomPlanFile = "blood-priority-checks"
            };
            Main.IronPillBloodReserved = false;
            var c = Main.Character = new FocusCharacter();
            c.idleEnergy = 1000;
            c.magic.idleMagic = 1000;
            c.GoldPerSecond = 1000;
            c.buttons.bloodMagic.interactable = c.buttons.wandoos.interactable = true;
            for (var i = 0; i < 8; i++) c.bloodMagicController.normalSpeedDividers[i] = .001f;
            CustomStageAllocator.ResetResourceTargets();
            return c;
        }

        private static void Apply(CustomPlanStage stage)
        {
            CustomStageAllocator.ApplyStage(new CustomPlan {AutoIronPillEnabled = false, AutoRebirthEnabled = false}, stage, 0, 0, "blood-goal-checks");
        }

        private static void CheckPlanOverrides()
        {
            var c = Fresh();
            Main.Settings.UseCustomRebirthPlan = true;
            Main.Settings.CustomAutoBloodSpellsEnabled = false;
            Main.Settings.CustomIronPillSaveSeconds = 900;
            Main.Settings.CustomIronPillMinWorth = 7;
            var plan = new CustomPlan {
                StageDurationSeconds = 7200,
                AutoIronPillEnabled = false, AutoRebirthEnabled = false,
                AutoBloodSpellsEnabled = true, BloodSpaghettiGoalPercent = 0,
                BloodCounterfeitGoalPercent = 50, IronPillSaveSeconds = 0, IronPillMinWorth = 3
            };
            var other = new CustomPlan {AutoBloodSpellsEnabled = false, BloodSpaghettiGoalPercent = 90};
            Equal(true, Main.EffectiveCustomAutoBloodSpellsEnabled(plan), "plan true overrides global false");
            Equal(0, Main.EffectiveCustomBloodSpaghettiGoalPercent(plan), "explicit zero overrides positive global goal");
            Equal(50, Main.EffectiveCustomBloodCounterfeitGoalPercent(plan), "plan gold target overrides global");
            Equal(0, Main.EffectiveCustomIronPillSaveSeconds(plan), "zero plan saving time is not fallback");
            Equal(3d, Main.EffectiveCustomIronPillMinWorth(plan), "plan worth overrides global");
            Equal(false, Main.EffectiveCustomAutoBloodSpellsEnabled(other), "independent plan disabled");
            Equal(90, Main.EffectiveCustomBloodSpaghettiGoalPercent(other), "independent plan goal");
            Equal(100, Main.EffectiveCustomBloodCounterfeitGoalPercent(other), "unset gold falls back independently");
            Equal(900, Main.EffectiveCustomIronPillSaveSeconds(other), "unset time falls back independently");
            Equal(7d, Main.EffectiveCustomIronPillMinWorth(other), "unset worth falls back independently");
            CustomStageAllocator.ApplyStage(plan, CasualStage(), 0, 0, "plan-overrides");
            Equal(51L, c.bloodMagic.ritual.Sum(x => x.magic), "actual running plan enables catch-up despite disabled global");
            c.bloodMagicController.GoldBonus = 1.5f;
            CustomStageAllocator.ApplyStage(plan, CasualStage(), 0, 0, "plan-overrides");
            Equal(0L, c.bloodMagic.ritual.Sum(x => x.magic), "actual plan threshold releases allocation before global threshold");
            Main.Settings.CustomAutoBloodSpellsEnabled = true;
            CustomStageAllocator.ApplyStage(other, CasualStage(), 0, 0, "independent-disabled");
            Equal(0L, c.bloodMagic.ritual.Sum(x => x.magic), "plan disabled suppresses catch-up despite enabled global");
            Main.Settings.UseCustomRebirthPlan = false;
            Equal(true, Main.CustomBloodGoalCatchUpRequested(other), "stopped disabled plan cannot suppress Quick global catch-up");
            Main.Settings.CustomAutoBloodSpellsEnabled = false;
            Equal(false, Main.CustomBloodGoalCatchUpRequested(plan), "stopped enabled plan cannot opt Quick into blood automation");
            plan.AutoBloodSpellsEnabled = null;
            plan.BloodSpaghettiGoalPercent = null;
            plan.BloodCounterfeitGoalPercent = null;
            plan.IronPillSaveSeconds = null;
            plan.IronPillMinWorth = null;
            Equal(false, Main.EffectiveCustomAutoBloodSpellsEnabled(plan), "null enabled uses global");
            Equal(15, Main.EffectiveCustomBloodSpaghettiGoalPercent(plan), "null loot uses global");
            Equal(100, Main.EffectiveCustomBloodCounterfeitGoalPercent(plan), "null gold uses global");
            Equal(900, Main.EffectiveCustomIronPillSaveSeconds(plan), "null save uses global");
            Equal(7d, Main.EffectiveCustomIronPillMinWorth(plan), "null worth uses global");
            Equal(15, Main.EffectiveCustomBloodSpaghettiGoalPercent(null), "no plan uses globals");
            Equal(false, Main.Settings.CustomAutoBloodSpellsEnabled, "effective plan reads do not mutate globals");
        }

        private static void CheckShortPlanSuppression()
        {
            var c = Fresh();
            Main.Settings.UseCustomRebirthPlan = true;
            var casual = CasualStage();
            casual.DurationSeconds = 10;
            Func<CustomPlanStage> noncasual = () => new CustomPlanStage {
                DurationSeconds = 10, MagicFocus = (int) CustomStageFocus.MagicNgu,
                GearGoal = (int) GearOptimizationGoal.MagicNgu
            };
            var plan = new CustomPlan {
                Name = "short-blood-policy", TimeTriggerEnabled = false, StageDurationSeconds = 30,
                StageLoopStartIndex = -1, AutoRebirthEnabled = false, AutoIronPillEnabled = false,
                Stages = new[] {noncasual(), casual, noncasual()}
            };
            var owner = Main.reference;
            Equal(false, Main.CustomBloodGoalCatchUpRequested(plan), "short plan with no selected stage cannot claim a future casual exception");
            Equal(null, owner.SchedulerStateForChecks, "blood demand inspection cannot initialize the scheduler");
            owner.SetTimingForChecks(0, 0);
            var active = owner.GetProtectedStage(plan);
            Equal(0, active.StageIndex, "actual scheduler initially selects the noncasual row");
            var before = owner.SchedulerStateForChecks;
            Equal(false, Main.CustomBloodGoalCatchUpRequested(plan), "short noncasual selection suppresses unmet blood goals");
            Equal(before, owner.SchedulerStateForChecks, "suppression inspection leaves the actual scheduler unchanged");
            owner.ApplyStageForChecks(plan, active);
            Equal(true, CustomStageAllocator.LastStageApplicationSucceeded, "noncasual row obtains a real allocation receipt");
            Equal(0L, c.bloodMagic.ritual.Sum(x => x.magic), "short noncasual row does not fund catch-up rituals");
            Equal(1000L, c.NGU.magicSkills[0].magic, "short noncasual row retains all magic for its primary");

            owner.SetTimingForChecks(10, 10);
            active = owner.GetProtectedStage(plan);
            Equal(1, active.StageIndex, "actual scheduler advances to the casual row after applied work");
            before = owner.SchedulerStateForChecks;
            Equal(true, Main.CustomBloodGoalCatchUpRequested(plan), "selected casual row permits short-plan blood catch-up");
            Equal(before, owner.SchedulerStateForChecks, "casual exception inspection cannot advance stage timing");
            owner.ApplyStageForChecks(plan, active);
            Equal(true, CustomStageAllocator.LastStageApplicationSucceeded, "casual row obtains a real allocation receipt");
            Equal(51L, c.bloodMagic.ritual.Sum(x => x.magic), "actual selected casual row funds one capped catch-up ritual");
            Equal(949L, c.NGU.magicSkills[0].magic, "casual catch-up conserves the magic pool with NGU overflow");

            owner.SetTimingForChecks(20, 20);
            active = owner.GetProtectedStage(plan);
            Equal(2, active.StageIndex, "actual scheduler leaves the casual row");
            Equal(false, Main.CustomBloodGoalCatchUpRequested(plan), "short-plan suppression resumes on the next noncasual selection");
            owner.ApplyStageForChecks(plan, active);
            Equal(0L, c.bloodMagic.ritual.Sum(x => x.magic), "leaving the casual exception releases temporary catch-up magic");
            Equal(1000L, c.NGU.magicSkills[0].magic, "latest noncasual primary receives the conserved magic pool");
        }

        internal static int Run()
        {
            var settings = Main.Settings;
            var character = Main.Character;
            var owner = Main.reference;
            try
            {
                CheckPlanOverrides();
                CheckShortPlanSuppression();
                // Actual controller multipliers enter the real shared whole-percent calculation.
                var cases = new[] {
                    new {LootGoal = 15, GoldGoal = 0, Loot = 1f, Gold = 1f, Expected = 51L},
                    new {LootGoal = 0, GoldGoal = 100, Loot = 1f, Gold = 1f, Expected = 51L},
                    new {LootGoal = 15, GoldGoal = 100, Loot = 1f, Gold = 1f, Expected = 51L},
                    new {LootGoal = 15, GoldGoal = 100, Loot = 1.15f, Gold = 1f, Expected = 51L},
                    new {LootGoal = 15, GoldGoal = 100, Loot = 1f, Gold = 2f, Expected = 51L},
                    new {LootGoal = 15, GoldGoal = 100, Loot = 1.15f, Gold = 2f, Expected = 0L},
                    new {LootGoal = 0, GoldGoal = 100, Loot = 1f, Gold = 2f, Expected = 0L},
                    new {LootGoal = 15, GoldGoal = 0, Loot = 1.15f, Gold = 1f, Expected = 0L},
                    new {LootGoal = 0, GoldGoal = 0, Loot = 1f, Gold = 1f, Expected = 0L}
                };
                foreach (var test in cases)
                {
                    var c = Fresh();
                    Main.Settings.CustomBloodSpaghettiGoalPercent = test.LootGoal;
                    Main.Settings.CustomBloodCounterfeitGoalPercent = test.GoldGoal;
                    c.bloodMagicController.LootBonus = test.Loot;
                    c.bloodMagicController.GoldBonus = test.Gold;
                    Apply(CasualStage());
                    var label = test.LootGoal + "/" + test.GoldGoal + " at " + test.Loot + "/" + test.Gold;
                    Equal(test.Expected, c.bloodMagic.ritual.Sum(x => x.magic), "actual stage ritual allocation " + label);
                    Equal(1000L - test.Expected, c.NGU.magicSkills[0].magic, "actual stage magic NGU allocation " + label);
                    Equal(500L, c.NGU.skills[4].energy, "energy adventure share unchanged " + label);
                    Equal(500L, c.NGU.skills[6].energy, "energy drop share unchanged " + label);
                }

                var current = Fresh();
                var stage = CasualStage();
                stage.MagicPriorities = new[] {"NGU", "RIT-0", "RIT-1", "WAN"};
                var saved = stage.MagicPriorities.ToArray();
                var support = Main.SetupForStage(stage).Diggers.Select(x => x.Id + ":" + x.Weight).ToArray();
                Apply(stage);
                Equal(1, current.bloodMagic.ritual.Count(x => x.magic > 0), "catch-up collapses explicit rituals into one sustainable gold consumer");
                Apply(stage);
                Equal(51L, current.bloodMagic.ritual.Sum(x => x.magic), "quick reapplication retains catch-up");
                Equal(949L, current.NGU.magicSkills[0].magic, "quick reapplication preserves exact magic total");
                current.bloodMagicController.LootBonus = 1.15f;
                current.bloodMagicController.GoldBonus = 2f;
                Apply(stage);
                Equal(0L, current.bloodMagic.ritual.Sum(x => x.magic), "reached thresholds release ritual allocation");
                Equal(1000L, current.NGU.magicSkills[0].magic, "reached thresholds restore saved NGU-first allocation");
                Equal(string.Join(",", saved), string.Join(",", stage.MagicPriorities), "catch-up does not mutate saved priorities");
                Equal(string.Join(",", support), string.Join(",", Main.SetupForStage(stage).Diggers.Select(x => x.Id + ":" + x.Weight)), "catch-up never changes equipment support");
                Equal((int) GearOptimizationGoal.EnergyNgu, stage.GearGoal, "catch-up never changes gear goal");

                foreach (var gear in new[] {GearOptimizationGoal.MagicNgu, GearOptimizationGoal.AllNgu})
                {
                    current = Fresh();
                    stage = CasualStage();
                    stage.GearGoal = (int) gear;
                    if (gear == GearOptimizationGoal.MagicNgu) stage.EnergyFocus = (int) CustomStageFocus.None;
                    Apply(stage);
                    Equal(1000L, current.NGU.magicSkills[0].magic, gear + " keeps important magic NGU allocation");
                    Equal(0L, current.bloodMagic.ritual.Sum(x => x.magic), gear + " cannot enter catch-up");
                }
                foreach (var blocked in new[] {"override", "spells off", "magic unmanaged"})
                {
                    current = Fresh();
                    Main.reference.Blocked = blocked == "override";
                    Main.Settings.CustomAutoBloodSpellsEnabled = blocked != "spells off";
                    Main.Settings.ManageMagic = blocked != "magic unmanaged";
                    Apply(CasualStage());
                    Equal(0L, current.bloodMagic.ritual.Sum(x => x.magic), blocked + " cannot interfere with magic");
                }

                current = Fresh();
                current.buttons.brokenTimeMachine.interactable = true;
                stage = CasualStage();
                stage.MagicFocus = (int) CustomStageFocus.TimeMachine;
                Main.IronPillBloodReserved = true;
                Apply(stage);
                Equal(1000L, current.machine.goldMultiMagic, "reserved Time Machine gets magic before blood");
                Equal(0L, current.bloodMagic.ritual.Sum(x => x.magic), "reserved ritual cannot steal TM's magic");
                Equal(0L, current.NGU.magicSkills[0].magic, "TM exhausted budget before lower priorities");
                current = Fresh();
                current.buttons.brokenTimeMachine.interactable = true;
                current.magic.idleMagic = 51000;
                current.machine.multiTarget = 1;
                Main.IronPillBloodReserved = true;
                Apply(stage);
                Equal(50001L, current.machine.goldMultiMagic, "reserved TM reaches its buffered target before ritual spillover");
                Equal(51L, current.bloodMagic.ritual.Sum(x => x.magic), "reserved TM overflow reaches RIT second");
                Equal(948L, current.NGU.magicSkills[0].magic, "reserved TM and RIT overflow reaches original NGU fallback");

                foreach (var goal in new[] {CustomFocusGoal.EnergyNgu, CustomFocusGoal.MagicNgu, CustomFocusGoal.Ngu})
                {
                    current = Fresh();
                    Main.CustomPlanStore.Save("blood-priority-checks", new CustomPlan {AutoIronPillEnabled = false, AutoRebirthEnabled = false});
                    Main.reference.Begin(goal);
                    Main.reference.Allocate(goal);
                    var expected = goal == CustomFocusGoal.EnergyNgu ? 51L : 0L;
                    Equal(expected, current.bloodMagic.ritual.Sum(x => x.magic), goal + " Quick action uses same narrow catch-up policy");
                    Equal(1000L - expected, current.NGU.magicSkills[0].magic, goal + " Quick action keeps correct magic NGU share");
                    current.bloodMagicController.LootBonus = 1.15f;
                    current.bloodMagicController.GoldBonus = 2f;
                    Main.reference.Allocate(goal);
                    Equal(0L, current.bloodMagic.ritual.Sum(x => x.magic), goal + " Quick action releases catch-up when goals met");
                    Equal(1000L, current.NGU.magicSkills[0].magic, goal + " Quick action restores NGU-first order");
                }
                Console.WriteLine("PASS: blood goal priority allocation: " + _assertions + " assertions.");
                return _assertions;
            }
            finally
            {
                Main.Settings = settings;
                Main.Character = character;
                Main.reference = owner;
                Main.IronPillBloodReserved = false;
            }
        }
    }
}
