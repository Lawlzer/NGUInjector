using System;
using NGUInjector.AllocationProfiles.BreakpointTypes;

namespace NGUInjector.Managers
{
    internal class AugmentOptimizerResult
    {
        internal bool Success { get; set; }
        internal string Message { get; set; }
        internal int AugmentIndex { get; set; }
        internal bool UsesUpgrade { get; set; }
        internal float EnergyToAugment { get; set; }
        internal float EnergyToUpgrade { get; set; }
        internal double Seconds { get; set; }
        internal double ExpectedSeconds { get; set; }
        internal double ValuePerSecond { get; set; }
        internal double ExpectedValue { get; set; }
        internal double ProjectedGoldCost { get; set; }
        internal double ProjectedGoldBudget { get; set; }
        internal double GoldPerSecond { get; set; }
        internal double EnergyPower { get; set; }
        internal double AugmentSpeedMultiplier { get; set; }
        internal double ProjectedWork { get; set; }
        internal double ProgressFraction { get; set; }
    }

    internal static class AugmentOptimizer
    {
        private static readonly float[] AugmentRatios = {5 / 15f, 7 / 17f, 9 / 19f, 11 / 21f, 13 / 23f, 15 / 25f, 17 / 27f};
        private static readonly float[] UpgradeRatios = {10 / 15f, 10 / 17f, 10 / 19f, 10 / 21f, 10 / 23f, 10 / 25f, 10 / 27f};
        private const int DefaultExpectedSeconds = 300;

        internal static AugmentOptimizerResult OptimizeBest(int remainingSeconds)
        {
            return OptimizeBest(remainingSeconds, false);
        }

        internal static AugmentOptimizerResult OptimizeBest(int remainingSeconds, bool useFullAllocation)
        {
            var c = Main.Character;
            return OptimizeBest(remainingSeconds, useFullAllocation, (float) Math.Max(c.idleEnergy, c.curEnergy));
        }

        internal static AugmentOptimizerResult OptimizeTarget(int augmentIndex, bool useUpgrade, int remainingSeconds, bool useFullAllocation)
        {
            var c = Main.Character;
            return OptimizeTarget(augmentIndex, useUpgrade, remainingSeconds, useFullAllocation, (float) Math.Max(c.idleEnergy, c.curEnergy));
        }

        private static AugmentOptimizerResult OptimizeBest(int remainingSeconds, bool useFullAllocation, float maxAllocation)
        {
            var useUpgrades = Main.Character.bossID >= 37;
            var result = OptimizeBestMode(remainingSeconds, useFullAllocation, maxAllocation, useUpgrades);
            if (!useUpgrades || result.Success && (!result.UsesUpgrade || CanSpendUpgradeNow(result.AugmentIndex)))
                return result;

            var fallback = OptimizeBestMode(remainingSeconds, true, maxAllocation, false);
            if (!fallback.Success)
                return result;

            fallback.Message = $"Upgrade skipped: {UpgradeGoldStatus(result.AugmentIndex)} {fallback.Message}";
            return fallback;
        }

