using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using static NGUInjector.Main;

namespace NGUInjector.Managers
{
    internal class YggdrasilManager
    {
        internal const int FruitOfPowerAlphaId = 1;
        internal const int FruitOfPowerBetaId = 6;
        private readonly Character _character;

        public YggdrasilManager()
        {
            _character = Main.Character;
        }

        internal static bool AnyHarvestable()
        {
            for (var i = 0; i < Main.Character.yggdrasil.fruits.Count; i++)
            {
                if (Main.Character.yggdrasilController.fruits[0].harvestTier(i) > 0)
                    return true;
            }

            return false;
        }

        internal bool NeedsHarvest()
        {
            return _character.yggdrasilController.anyFruitMaxxed();
        }

        internal bool NeedsSwap()
        {
            var thresh = Math.Max(1, Settings.YggSwapThreshold);
            for (var i = 0; i < Main.Character.yggdrasil.fruits.Count; i++)
            {
                if (Main.Character.yggdrasilController.fruits[0].harvestTier(i) >= thresh && Main.Character.yggdrasilController.fruits[0].fruitMaxxed(i))
                    return true;
            }

            return false;
        }

        internal void ManageYggHarvest()
        {
            //We need to harvest but we dont have a loadout to manage OR we're not managing loadout
            if (!Settings.SwapYggdrasilLoadouts || Settings.YggdrasilLoadout.Length == 0)
            {
                //Not sure why this would be true, but safety first
                if (LoadoutManager.CurrentLock == LockType.Yggdrasil)
                {
                    LoadoutManager.RestoreGear();
                    LoadoutManager.ReleaseLock();
                }

                if (DiggerManager.CurrentLock == LockType.Yggdrasil)
                {
                    DiggerManager.RestoreDiggers();
                    DiggerManager.ReleaseLock();
                }
                ActuallyHarvest();
                return;
            }

            //We dont need to harvest anymore and we've already swapped, so swap back
            if (!NeedsHarvest() && LoadoutManager.CurrentLock == LockType.Yggdrasil)
            {
                LoadoutManager.RestoreGear();
                LoadoutManager.ReleaseLock();
            }

            if (!NeedsHarvest() && DiggerManager.CurrentLock == LockType.Yggdrasil)
            {
                DiggerManager.RestoreDiggers();
                DiggerManager.ReleaseLock();
            }

            //We're managing loadouts
            if (NeedsHarvest())
            {
                if (NeedsSwap())
                {
                    if (!LoadoutManager.TryYggdrasilSwap() || !DiggerManager.TryYggSwap())
                        return;

                    Log("Equipping Loadout for Yggdrasil and Harvesting");
                }
                else
                {
                    Log("Harvesting without swap because threshold not met");
                }

                //Harvest stuff
                ActuallyHarvest();
            }
        }

        private void ActuallyHarvest()
        {
            ReadTooltipLog(false);
            var currentPage = _character.yggdrasilController.curPage;
            _character.yggdrasilController.changePage(0);
            _character.yggdrasilController.consumeAll();
            _character.yggdrasilController.changePage(1);
            _character.yggdrasilController.consumeAll();
            _character.yggdrasilController.changePage(2);
            _character.yggdrasilController.consumeAll();
            _character.yggdrasilController.changePage(currentPage);
            _character.yggdrasilController.refreshMenu();
            ReadTooltipLog(true);
        }

        internal static void HarvestAll()
        {
            ReadTooltipLog(false);
            var currentPage = Main.Character.yggdrasilController.curPage;
            Main.Character.yggdrasilController.changePage(0);
            Main.Character.yggdrasilController.consumeAll(true);
            Main.Character.yggdrasilController.changePage(1);
            Main.Character.yggdrasilController.consumeAll(true);
            Main.Character.yggdrasilController.changePage(2);
            Main.Character.yggdrasilController.consumeAll(true);
            Main.Character.yggdrasilController.changePage(currentPage);
            Main.Character.yggdrasilController.refreshMenu();
            ReadTooltipLog(true);
        }

        internal static bool ConsumeFruitOfPowerAlpha(bool requireMaxed)
        {
            return ConsumeFruitIfReady(FruitOfPowerAlphaId, "Fruit of Power alpha", requireMaxed);
        }

        internal static bool ConsumeFruitOfPowerBeta(bool requireMaxed)
        {
            return ConsumeFruitIfReady(FruitOfPowerBetaId, "Fruit of Power beta", requireMaxed);
        }

        private static bool ConsumeFruitIfReady(int fruitId, string label, bool requireMaxed)
        {
            if (!Main.Character.buttons.yggdrasil.interactable)
                return false;

            var controller = Main.Character.yggdrasilController;
            if (controller?.fruits == null || controller.fruits.Length == 0 || controller.fruits[0] == null)
                return false;

            var fruitController = controller.fruits[0];
            if (!FruitIdValid(fruitId) || fruitController.harvestTier(fruitId) < 1)
                return false;

            if (requireMaxed && !fruitController.fruitMaxxed(fruitId))
                return false;

            ReadTooltipLog(false);
            fruitController.consumeFruit(fruitId);
            controller.refreshMenu();
            ReadTooltipLog(true);
            Log($"Consumed {label}");
            return true;
        }

