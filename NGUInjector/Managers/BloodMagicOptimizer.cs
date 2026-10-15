using System;

namespace NGUInjector.Managers
{
    internal class BloodMagicOptimizerResult
    {
        internal bool Success { get; set; }
        internal string Message { get; set; }
        internal int RitualIndex { get; set; }
        internal long MagicAllocated { get; set; }
        internal double BloodPerSecond { get; set; }
        internal double BloodMagicGoldPerSecond { get; set; }
        internal double GrossGoldPerSecond { get; set; }
    }

    internal static class BloodMagicOptimizer
    {
        internal static BloodMagicOptimizerResult OptimizeBest()
        {
            return OptimizeBest(GetAvailableMagicForFullAllocation());
        }

        internal static BloodMagicOptimizerResult OptimizeTarget(int ritualIndex)
        {
            return OptimizeTarget(ritualIndex, GetAvailableMagicForFullAllocation());
        }

        internal static BloodMagicOptimizerResult OptimizeAndAllocate()
        {
            return OptimizeAndAllocate(0);
        }

        internal static BloodMagicOptimizerResult OptimizeAndAllocate(int settleMilliseconds)
        {
            var preview = OptimizeBest();
            if (!preview.Success)
                return preview;

            try
            {
                var c = Main.Character;
                c.removeMostMagic();
                if (settleMilliseconds > 0)
                    System.Threading.Thread.Sleep(settleMilliseconds);

                var allocation = Math.Max(0, c.magic.idleMagic);
                if (allocation <= 0)
                {
                    return Fail("Blood Magic skipped: no idle magic after clearing allocations.");
                }

                var result = OptimizeBest(allocation);
                if (!result.Success)
                    return result;

                SetInput(allocation);
                c.bloodMagicController.bloodMagics[result.RitualIndex].add();
                c.bloodMagicController.updateMenu();
                result.MagicAllocated = allocation;
                result.Message = $"Blood Magic ritual {result.RitualIndex}: {allocation:n0} magic, {result.BloodPerSecond:0.###}/s blood, gold {result.GrossGoldPerSecond:0.###}/s - {result.BloodMagicGoldPerSecond:0.###}/s.";
                return result;
            }
            catch (Exception e)
            {
                return Fail(Main.LogError("Allocate Blood Magic", e));
            }
        }

        internal static BloodMagicOptimizerResult OptimizeAndAllocateTarget(int ritualIndex, int settleMilliseconds)
        {
            var preview = OptimizeTarget(ritualIndex);
            if (!preview.Success)
                return preview;

            try
            {
                var c = Main.Character;
                c.removeMostMagic();
                if (settleMilliseconds > 0)
                    System.Threading.Thread.Sleep(settleMilliseconds);

                var allocation = Math.Max(0, c.magic.idleMagic);
                if (allocation <= 0)
                    return Fail("Blood Magic skipped: no idle magic after clearing allocations.");

                var result = OptimizeTarget(ritualIndex, allocation);
                if (!result.Success)
                    return result;

                SetInput(allocation);
                c.bloodMagicController.bloodMagics[result.RitualIndex].add();
                c.bloodMagicController.updateMenu();
                result.MagicAllocated = allocation;
                result.Message = $"Pinned Blood Magic ritual {result.RitualIndex}: {allocation:n0} magic, {result.BloodPerSecond:0.###}/s blood, gold {result.GrossGoldPerSecond:0.###}/s - {result.BloodMagicGoldPerSecond:0.###}/s.";
                return result;
            }
            catch (Exception e)
            {
                return Fail(Main.LogError("Allocate pinned Blood Magic", e));
            }
        }

        internal static BloodMagicOptimizerResult OptimizeAndAllocateIdleMagic()
        {
            try
            {
                var c = Main.Character;
                var allocation = Math.Max(0, c.magic.idleMagic);
                if (allocation <= 0)
                {
                    return Fail("Blood Magic skipped: no idle magic after Wandoos cap.");
                }

                var result = OptimizeBest(allocation);
                if (!result.Success)
                    return result;

                SetInput(allocation);
                c.bloodMagicController.bloodMagics[result.RitualIndex].add();
                c.bloodMagicController.updateMenu();
                result.MagicAllocated = allocation;
                result.Message = $"Allocated Wandoos overflow to Blood Magic ritual {result.RitualIndex}: {allocation:n0} magic, {result.BloodPerSecond:0.###}/s blood.";
                return result;
            }
            catch (Exception e)
            {
                return Fail(Main.LogError("Allocate idle Blood Magic overflow", e));
            }
        }