        private static AugmentOptimizerResult OptimizeBestMode(int remainingSeconds, bool useFullAllocation, float maxAllocation, bool useUpgrades)
        {
            var c = Main.Character;
            if (!c.buttons.augmentation.interactable)
            {
                return Fail("Augments are not unlocked.");
            }

            if (c.challenges.noAugsChallenge.inChallenge)
            {
                return Fail("Augments are disabled in the current challenge.");
            }

            if (maxAllocation <= 0)
            {
                return Fail("No energy is available for augments.");
            }

            var expectedSeconds = remainingSeconds > 0 ? remainingSeconds : DefaultExpectedSeconds;
            var goldPerSecond = Math.Max(0d, c.goldPerSecond());
            var energyPower = Math.Max(1d, c.totalEnergyPower());
            var bestAugment = -1;
            var bestValue = 0d;
            var bestSeconds = 0d;
            var bestExpectedSeconds = 0d;
            var bestExpectedValue = 0d;
            var bestAugEnergy = 0f;
            var bestUpgradeEnergy = 0f;
            var bestGoldCost = 0d;
            var bestGoldBudget = 0d;
            var bestSpeedMultiplier = 0d;
            var bestProjectedWork = 0d;
            var bestProgressFraction = 0d;

            for (var i = 0; i < 7; i++)
            {
                var aug = c.augmentsController.augments[i];
                if (aug.augLocked() || aug.hitAugmentTarget())
                    continue;

                if (useUpgrades && (aug.upgradeLocked() || aug.hitUpgradeTarget()))
                    continue;

                if (useUpgrades && !CanSpendUpgradeNow(i))
                    continue;

                var augEnergy = useUpgrades ? maxAllocation * AugmentRatios[i] : maxAllocation;
                var upgradeEnergy = useUpgrades ? maxAllocation * UpgradeRatios[i] : 0f;
                if (augEnergy <= 0)
                    continue;

                double seconds;
                double remaining;
                double barCost;
                float progress;
                if (useUpgrades)
                {
                    seconds = Math.Max(aug.UpgradeTimeLeftEnergyMax((long) upgradeEnergy), aug.AugTimeLeftEnergyMax((long) augEnergy));
                    remaining = aug.UpgradeTimeLeftEnergy((long) upgradeEnergy);
                    barCost = aug.getUpgradeCost();
                    progress = aug.UpgradeProgress();
                }
                else
                {
                    seconds = aug.AugTimeLeftEnergyMax((long) augEnergy);
                    remaining = aug.AugTimeLeftEnergy((long) augEnergy);
                    barCost = aug.getAugCost();
                    progress = aug.AugProgress();
                }

                if (seconds < 0.01d)
                    seconds = 0.01d;

                var secondsWorked = Math.Max(0.01d, Math.Min(seconds, expectedSeconds));
                var progressFraction = Math.Max(0.000001d, Math.Min(1d, secondsWorked / seconds));
                var projectedGoldCost = Math.Max(0d, barCost) * Math.Max(0.000001d, secondsWorked / seconds);
                var projectedGoldBudget = c.realGold + goldPerSecond * secondsWorked;

                if (projectedGoldCost > projectedGoldBudget && (progress == 0f || remaining < 10d))
                    continue;

                var value = AugmentValue(i, useUpgrades);
                var expectedValue = value * progressFraction;
                var valuePerSecond = expectedValue / secondsWorked;
                if (valuePerSecond <= bestValue)
                    continue;

                bestAugment = i;
                bestValue = valuePerSecond;
                bestSeconds = seconds;
                bestExpectedSeconds = secondsWorked;
                bestExpectedValue = expectedValue;
                bestAugEnergy = augEnergy;
                bestUpgradeEnergy = upgradeEnergy;
                bestGoldCost = projectedGoldCost;
                bestGoldBudget = projectedGoldBudget;
                bestSpeedMultiplier = AugmentSpeedDivisor(i, useUpgrades) / energyPower;
                bestProjectedWork = (augEnergy + upgradeEnergy) * energyPower * bestSpeedMultiplier * secondsWorked;
                bestProgressFraction = progressFraction;
            }

            if (bestAugment < 0)
            {
                return Fail("No affordable unlocked augment target found.");
            }

            var augmentAllocation = useFullAllocation ? bestAugEnergy : CalculateAugCap(bestAugment * 2, bestAugEnergy, maxAllocation);
            var upgradeAllocation = useUpgrades
                ? (useFullAllocation ? bestUpgradeEnergy : CalculateAugCap(bestAugment * 2 + 1, bestUpgradeEnergy, maxAllocation))
                : 0f;
            return new AugmentOptimizerResult
            {
                Success = true,
                AugmentIndex = bestAugment,
                UsesUpgrade = useUpgrades,
                EnergyToAugment = augmentAllocation,
                EnergyToUpgrade = upgradeAllocation,
                Seconds = bestSeconds,
                ExpectedSeconds = bestExpectedSeconds,
                ValuePerSecond = bestValue,
                ExpectedValue = bestExpectedValue,
                ProjectedGoldCost = bestGoldCost,
                ProjectedGoldBudget = bestGoldBudget,
                GoldPerSecond = goldPerSecond,
                EnergyPower = energyPower,
                AugmentSpeedMultiplier = bestSpeedMultiplier,
                ProjectedWork = bestProjectedWork,
                ProgressFraction = bestProgressFraction,
                Message = $"Best augment: pair {bestAugment}, {(useUpgrades ? "augment+upgrade" : "augment only")}, {bestExpectedSeconds:0.#}s expected of {bestSeconds:0.#}s to next, score {bestValue:0.###}/s, progress {bestProgressFraction * 100:0.#}%, gold {bestGoldCost:0.###}/{bestGoldBudget:0.###}."
            };
        }

