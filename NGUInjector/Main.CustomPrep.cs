using System;
using System.Collections.Generic;
using NGUInjector.AllocationProfiles.RebirthStuff;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private const int StatDiggerId = 2;
        private bool _customPreRebirthPrepRunning;
        private bool _customPreRebirthPrepCompleted;
        private bool _manualRebirthPending;

        internal bool RunCustomPreRebirthPrepIfNeeded(CustomPlan plan)
        {
            if (!Settings.UseCustomRebirthPlan || plan == null)
                return false;

            if (_customPreRebirthPrepCompleted)
                return false;

            if (_customPreRebirthPrepRunning)
            {
                if (BossPushRunning)
                    return true;

                _customPreRebirthPrepRunning = false;
                _customPreRebirthPrepCompleted = true;
                Log("Custom pre-rebirth fight prep complete");
                return false;
            }

            RunFightPrep(plan, true, true, "Automatic rebirth prep");
            _customPreRebirthPrepRunning = BossPushRunning;
            _customPreRebirthPrepCompleted = !_customPreRebirthPrepRunning;
            return _customPreRebirthPrepRunning;
        }

        internal string RunManualFightPrep()
        {
            _manualRebirthPending = false;
            ClearCustomSustainedFocus();
            var plan = CustomPlanStore?.Load(Settings.CustomPlanFile) ?? new CustomPlan();
            return RunFightPrep(plan, false, false, "Manual fight prep");
        }

        internal string RunManualRebirth()
        {
            ClearCustomSustainedFocus();
            _manualRebirthPending = true;
            return ContinueManualRebirth();
        }

        private string ContinueManualRebirth()
        {
            var plan = CustomPlanStore?.Load(Settings.CustomPlanFile) ?? new CustomPlan();

            if (_customPreRebirthPrepRunning)
            {
                if (BossPushRunning)
                    return "Manual rebirth prep is still fighting bosses; rebirth will happen automatically when boss push finishes.";

                _customPreRebirthPrepRunning = false;
                _customPreRebirthPrepCompleted = true;
                Log("Manual rebirth prep complete");
            }

            if (!_customPreRebirthPrepCompleted)
            {
                var prepSummary = RunFightPrep(plan, true, true, "Manual rebirth prep");
                _customPreRebirthPrepRunning = BossPushRunning;
                _customPreRebirthPrepCompleted = !_customPreRebirthPrepRunning;

                if (_customPreRebirthPrepRunning)
                    return prepSummary + "; boss push running, rebirth will happen automatically when it stops.";
            }

            return TryEngageManualRebirth();
        }

        private string TryEngageManualRebirth()
        {
            if (Character.bossController.isFighting || Character.bossController.nukeBoss)
                return "Manual rebirth waiting for the boss fight to fully stop.";

            var rebirth = new ManualRebirth();
            if (!rebirth.RebirthAvailable())
            {
                _manualRebirthPending = false;
                ResetCustomPreRebirthPrep();
                return "Manual rebirth unavailable; minimum rebirth time or challenge state is blocking it.";
            }

            if (_profile.DoRebirth(rebirth))
            {
                _manualRebirthPending = false;
                ResetCustomPreRebirthPrep();
                ResetCustomAutomationRuntime();
                return "Manual rebirth engaged";
            }

            return "Manual rebirth delayed by final pre-rebirth checks; will retry automatically.";
        }

        private void ProcessPendingManualRebirth()
        {
            if (!_manualRebirthPending)
                return;

            var message = ContinueManualRebirth();
            settingsForm?.ReportCustomStatus(message);
        }

        internal void ResetCustomPreRebirthPrep()
        {
            _manualRebirthPending = false;
            _customPreRebirthPrepRunning = false;
            _customPreRebirthPrepCompleted = false;
            StopBossPush();
        }

        private string RunFightPrep(CustomPlan plan, bool upgradeDiggers, bool consumePowerFruits, string summaryPrefix)
        {
            var messages = new List<string>();
            plan = plan ?? new CustomPlan();
            ResetCustomIronPillPrep();

            if (upgradeDiggers)
            {
                RunPrepStep(messages, "upgrade diggers", () =>
                {
                    DiggerManager.UpdateCheapestDigger(true);
                    DiggerManager.UpgradeCheapestDigger(true, 0);
                    return "upgraded affordable diggers";
                });
            }

            RunPrepStep(messages, "equip fight gear", () =>
            {
                var result = GearOptimizer.OptimizeAndEquip(GearOptimizationGoal.Fight, (FightScoreMode) plan.FightScoreMode);
                return result.Success ? "equipped fight gear" : result.Message;
            });

            if (consumePowerFruits)
            {
                RunPrepStep(messages, "consume Fruit of Power alpha", () =>
                    YggdrasilManager.ConsumeFruitOfPowerAlpha(plan.PrepRequireMaxedFruit)
                        ? "consumed Fruit of Power alpha"
                        : "Fruit of Power alpha not ready");

                RunPrepStep(messages, "consume Fruit of Power beta", () =>
                    YggdrasilManager.ConsumeFruitOfPowerBeta(plan.PrepRequireMaxedFruit)
                        ? "consumed Fruit of Power beta"
                        : "Fruit of Power beta not ready");
            }

            RunPrepStep(messages, "enable Fu Manchu", () =>
                BeardManager.EnableFuManchu(true) ? "enabled Fu Manchu" : "Fu Manchu unavailable");

            RunPrepStep(messages, "clear diggers", () =>
            {
                DiggerManager.ClearDiggers();
                return "cleared diggers";
            });

            RunPrepStep(messages, "enable stat digger", () =>
            {
                DiggerManager.EquipDiggers(new[] {StatDiggerId});
                return "enabled stat digger";
            });

            RunPrepStep(messages, "start boss push", () =>
            {
                StartBossPush();
                return "started boss push";
            });

            if (messages.Count == 0)
            {
                messages.Add("no actions enabled");
            }

            var summary = $"{summaryPrefix}: {string.Join(", ", messages.ToArray())}";
            Log(summary);
            return summary;
        }

        private static void RunPrepStep(List<string> messages, string stepName, Func<string> step)
        {
            try
            {
                messages.Add(step());
            }
            catch (Exception e)
            {
                messages.Add(LogError($"fight prep step '{stepName}'", e));
            }
        }
    }
}