        internal static BloodMagicOptimizerResult OptimizeBest(long magicAllocation)
        {
            return OptimizeBest(magicAllocation, Math.Max(0d, Main.Character.grossGoldPerSecond()));
        }

        internal static BloodMagicOptimizerResult OptimizeBest(long magicAllocation, double goldBudgetPerSecond)
        {
            var c = Main.Character;
            if (!c.buttons.bloodMagic.interactable)
            {
                return Fail("Blood Magic skipped: locked.");
            }

            if (magicAllocation <= 0)
            {
                return Fail("Blood Magic skipped: no magic available.");
            }

            var ritualCount = Math.Min(c.bloodMagic.ritual.Count, c.bloodMagicController.bloodMagics.Length);
            ritualCount = Math.Min(ritualCount, c.bloodMagicController.ritualsUnlocked());
            if (ritualCount <= 0)
            {
                return Fail("Blood Magic skipped: no rituals unlocked.");
            }

            var grossGoldPerSecond = Math.Max(0d, c.grossGoldPerSecond());
            var bestRitual = -1;
            var bestBloodPerSecond = 0d;
            var bestGoldPerSecond = 0d;

            for (var i = 0; i < ritualCount; i++)
            {
                var ritual = c.bloodMagic.ritual[i];
                var controller = c.bloodMagicController.bloodMagics[i];
                if (c.bossID <= ritual.boss)
                    continue;

                if (ritual.progress <= 0 && c.realGold <= controller.currentCost())
                    continue;

                var fillsPerSecond = BarFillsPerSecond(i, magicAllocation);
                if (fillsPerSecond <= 0)
                    continue;

                var bloodMagicGoldPerSecond = controller.currentCost() * fillsPerSecond;
                if (Math.Min(grossGoldPerSecond, goldBudgetPerSecond) - bloodMagicGoldPerSecond <= 0)
                    continue;

                var bloodPerSecond = controller.bloodAdded() * fillsPerSecond;
                if (bloodPerSecond <= bestBloodPerSecond && (Math.Abs(bloodPerSecond - bestBloodPerSecond) > 0.000001 || i <= bestRitual))
                    continue;

                bestRitual = i;
                bestBloodPerSecond = bloodPerSecond;
                bestGoldPerSecond = bloodMagicGoldPerSecond;
            }

            if (bestRitual < 0)
            {
                return Fail($"Blood Magic skipped: no ritual keeps gold positive at {grossGoldPerSecond:0.###}/s gross GPS.");
            }

            return new BloodMagicOptimizerResult
            {
                Success = true,
                RitualIndex = bestRitual,
                MagicAllocated = magicAllocation,
                BloodPerSecond = bestBloodPerSecond,
                BloodMagicGoldPerSecond = bestGoldPerSecond,
                GrossGoldPerSecond = grossGoldPerSecond,
                Message = $"Best Blood Magic ritual {bestRitual}: {bestBloodPerSecond:0.###}/s blood, gold {grossGoldPerSecond:0.###}/s - {bestGoldPerSecond:0.###}/s."
            };
        }

        internal static BloodMagicOptimizerResult OptimizeTarget(int ritualIndex, long magicAllocation)
        {
            return OptimizeTarget(ritualIndex, magicAllocation, Math.Max(0d, Main.Character.grossGoldPerSecond()));
        }