        private static AugmentOptimizerResult OptimizeTarget(int augmentIndex, bool useUpgrade, int remainingSeconds, bool useFullAllocation, float maxAllocation)
        {
            var useUpgrades = useUpgrade && Main.Character.bossID >= 37;
            var result = OptimizeTargetMode(augmentIndex, useUpgrades, remainingSeconds, useFullAllocation, maxAllocation);
            if (!useUpgrades || result.Success)
                return result;

            var fallback = OptimizeTargetMode(augmentIndex, false, remainingSeconds, true, maxAllocation);
            if (!fallback.Success)
                return result;

            fallback.Message = $"Pinned upgrade skipped: {UpgradeGoldStatus(augmentIndex)} {fallback.Message}";
            return fallback;
        }

        private static AugmentOptimizerResult OptimizeTargetMode(int augmentIndex, bool useUpgrades, int remainingSeconds, bool useFullAllocation, float maxAllocation)
        {
            var c = Main.Character;
            if (!c.buttons.augmentation.interactable)
                return Fail("Augments are not unlocked.");

            if (c.challenges.noAugsChallenge.inChallenge)
                return Fail("Augments are disabled in the current challenge.");

            if (maxAllocation <= 0)
                return Fail("No energy is available for augments.");

            if (augmentIndex < 0 || augmentIndex >= c.augmentsController.augments.Length)
                return Fail("Pinned augment target is invalid.");

            var aug = c.augmentsController.augments[augmentIndex];
            if (aug.augLocked() || aug.hitAugmentTarget())
                return Fail("Pinned augment target is complete or locked.");

            if (useUpgrades && (aug.upgradeLocked() || aug.hitUpgradeTarget()))
                return Fail("Pinned augment upgrade target is complete or locked.");

            if (useUpgrades && !CanSpendUpgradeNow(augmentIndex))
                return Fail($"Pinned augment upgrade is not affordable now ({UpgradeGoldStatus(augmentIndex)})");

            var expectedSeconds = remainingSeconds > 0 ? remainingSeconds : DefaultExpectedSeconds;
            var goldPerSecond = Math.Max(0d, c.goldPerSecond());
            var energyPower = Math.Max(1d, c.totalEnergyPower());
            var augEnergy = useUpgrades ? maxAllocation * AugmentRatios[augmentIndex] : maxAllocation;
            var upgradeEnergy = useUpgrades ? maxAllocation * UpgradeRatios[augmentIndex] : 0f;
            if (augEnergy <= 0)
                return Fail("No energy is available for pinned augment target.");

            double seconds;
            double remaining;
            double barCost;
            float progress;
            if (useUpgrades)
            {
                seconds = Math.Max(aug.UpgradeTimeLeftEnergyMax((long) upgradeEnergy), aug.AugTimeLeftEnergyMax((long) augEnergy));
                remaining = aug.UpgradeTimeLeftEnergy((long) upgradeEnergy);
                barCost = aug.getUpgradeCost();
                progress = aug.UpgradeProgress();
            }
            else
            {
                seconds = aug.AugTimeLeftEnergyMax((long) augEnergy);
                remaining = aug.AugTimeLeftEnergy((long) augEnergy);
                barCost = aug.getAugCost();
                progress = aug.AugProgress();
            }

            if (seconds < 0.01d)
                seconds = 0.01d;

            var secondsWorked = Math.Max(0.01d, Math.Min(seconds, expectedSeconds));
            var progressFraction = Math.Max(0.000001d, Math.Min(1d, secondsWorked / seconds));
            var projectedGoldCost = Math.Max(0d, barCost) * Math.Max(0.000001d, secondsWorked / seconds);
            var projectedGoldBudget = c.realGold + goldPerSecond * secondsWorked;
            if (projectedGoldCost > projectedGoldBudget && (progress == 0f || remaining < 10d))
                return Fail($"Pinned augment target is unaffordable: gold {projectedGoldBudget:0.###}/{projectedGoldCost:0.###}.");

            var value = AugmentValue(augmentIndex, useUpgrades);
            var expectedValue = value * progressFraction;
            var valuePerSecond = expectedValue / secondsWorked;
            var augmentAllocation = useFullAllocation ? augEnergy : CalculateAugCap(augmentIndex * 2, augEnergy, maxAllocation);
            var upgradeAllocation = useUpgrades
                ? (useFullAllocation ? upgradeEnergy : CalculateAugCap(augmentIndex * 2 + 1, upgradeEnergy, maxAllocation))
                : 0f;

            return new AugmentOptimizerResult
            {
                Success = true,
                AugmentIndex = augmentIndex,
                UsesUpgrade = useUpgrades,
                EnergyToAugment = augmentAllocation,
                EnergyToUpgrade = upgradeAllocation,
                Seconds = seconds,
                ExpectedSeconds = secondsWorked,
                ValuePerSecond = valuePerSecond,
                ExpectedValue = expectedValue,
                ProjectedGoldCost = projectedGoldCost,
                ProjectedGoldBudget = projectedGoldBudget,
                GoldPerSecond = goldPerSecond,
                EnergyPower = energyPower,
                AugmentSpeedMultiplier = AugmentSpeedDivisor(augmentIndex, useUpgrades) / energyPower,
                ProjectedWork = (augEnergy + upgradeEnergy) * energyPower * (AugmentSpeedDivisor(augmentIndex, useUpgrades) / energyPower) * secondsWorked,
                ProgressFraction = progressFraction,
                Message = $"Pinned augment: pair {augmentIndex}, {(useUpgrades ? "augment+upgrade" : "augment only")}, {secondsWorked:0.#}s expected of {seconds:0.#}s to next, score {valuePerSecond:0.###}/s, progress {progressFraction * 100:0.#}%, gold {projectedGoldCost:0.###}/{projectedGoldBudget:0.###}."
            };
        }