        internal static string FruitName(int fruitId)
        {
            try
            {
                var controller = Main.Character.yggdrasilController;
                if (controller?.fruitName != null && fruitId >= 0 && fruitId < controller.fruitName.Count)
                    return controller.fruitName[fruitId];
            }
            catch
            {
                // Diagnostics only; fall through to stable fallback names.
            }

            switch (fruitId)
            {
                case FruitOfPowerAlphaId:
                    return "Fruit of Power alpha";
                case FruitOfPowerBetaId:
                    return "Fruit of Power beta";
                default:
                    return $"fruit {fruitId}";
            }
        }

        private static bool FruitIdValid(int fruitId)
        {
            return Main.Character?.yggdrasil?.fruits != null && fruitId >= 0 && fruitId < Main.Character.yggdrasil.fruits.Count;
        }

        internal static bool HasInactiveUnlockedFruit(bool usesEnergy)
        {
            var controller = Main.Character.yggdrasilController;
            if (controller?.usesEnergy == null || Main.Character.yggdrasil?.fruits == null)
                return false;

            for (var i = 0; i < Main.Character.yggdrasil.fruits.Count && i < controller.usesEnergy.Count; i++)
            {
                var fruit = Main.Character.yggdrasil.fruits[i];
                if (fruit.maxTier > 0L && !fruit.permCostPaid && !fruit.activated && controller.usesEnergy[i] == usesEnergy)
                    return true;
            }

            return false;
        }

        internal static int ActivateAffordableFruits(bool logSkipped = false)
        {
            if (!Main.Character.buttons.yggdrasil.interactable)
                return 0;

            var controller = Main.Character.yggdrasilController;
            if (controller?.fruits == null || controller.fruits.Length == 0)
                return 0;

            var activated = 0;
            var curPage = controller.curPage;
            try
            {
                for (var i = 0; i < Main.Character.yggdrasil.fruits.Count; i++)
                {
                    var fruit = Main.Character.yggdrasil.fruits[i];
                    if (fruit.maxTier == 0L || fruit.permCostPaid || fruit.activated)
                        continue;

                    if (controller.usesEnergy[i])
                    {
                        if (Math.Max(Main.Character.curEnergy, Main.Character.idleEnergy) < controller.activationCost[i])
                        {
                            if (logSkipped)
                                LogFruitActivationSkip(i, "energy", controller.activationCost[i], Main.Character.curEnergy, Main.Character.idleEnergy);
                            continue;
                        }

                        if (Main.Character.idleEnergy < controller.activationCost[i])
                        {
                            Log($"Removing energy for {FruitName(i)} ({i})");
                            Main.Character.removeMostEnergy();
                        }

                        if (Main.Character.idleEnergy < controller.activationCost[i])
                        {
                            if (logSkipped)
                                LogFruitActivationSkip(i, "energy", controller.activationCost[i], Main.Character.curEnergy, Main.Character.idleEnergy);
                            continue;
                        }
                    }
                    else
                    {
                        if (Math.Max(Main.Character.magic.curMagic, Main.Character.magic.idleMagic) < controller.activationCost[i])
                        {
                            if (logSkipped)
                                LogFruitActivationSkip(i, "magic", controller.activationCost[i], Main.Character.magic.curMagic, Main.Character.magic.idleMagic);
                            continue;
                        }

                        if (Main.Character.magic.idleMagic < controller.activationCost[i])
                        {
                            Log($"Removing magic for {FruitName(i)} ({i})");
                            Main.Character.removeMostMagic();
                        }

                        if (Main.Character.magic.idleMagic < controller.activationCost[i])
                        {
                            if (logSkipped)
                                LogFruitActivationSkip(i, "magic", controller.activationCost[i], Main.Character.magic.curMagic, Main.Character.magic.idleMagic);
                            continue;
                        }
                    }

                    var slot = ChangeFruitPage(i);
                    controller.fruits[slot].activate(i);
                    Log($"Activated {FruitName(i)} ({i})");
                    activated++;
                }
            }
            finally
            {
                controller.changePage(curPage);
                controller.refreshMenu();
            }

            return activated;
        }

        private static void LogFruitActivationSkip(int fruitId, string resource, double cost, double current, double idle)
        {
            Log($"Skipped activating {FruitName(fruitId)} ({fruitId}): needs {cost:n0} {resource}, current {current:n0}, idle {idle:n0}");
        }

        internal static void ReadTooltipLog(bool doLog)
        {
            var bLog = Main.Character.tooltip.log;
            var type = bLog.GetType().GetField("Eventlog",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            var val = type?.GetValue(bLog);

            if (val != null)
            {
                //Add something to the end of our logs to mark them as complete
                var log = (List<string>)val;
                for (var i = log.Count - 1; i >= 0; i--)
                {
                    var line = log[i];
                    if (line.EndsWith("<b></b>")) continue;
                    if (doLog)
                    {
                        LogPitSpin(line);
                    }
                    log[i] = $"{line}<b></b>";
                }
            }
        }

        internal void CheckFruits()
        {
            if (!Settings.ActivateFruits)
                return;
            ActivateAffordableFruits();
        }

        private static int ChangeFruitPage(int slot)
        {
            var page = (int)Math.Floor((double)slot / 9);
            Main.Character.yggdrasilController.changePage(page);
            return slot - (page * 9);
        }
    }
}