        internal static BloodMagicOptimizerResult OptimizeTarget(int ritualIndex, long magicAllocation, double goldBudgetPerSecond)
        {
            var c = Main.Character;
            if (!c.buttons.bloodMagic.interactable)
                return Fail("Blood Magic skipped: locked.");

            if (magicAllocation <= 0)
                return Fail("Blood Magic skipped: no magic available.");

            var ritualCount = Math.Min(c.bloodMagic.ritual.Count, c.bloodMagicController.bloodMagics.Length);
            ritualCount = Math.Min(ritualCount, c.bloodMagicController.ritualsUnlocked());
            if (ritualIndex < 0 || ritualIndex >= ritualCount)
                return Fail("Pinned Blood Magic ritual is locked or invalid.");

            var ritual = c.bloodMagic.ritual[ritualIndex];
            var controller = c.bloodMagicController.bloodMagics[ritualIndex];
            if (c.bossID <= ritual.boss)
                return Fail("Pinned Blood Magic ritual is locked by boss progress.");

            if (ritual.progress <= 0 && c.realGold <= controller.currentCost())
                return Fail("Pinned Blood Magic ritual is unaffordable to start.");

            var fillsPerSecond = BarFillsPerSecond(ritualIndex, magicAllocation);
            if (fillsPerSecond <= 0)
                return Fail("Pinned Blood Magic ritual has no measurable progress.");

            var grossGoldPerSecond = Math.Max(0d, c.grossGoldPerSecond());
            var bloodMagicGoldPerSecond = controller.currentCost() * fillsPerSecond;
            if (Math.Min(grossGoldPerSecond, goldBudgetPerSecond) - bloodMagicGoldPerSecond <= 0)
                return Fail($"Pinned Blood Magic ritual would exceed gross GPS {grossGoldPerSecond:0.###}/s.");

            var bloodPerSecond = controller.bloodAdded() * fillsPerSecond;
            if (bloodPerSecond <= 0)
                return Fail("Pinned Blood Magic ritual would produce no blood.");

            return new BloodMagicOptimizerResult
            {
                Success = true,
                RitualIndex = ritualIndex,
                MagicAllocated = magicAllocation,
                BloodPerSecond = bloodPerSecond,
                BloodMagicGoldPerSecond = bloodMagicGoldPerSecond,
                GrossGoldPerSecond = grossGoldPerSecond,
                Message = $"Pinned Blood Magic ritual {ritualIndex}: {bloodPerSecond:0.###}/s blood, gold {grossGoldPerSecond:0.###}/s - {bloodMagicGoldPerSecond:0.###}/s."
            };
        }

        private static long GetAvailableMagicForFullAllocation()
        {
            var c = Main.Character;
            return Math.Max(c.magic.curMagic, c.magic.idleMagic);
        }

        private static double BarFillsPerSecond(int ritualIndex, long magicAllocation)
        {
            var progressPerTick = ProgressPerTick(ritualIndex, magicAllocation);
            if (progressPerTick < 1e-9)
                return 0d;

            return 50d / Math.Ceiling(1d / Math.Min(progressPerTick, 1d));
        }

        private static double ProgressPerTick(int ritualIndex, long magicAllocation)
        {
            var c = Main.Character;
            double progress = 0d;
            if (c.settings.rebirthDifficulty == difficulty.normal)
            {
                progress = magicAllocation * (double) c.totalMagicPower() / 50000.0 / c.bloodMagicController.normalSpeedDividers[ritualIndex];
            }
            else if (c.settings.rebirthDifficulty == difficulty.evil)
            {
                progress = magicAllocation * (double) c.totalMagicPower() / 50000.0 / c.bloodMagicController.evilSpeedDividers[ritualIndex];
            }
            else if (c.settings.rebirthDifficulty == difficulty.sadistic)
            {
                progress = magicAllocation * (double) c.totalMagicPower() / c.bloodMagicController.sadisticSpeedDividers[ritualIndex];
            }

            if (c.settings.rebirthDifficulty >= difficulty.sadistic)
            {
                progress /= c.bloodMagicController.bloodMagics[ritualIndex].sadisticDivider();
            }

            progress *= c.bloodMagicController.bloodMagics[ritualIndex].totalBloodMagicSpeedBonus();
            if (progress <= 1e-9)
                return 0d;

            return Math.Min(progress, 3.40282346638529E+38);
        }

        private static BloodMagicOptimizerResult Fail(string message)
        {
            return new BloodMagicOptimizerResult {Success = false, Message = message, RitualIndex = -1};
        }

        private static void SetInput(long val)
        {
            var c = Main.Character;
            c.energyMagicPanel.energyRequested.text = Math.Max(0, val).ToString();
            c.energyMagicPanel.validateInput();
        }
    }
}