        internal static bool CanSpendUpgradeNow(int augmentIndex)
        {
            var c = Main.Character;
            if (c == null || augmentIndex < 0 || augmentIndex >= c.augmentsController.augments.Length)
                return false;

            var aug = c.augmentsController.augments[augmentIndex];
            if (aug.upgradeLocked() || aug.hitUpgradeTarget())
                return false;

            var requiredGold = Math.Max(0d, aug.getUpgradeCost());
            if (requiredGold <= 0d)
                return true;

            var goldAvailableSoon = c.realGold + Math.Max(0d, c.goldPerSecond()) * 2d;
            return goldAvailableSoon >= requiredGold;
        }

        private static string UpgradeGoldStatus(int augmentIndex)
        {
            var c = Main.Character;
            if (c == null || augmentIndex < 0 || augmentIndex >= c.augmentsController.augments.Length)
                return "upgrade gold check unavailable.";

            var requiredGold = Math.Max(0d, c.augmentsController.augments[augmentIndex].getUpgradeCost());
            var goldAvailableSoon = c.realGold + Math.Max(0d, c.goldPerSecond()) * 2d;
            return $"upgrade cost {requiredGold:0.###}, available now/soon {goldAvailableSoon:0.###}.";
        }

        internal static AugmentOptimizerResult OptimizeAndAllocate(int remainingSeconds)
        {
            return OptimizeAndAllocate(remainingSeconds, false, 0);
        }

        internal static AugmentOptimizerResult OptimizeAndAllocate(int remainingSeconds, bool useFullAllocation, int settleMilliseconds)
        {
            var result = OptimizeBest(remainingSeconds, useFullAllocation);
            if (!result.Success)
                return result;

            try
            {
                var c = Main.Character;
                c.removeMostEnergy();
                if (settleMilliseconds > 0)
                    System.Threading.Thread.Sleep(settleMilliseconds);

                result = OptimizeBest(remainingSeconds, useFullAllocation);
                if (!result.Success)
                    return result;

                SetInput(result.EnergyToAugment);
                c.augmentsController.augments[result.AugmentIndex].addEnergyAug();
                if (result.UsesUpgrade)
                {
                    if (settleMilliseconds > 0)
                        System.Threading.Thread.Sleep(settleMilliseconds);

                    if (!CanSpendUpgradeNow(result.AugmentIndex))
                    {
                        SetInput(result.EnergyToUpgrade);
                        c.augmentsController.augments[result.AugmentIndex].addEnergyAug();
                        c.augmentsController.updateMenu();
                        result.Message = $"Allocated augment pair {result.AugmentIndex}: aug {result.EnergyToAugment + result.EnergyToUpgrade:0}, upgrade skipped after gold check ({UpgradeGoldStatus(result.AugmentIndex)})";
                        result.UsesUpgrade = false;
                        result.EnergyToAugment += result.EnergyToUpgrade;
                        result.EnergyToUpgrade = 0;
                        return result;
                    }

                    SetInput(result.EnergyToUpgrade);
                    c.augmentsController.augments[result.AugmentIndex].addEnergyUpgrade();
                }

                c.augmentsController.updateMenu();
                result.Message = $"Allocated augment pair {result.AugmentIndex}: aug {result.EnergyToAugment:0}, upgrade {result.EnergyToUpgrade:0}; projected gold {result.ProjectedGoldCost:0.###}/{result.ProjectedGoldBudget:0.###}.";
                return result;
            }
            catch (Exception e)
            {
                return Fail(Main.LogError("Allocate best augment", e));
            }
        }

