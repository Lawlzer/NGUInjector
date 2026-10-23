using System;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal static bool EffectiveCustomAutoBloodSpellsEnabled(CustomPlan plan)
        {
            return plan?.AutoBloodSpellsEnabled ?? Settings?.CustomAutoBloodSpellsEnabled ?? false;
        }

        internal static int EffectiveCustomBloodSpaghettiGoalPercent(CustomPlan plan)
        {
            return plan?.BloodSpaghettiGoalPercent ?? Settings?.CustomBloodSpaghettiGoalPercent ?? 0;
        }

        internal static int EffectiveCustomBloodCounterfeitGoalPercent(CustomPlan plan)
        {
            return plan?.BloodCounterfeitGoalPercent ?? Settings?.CustomBloodCounterfeitGoalPercent ?? 0;
        }

        internal static int EffectiveCustomIronPillSaveSeconds(CustomPlan plan)
        {
            return plan?.IronPillSaveSeconds ?? Settings?.CustomIronPillSaveSeconds ?? 0;
        }

        internal static double EffectiveCustomIronPillMinWorth(CustomPlan plan)
        {
            return plan?.IronPillMinWorth ?? Settings?.CustomIronPillMinWorth ?? 0d;
        }

        internal static bool CustomBloodGoalCatchUpRequested(CustomPlan plan)
        {
            // Quick focus can carry a selected plan for other settings, but blood uses globals when stopped.
            if (Settings == null || !Settings.UseCustomRebirthPlan) plan = null;
            if (Settings == null || !EffectiveCustomAutoBloodSpellsEnabled(plan) || !Settings.ManageMagic
                || reference == null || IronPillBloodReserved || reference.CustomQuickResourceRefreshBlocked(plan)
                || reference.CustomBloodGoalsSuppressed(plan)
                || Character?.buttons?.bloodMagic == null || !Character.buttons.bloodMagic.interactable)
                return false;

            return TryGetCustomBloodGoals(plan, out var loot, out var gold) && (loot || gold);
        }

        private bool CustomBloodGoalsSuppressed(CustomPlan plan)
        {
            var seconds = CustomPlannedRebirthSeconds(plan);
            return seconds > 0 && seconds <= 3600 && !CustomCasualBloodGoalStageActive(plan);
        }

        private static int CustomPlannedRebirthSeconds(CustomPlan plan)
        {
            if (plan == null) return 0;
            return Math.Max(plan.TimeTriggerEnabled ? plan.TimeTriggerSeconds : 0,
                plan.StageSchedulerEnabled ? plan.StageDurationSeconds : 0);
        }

        private static bool TryGetCustomBloodGoals(CustomPlan plan, out bool loot, out bool gold)
        {
            loot = gold = false;
            if (Settings == null || Character?.bloodMagic == null || Character.bloodMagicController == null)
                return false;
            try
            {
                var lootGoal = EffectiveCustomBloodSpaghettiGoalPercent(plan);
                var goldGoal = EffectiveCustomBloodCounterfeitGoalPercent(plan);
                loot = lootGoal > 0 && BloodGoalEstimate.WholePercent(Character.bloodMagicController.lootBonus()) < lootGoal;
                gold = goldGoal > 0 && BloodGoalEstimate.WholePercent(Character.bloodMagicController.goldBonus()) < goldGoal;
                return true;
            }
            catch (Exception e)
            {
                loot = gold = false;
                LogError("Reading ordinary blood goals", e);
                return false;
            }
        }

        private bool CustomCasualBloodGoalStageActive(CustomPlan plan)
        {
            if (CustomQuickResourceRefreshBlocked(plan)) return false;
            if (!Settings.UseCustomRebirthPlan)
                return _customSustainedFocusGoal == CustomFocusGoal.EnergyNgu;
            if (plan == null || !plan.StageSchedulerEnabled || Character?.rebirthTime == null)
                return false;

            // Inspect only: spell refresh and telemetry must never advance sequential timers.
            var snapshot = CustomStageAllocator.GetSchedulerSnapshot(plan, Character.rebirthTime.totalseconds);
            return snapshot != null && snapshot.ActiveStageIndex >= 0
                   && CustomStageAllocator.IsCasualEnergyNguStage(plan.Stages[snapshot.ActiveStageIndex]);
        }
    }
}
