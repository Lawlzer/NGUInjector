using System;

namespace NGUInjector.Managers
{
    internal sealed class BloodGoalEstimate
    {
        internal int GoalPercent;
        internal double CurrentPercent;
        internal double? RequiredBlood;
        internal double? RemainingBlood;
        internal double BloodPerSecond;
        internal double? EstimatedSeconds;
        internal string Status;
        internal string ProductionAssumption = string.Empty;

        internal static double WholePercent(double multiplier)
        {
            // The game grants whole-percent bonuses but exposes float multipliers.
            return Math.Max(0d, Math.Round((multiplier - 1d) * 100d));
        }

        internal static BloodGoalEstimate Calculate(int goal, bool gold, double currentPercent,
            double invested, double pool, double minimumBlood, double production, int autoCount, bool casting)
        {
            var result = new BloodGoalEstimate { GoalPercent = goal, CurrentPercent = currentPercent };
            if (goal <= 0)
            {
                result.Status = "Disabled (0%)";
                return result;
            }

            var required = minimumBlood * Math.Pow(2d, (gold ? Math.Sqrt(goal) : goal) - 1d);
            if (double.IsNaN(required) || double.IsInfinity(required))
            {
                result.Status = "Goal too large to estimate";
                return result;
            }

            var split = Math.Max(1, autoCount);
            result.RequiredBlood = required;
            // Even the last small bonus deficit needs a valid minimum-sized cast.
            var castBlood = Math.Max(minimumBlood, required - invested);
            result.RemainingBlood = Math.Max(0d, castBlood - (casting ? Math.Floor(Math.Max(0d, pool) / split) : 0d));
            result.BloodPerSecond = !casting || double.IsNaN(production) || double.IsInfinity(production) ? 0d : Math.Max(0d, production) / split;
            if (currentPercent >= goal)
            {
                result.RemainingBlood = 0d;
                result.EstimatedSeconds = 0d;
                result.Status = "Goal reached";
            }
            else if (!casting)
                result.Status = "Spell off / paused";
            else if (result.RemainingBlood == 0d)
            {
                result.EstimatedSeconds = 0d;
                result.Status = "Enough blood; awaiting cast";
            }
            else if (result.BloodPerSecond <= 0d)
                result.Status = "No blood production";
            else
            {
                var seconds = result.RemainingBlood.Value / result.BloodPerSecond;
                result.EstimatedSeconds = double.IsInfinity(seconds) || double.IsNaN(seconds) ? (double?)null : seconds;
                result.Status = result.EstimatedSeconds.HasValue ? "Casting" : "Time too large to estimate";
            }
            return result;
        }
    }
}