        internal static AugmentOptimizerResult OptimizeAndAllocateTarget(int augmentIndex, bool useUpgrade, int remainingSeconds, bool useFullAllocation, int settleMilliseconds)
        {
            var result = OptimizeTarget(augmentIndex, useUpgrade, remainingSeconds, useFullAllocation);
            if (!result.Success)
                return result;

            try
            {
                var c = Main.Character;
                c.removeMostEnergy();
                if (settleMilliseconds > 0)
                    System.Threading.Thread.Sleep(settleMilliseconds);

                result = OptimizeTarget(augmentIndex, result.UsesUpgrade, remainingSeconds, useFullAllocation);
                if (!result.Success)
                    return result;

                SetInput(result.EnergyToAugment);
                c.augmentsController.augments[result.AugmentIndex].addEnergyAug();
                if (result.UsesUpgrade)
                {
                    if (settleMilliseconds > 0)
                        System.Threading.Thread.Sleep(settleMilliseconds);

                    SetInput(result.EnergyToUpgrade);
                    c.augmentsController.augments[result.AugmentIndex].addEnergyUpgrade();
                }

                c.augmentsController.updateMenu();
                result.Message = $"Allocated pinned augment pair {result.AugmentIndex}: aug {result.EnergyToAugment:0}, upgrade {result.EnergyToUpgrade:0}; projected gold {result.ProjectedGoldCost:0.###}/{result.ProjectedGoldBudget:0.###}.";
                return result;
            }
            catch (Exception e)
            {
                return Fail(Main.LogError("Allocate pinned augment", e));
            }
        }

        internal static AugmentOptimizerResult OptimizeAndAllocateIdle(int remainingSeconds)
        {
            try
            {
                var c = Main.Character;
                var idleEnergy = (float) Math.Max(0, c.idleEnergy);
                if (idleEnergy <= 0)
                {
                    return Fail("Augments skipped: no idle energy after Wandoos cap.");
                }

                var result = OptimizeBest(remainingSeconds, false, idleEnergy);
                if (!result.Success)
                    return result;

                SetInput(result.EnergyToAugment);
                c.augmentsController.augments[result.AugmentIndex].addEnergyAug();
                if (result.UsesUpgrade)
                {
                    if (!CanSpendUpgradeNow(result.AugmentIndex))
                    {
                        SetInput(result.EnergyToUpgrade);
                        c.augmentsController.augments[result.AugmentIndex].addEnergyAug();
                        c.augmentsController.updateMenu();
                        result.Message = $"Allocated Wandoos overflow to augment pair {result.AugmentIndex}: aug {result.EnergyToAugment + result.EnergyToUpgrade:0}, upgrade skipped after gold check.";
                        result.UsesUpgrade = false;
                        result.EnergyToAugment += result.EnergyToUpgrade;
                        result.EnergyToUpgrade = 0;
                        return result;
                    }

                    SetInput(result.EnergyToUpgrade);
                    c.augmentsController.augments[result.AugmentIndex].addEnergyUpgrade();
                }

                c.augmentsController.updateMenu();
                result.Message = $"Allocated Wandoos overflow to augment pair {result.AugmentIndex}: aug {result.EnergyToAugment:0}, upgrade {result.EnergyToUpgrade:0}.";
                return result;
            }
            catch (Exception e)
            {
                return Fail(Main.LogError("Allocate idle augment overflow", e));
            }
        }

        private static AugmentOptimizerResult Fail(string message)
        {
            return new AugmentOptimizerResult {Success = false, Message = message, AugmentIndex = -1};
        }

