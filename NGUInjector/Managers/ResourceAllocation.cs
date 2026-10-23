using System;
using System.Linq;

namespace NGUInjector.Managers
{
    internal static class ResourceAllocation
    {
        internal static long[] ShareCapped(long[] caps, long available, int[] weights)
        {
            var amounts = new long[caps.Length];
            var active = Enumerable.Range(0, caps.Length).Where(i => caps[i] > 0 && weights[i] > 0).ToList();
            var remaining = Math.Max(0, available);
            while (remaining > 0 && active.Count > 0)
            {
                var totalWeight = active.Sum(i => (long)weights[i]);
                var fractions = new decimal[caps.Length];
                var shares = new long[caps.Length];
                foreach (var i in active)
                {
                    // long * Int32 fits decimal exactly. Remove the exact remainder first.
                    var scaled = (decimal)remaining * weights[i];
                    fractions[i] = scaled % totalWeight;
                    shares[i] = (long)((scaled - fractions[i]) / totalWeight);
                }
                var capped = active.Where(i => caps[i] <= shares[i]).ToArray();
                if (capped.Length > 0)
                {
                    foreach (var i in capped)
                    {
                        amounts[i] = caps[i];
                        remaining -= caps[i];
                        active.Remove(i);
                    }
                    continue;
                }
                foreach (var i in active)
                {
                    amounts[i] = shares[i];
                    remaining -= shares[i];
                }
                foreach (var i in active.OrderByDescending(i => fractions[i]).ThenBy(i => i).Take((int)remaining))
                    amounts[i]++;
                break;
            }
            return amounts;
        }

        internal static long[] SplitPair(long firstCap, long secondCap, long available, double firstWeight)
        {
            // Combat's historical split rounds the second share down, not largest-remainder.
            // Zero-weight lanes remain excluded during cap redistribution.
            if (firstWeight <= 0) return new[] { 0L, Math.Min(available, secondCap) };
            if (firstWeight >= 1) return new[] { Math.Min(available, firstCap), 0L };
            var amount = Math.Floor(available * (1 - firstWeight));
            var secondShare = Math.Min(available, amount >= long.MaxValue ? long.MaxValue : amount <= 0 ? 0 : (long)amount);
            var first = Math.Min(firstCap, available - secondShare);
            var second = Math.Min(secondCap, secondShare);
            var left = available - first - second;
            var extra = Math.Min(left, firstCap - first); first += extra; left -= extra;
            second += Math.Min(left, secondCap - second);
            return new[] { first, second };
        }
    }
}
