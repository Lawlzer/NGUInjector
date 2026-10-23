using System;
using System.Collections.Generic;
using System.Reflection;
using static NGUInjector.Main;

namespace NGUInjector.Managers
{
    internal class YggdrasilManager
    {
        internal const int FruitOfPowerAlphaId = 1, FruitOfPowerBetaId = 6;
        private readonly Character _character;
        public YggdrasilManager() { _character = Main.Character; }

        internal static bool TryPrepareHarvest() => reference != null && reference.PrepareHarvestAction();
        internal static void CleanupOwnedHarvest(bool restore = true) => reference?.ReleaseHarvestAction(restore);
        internal static void CleanupStaleHarvest() { }

        internal static bool AnyHarvestable()
        {
            for (var i = 0; i < Main.Character.yggdrasil.fruits.Count; i++)
                if (Main.Character.yggdrasilController.fruits[0].harvestTier(i) > 0) return true;
            return false;
        }
        internal bool NeedsHarvest() => _character.yggdrasilController.anyFruitMaxxed();
        internal bool NeedsSwap()
        {
            var threshold = Math.Max(1, Settings.YggSwapThreshold);
            for (var i = 0; i < Main.Character.yggdrasil.fruits.Count; i++)
                if (Main.Character.yggdrasilController.fruits[0].harvestTier(i) >= threshold
                    && Main.Character.yggdrasilController.fruits[0].fruitMaxxed(i)) return true;
            return false;
        }

        internal void ManageYggHarvest(bool allowSwap)
        {
            if (!NeedsHarvest()) return;
            var swap = allowSwap && Settings.SwapYggdrasilLoadouts && Settings.YggdrasilLoadout.Length > 0 && NeedsSwap();
            try
            {
                if (swap && !TryPrepareHarvest()) return;
                HarvestIdentified(true);
            }
            finally { CleanupOwnedHarvest(); }
        }

        private static void HarvestIdentified(bool requireMaxed)
        {
            ReadTooltipLog(false);
            // Native consumeAll already traverses every fruit. Each identified leaf
            // preserves its eat/harvest choice and native reward/perk calculation.
            for (var i = 0; i < Main.Character.yggdrasil.fruits.Count; i++)
            {
                var controller = Main.Character.yggdrasilController.fruits[0];
                if (controller.harvestTier(i) < 1 || requireMaxed && !controller.fruitMaxxed(i)) continue;
                reference?.HarvestFruitAction(i, requireMaxed);
            }
            ReadTooltipLog(true);
        }

        internal static void HarvestAll() => HarvestIdentified(false);
        internal static bool ConsumeFruitOfPowerAlpha(bool requireMaxed) => reference != null && reference.HarvestFruitAction(FruitOfPowerAlphaId, requireMaxed, true);
        internal static bool ConsumeFruitOfPowerBeta(bool requireMaxed) => reference != null && reference.HarvestFruitAction(FruitOfPowerBetaId, requireMaxed, true);

        internal static string FruitName(int id)
        {
            var names = Main.Character?.yggdrasilController?.fruitName;
            return names != null && id >= 0 && id < names.Count ? names[id] : "fruit " + id;
        }

        internal static bool HasInactiveUnlockedFruit(bool usesEnergy)
        {
            var controller = Main.Character.yggdrasilController;
            if (controller?.usesEnergy == null || Main.Character.yggdrasil?.fruits == null) return false;
            for (var i = 0; i < Main.Character.yggdrasil.fruits.Count && i < controller.usesEnergy.Count; i++)
            {
                var fruit = Main.Character.yggdrasil.fruits[i];
                if (fruit.maxTier > 0 && !fruit.permCostPaid && !fruit.activated && controller.usesEnergy[i] == usesEnergy) return true;
            }
            return false;
        }

        internal static int ActivateAffordableFruits(bool logSkipped = false)
        {
            if (reference == null || Main.Character.buttons?.yggdrasil?.interactable != true) return 0;
            var count = 0;
            for (var i = 0; i < Main.Character.yggdrasil.fruits.Count; i++)
            {
                var fruit = Main.Character.yggdrasil.fruits[i];
                if (fruit.maxTier <= 0 || fruit.permCostPaid || fruit.activated) continue;
                if (reference.ActivateFruitAction(i)) count++;
                else if (logSkipped) Log("Fruit activation waiting: " + FruitName(i));
            }
            return count;
        }

        internal static void ReadTooltipLog(bool doLog)
        {
            var log = Main.Character.tooltip.log;
            var field = log.GetType().GetField("Eventlog", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            var lines = field?.GetValue(log) as List<string>;
            if (lines == null) return;
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i].EndsWith("<b></b>")) continue;
                if (doLog) LogPitSpin(lines[i]);
                lines[i] += "<b></b>";
            }
        }

        internal void CheckFruits() { if (Settings.ActivateFruits) ActivateAffordableFruits(); }
    }
}
