using System;
using System.Reflection;

namespace NGUInjector.Managers
{
    internal static class MoneyPitManager
    {
        private const double MinimumMoneyPitGold = 200000.0;

        internal static string CheckMoneyPit()
        {
            if (Main.Character.pit.pitTime.totalseconds < Main.Character.pitController.currentPitTime())
                return "Money Pit skipped: pit is not ready.";

            var effectiveThreshold = Math.Max(MinimumMoneyPitGold, Main.Settings.MoneyPitThreshold);
            if (Main.Character.realGold <= effectiveThreshold)
                return $"Money Pit skipped: gold is below {effectiveThreshold:0}.";

            DiggerManager.UpdateCheapestDigger(true);
            DiggerManager.UpgradeCheapestDigger(true, effectiveThreshold);

            if (Main.Character.realGold <= effectiveThreshold)
                return $"Money Pit skipped: digger upgrades used gold down to the {effectiveThreshold:0} reserve.";

            string result;
            if (Main.Character.realGold >= 1e50 && Main.Settings.ManageMagic && Main.Character.wishes.wishes[4].level > 0)
            {
                Main.Character.removeMostMagic();
                for (var i = Main.Character.bloodMagic.ritual.Count - 1; i >= 0; i--)
                {
                    Main.Character.bloodMagicController.bloodMagics[i].cap();
                }

                if (!DiggerManager.CanSwap())
                    return "Money Pit skipped: digger swap is locked.";
                DiggerManager.SaveDiggers();
                DiggerManager.EquipDiggers(new[] {10});
                result = DoMoneyPit();
                DiggerManager.RestoreDiggers();
            }
            else
            {
                result = DoMoneyPit();
            }

            return result;
        }

        private static string DoMoneyPit()
        {
            var controller = Main.Character.pitController;
            typeof(PitController).GetMethod("engage", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(controller, null);

            var result = $"Money Pit Reward: {controller.pitText.text}";
            Main.LogPitSpin(result);
            return result;
        }

        internal static string DoDailySpin()
        {
            if (Main.Character.daily.spinTime.totalseconds < Main.Character.dailyController.targetSpinTime())
                return "Daily Spin skipped: spin is not ready.";

            Main.Character.dailyController.startNoBullshitSpin();
            var result = Main.Character.dailyController.outcomeText.text;
            var message = $"Daily Spin Reward: {result}";
            Main.LogPitSpin(message);
            return message;
        }
    }
}
