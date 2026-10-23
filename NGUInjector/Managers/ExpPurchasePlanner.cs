using System;

namespace NGUInjector.Managers
{
    internal static class ExpPurchasePlanner
    {
        // Order: energy power/cap/bars, then magic power/cap/bars.
        internal static readonly long[] Quanta = { 1, 250, 1, 1, 250, 1 };
        internal static readonly long[] Costs = { 150, 1, 80, 450, 3, 240 };

        internal static long[] Plan(decimal[] stats, int[] ratios, bool[] unlocked, long budget, long capLimit, long powerBarLimit)
        {
            if (stats.Length != 6 || ratios.Length != 8 || unlocked.Length != 6)
                throw new ArgumentException("Expected six BASE stats and eight positive ratios.");
            foreach (var ratio in ratios)
                if (ratio < 1 || ratio > 1000000000) throw new ArgumentOutOfRangeException(nameof(ratios));

            var result = new long[6];
            if (budget <= 0) return result;
            var weights = new[]
            {
                (decimal)ratios[0] * ratios[2] / ratios[3], (decimal)ratios[0],
                (decimal)ratios[0] * ratios[4] / ratios[3],
                (decimal)ratios[1] * ratios[5] / ratios[6], (decimal)ratios[1],
                (decimal)ratios[1] * ratios[7] / ratios[6]
            };
            var maximum = new long[6];
            decimal high = decimal.MaxValue;
            for (var i = 0; i < 6; i++)
            {
                if (stats[i] < 0) throw new ArgumentOutOfRangeException(nameof(stats));
                if (!unlocked[i]) continue;
                var cap = i % 3 == 1;
                var headroom = Math.Max(0m, (cap ? capLimit : powerBarLimit) - stats[i]);
                // Custom power/bar inputs are ints; bound each pass without a per-unit purchase loop.
                var limit = Math.Min(headroom, cap ? long.MaxValue : 1000000000m);
                maximum[i] = (long)(decimal.Floor(limit / Quanta[i]) * Quanta[i]);
                if (maximum[i] > 0)
                {
                    var affordable = Math.Min(maximum[i], (decimal)(budget / Costs[i]) * Quanta[i]);
                    // Stop at a transaction boundary, rather than treating a per-pass int limit
                    // as permanent saturation and buying the other stats far ahead of it.
                    var bound = Math.Min(maximum[i], affordable + Quanta[i]);
                    high = Math.Min(high, (stats[i] + bound) / weights[i]);
                }
            }
            if (high == decimal.MaxValue) return result;

            // Raise the lowest ratio-normalized BASE stats together. Search quantized purchase
            // costs, not EXP shares: magic costs three times as much for the same BASE increase.
            decimal low = 0;
            for (var pass = 0; pass < 192; pass++)
            {
                var level = pass == 0 ? high : low + (high - low) / 2;
                if (pass > 0 && (level == low || level == high)) break;
                var candidate = new long[6];
                decimal cost = 0;
                for (var i = 0; i < 6; i++)
                {
                    if (maximum[i] == 0) continue;
                    // Compare before multiplying to keep extreme user ratios within decimal range.
                    var delta = level >= (stats[i] + maximum[i]) / weights[i]
                        ? maximum[i] : Math.Max(0m, level * weights[i] - stats[i]);
                    candidate[i] = (long)(decimal.Floor(delta / Quanta[i]) * Quanta[i]);
                    cost += (decimal)(candidate[i] / Quanta[i]) * Costs[i];
                }
                if (cost <= budget)
                {
                    low = level;
                    result = candidate;
                    if (pass == 0) return result;
                }
                else high = level;
            }
            return result;
        }
    }
}