        private static double AugmentValue(int id, bool useUpgrades)
        {
            var c = Main.Character;
            var aug = c.augmentsController.augments[id];
            if (useUpgrades)
            {
                return aug.baseBoost * Math.Max(Math.Pow(c.augments.augs[aug.id].upgradeLevel + 2, 2f) + 1f, 1f) * Math.Pow(c.augments.augs[aug.id].augLevel + 500f, aug.augTierBonus())
                       - aug.baseBoost * Math.Max(Math.Pow(c.augments.augs[aug.id].upgradeLevel, 2f) + 1f, 1f) * Math.Pow(c.augments.augs[aug.id].augLevel + 1f, aug.augTierBonus());
            }

            return aug.baseBoost * aug.getUpgradeBoost() * Math.Pow(c.augments.augs[aug.id].augLevel + 500f, aug.augTierBonus())
                   - aug.baseBoost * aug.getUpgradeBoost() * Math.Pow(c.augments.augs[aug.id].augLevel + 1f, aug.augTierBonus());
        }

        private static float CalculateAugCap(int index, float allocation, float maxUsableEnergy)
        {
            var calcA = CalculateAugCapCalc(500, index, allocation, maxUsableEnergy);
            if (calcA.PPT < 1)
            {
                var calcB = CalculateAugCapCalc(calcA.GetOffset(), index, allocation, maxUsableEnergy);
                return calcB.Num;
            }

            return calcA.Num;
        }

        private static CapCalc CalculateAugCapCalc(int offset, int index, float allocation, float maxUsableEnergy)
        {
            var c = Main.Character;
            var ret = new CapCalc {Num = 0, PPT = 1};
            if (allocation <= 0)
                return ret;

            int augIndex;
            double formula;
            if (index % 2 == 0)
            {
                augIndex = index / 2;
                formula = 50000 * (1f + c.augments.augs[augIndex].augLevel + offset) / AugmentSpeedDivisor(augIndex, false);
                if (c.settings.rebirthDifficulty == difficulty.normal)
                    formula *= c.augmentsController.normalAugSpeedDividers[augIndex];
                else if (c.settings.rebirthDifficulty == difficulty.evil)
                    formula *= c.augmentsController.evilAugSpeedDividers[augIndex];
                else if (c.settings.rebirthDifficulty == difficulty.sadistic)
                    formula *= c.augmentsController.sadisticAugSpeedDividers[augIndex];
            }
            else
            {
                augIndex = (index - 1) / 2;
                formula = 50000 * (1f + c.augments.augs[augIndex].upgradeLevel + offset) / AugmentSpeedDivisor(augIndex, true);
                if (c.settings.rebirthDifficulty == difficulty.normal)
                    formula *= c.augmentsController.normalUpgradeSpeedDividers[augIndex];
                else if (c.settings.rebirthDifficulty == difficulty.evil)
                    formula *= c.augmentsController.evilUpgradeSpeedDividers[augIndex];
                else if (c.settings.rebirthDifficulty == difficulty.sadistic)
                    formula *= c.augmentsController.sadisticUpgradeSpeedDividers[augIndex];
            }

            if (formula >= c.hardCap())
                formula = c.hardCap();

            var num4 = formula <= 1.0 ? 1L : (long) formula;
            var num = (long) (num4 / (long) Math.Ceiling(num4 / (double) allocation) * 1.00000202655792);
            if (num + 1L <= long.MaxValue)
                ++num;
            if (num > maxUsableEnergy)
                num = (long) maxUsableEnergy;
            if (num < 0L)
                num = 0L;

            ret.Num = num;
            ret.PPT = (double) num / num4;
            return ret;
        }

        private static double AugmentSpeedDivisor(int augIndex, bool upgrade)
        {
            var c = Main.Character;
            var divisor = c.totalEnergyPower()
                          * (1 + c.inventoryController.bonuses[specType.Augs])
                          * c.inventory.macguffinBonuses[12]
                          * c.hacksController.totalAugSpeedBonus()
                          * c.cardsController.getBonus(cardBonus.augSpeed)
                          * c.adventureController.itopod.totalAugSpeedBonus()
                          * (1.0 + c.allChallenges.noAugsChallenge.evilCompletions() * 0.0500000007450581);

            if (c.allChallenges.noAugsChallenge.completions() >= 1)
                divisor *= 1.10000002384186;
            if (c.allChallenges.noAugsChallenge.evilCompletions() >= c.allChallenges.noAugsChallenge.maxCompletions)
                divisor *= 1.25;
            if (c.settings.rebirthDifficulty >= difficulty.sadistic)
                divisor /= c.augmentsController.augments[augIndex].sadisticDivider();

            return Math.Max(1d, divisor);
        }

        private static void SetInput(float val)
        {
            var c = Main.Character;
            c.energyMagicPanel.energyRequested.text = val.ToString("0");
            c.energyMagicPanel.validateInput();
        }
    }
}
