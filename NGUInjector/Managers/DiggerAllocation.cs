using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUInjector.Managers
{
    internal class DiggerBudgetRequest
    {
        internal int Id;
        internal long Maximum;
        internal double Weight, BaseDrain, Growth;
        internal bool ConsumesGold = true;
    }

    internal class DiggerBudgetAllocation
    {
        internal int Id;
        internal long Level;
        internal double Drain;
    }

    internal static class DiggerAllocation
    {
        internal static double Drain(double baseDrain, double growth, long level, bool consumesGold = true)
        {
            if (level <= 0 || !consumesGold)
                return 0d;
            if (double.IsNaN(baseDrain) || double.IsInfinity(baseDrain) || baseDrain < 0d ||
                double.IsNaN(growth) || double.IsInfinity(growth) || growth < 1d)
                return double.PositiveInfinity;
            if (baseDrain == 0d)
                return 0d;
            return baseDrain * Math.Pow(growth, level - 1);
        }

        internal static long AffordableLevel(double baseDrain, double growth, long maximum, double budget, bool consumesGold = true)
        {
            if (maximum <= 0 || double.IsNaN(budget) || double.IsInfinity(budget) || budget < 0d)
                return 0;
            // Forward-drain search avoids log rounding, the level-one offset and double-to-long overflow.
            long low = 0, high = maximum;
            while (low < high)
            {
                var distance = high - low;
                var middle = low + distance / 2 + distance % 2;
                if (Drain(baseDrain, growth, middle, consumesGold) <= budget)
                    low = middle;
                else
                    high = middle - 1;
            }
            return low;
        }

        internal static DiggerBudgetAllocation[] Plan(IEnumerable<DiggerBudgetRequest> requests, int slots, double budget)
        {
            if (slots <= 0 || double.IsNaN(budget) || double.IsInfinity(budget) || budget < 0d)
                return new DiggerBudgetAllocation[0];
            var valid = (requests ?? new DiggerBudgetRequest[0]).Where(x => x != null && x.Id >= 0 &&
                x.Maximum > 0 && x.Weight > 0 && !double.IsNaN(x.Weight) && !double.IsInfinity(x.Weight)).ToArray();
            if (valid.Length == 0)
                return new DiggerBudgetAllocation[0];
            var scale = valid.Max(x => x.Weight);
            var ranked = valid.GroupBy(x => x.Id).Select(group => new DiggerBudgetRequest
            {
                Id = group.Key, Weight = group.Select(x => Math.Max(double.Epsilon, x.Weight / scale)).OrderBy(x => x).Sum(),
                Maximum = group.Min(x => x.Maximum), BaseDrain = group.First().BaseDrain,
                Growth = group.First().Growth, ConsumesGold = group.First().ConsumesGold
            }).OrderByDescending(x => x.Weight).ThenBy(x => x.Id)
                .Where(x => Drain(x.BaseDrain, x.Growth, 1, x.ConsumesGold) <= budget).ToList();

            while (ranked.Count > 0)
            {
                var selected = ranked.Take(slots).ToArray();
                var totalWeight = selected.Where(x => x.ConsumesGold).Sum(x => x.Weight);
                var rejected = selected.Where(x => x.ConsumesGold &&
                    Drain(x.BaseDrain, x.Growth, 1) > budget * (x.Weight / totalWeight)).ToArray();
                if (rejected.Length > 0)
                {
                    // Drop the lowest-ranked failure first: its replacement can make a primary's share feasible.
                    ranked.Remove(rejected[rejected.Length - 1]);
                    continue; // Vacated slots are backfilled before recomputing all shares.
                }

                var result = new List<DiggerBudgetAllocation>();
                var remaining = budget;
                foreach (var request in selected)
                {
                    var share = request.ConsumesGold ? Math.Min(remaining, budget * (request.Weight / totalWeight)) : 0d;
                    var level = AffordableLevel(request.BaseDrain, request.Growth, request.Maximum, share, request.ConsumesGold);
                    var drain = Drain(request.BaseDrain, request.Growth, level, request.ConsumesGold);
                    if (level <= 0)
                        continue;
                    result.Add(new DiggerBudgetAllocation { Id = request.Id, Level = level, Drain = drain });
                    remaining = Math.Max(0d, budget - result.Sum(x => x.Drain));
                }
                var unfunded = selected.LastOrDefault(x => !result.Any(y => y.Id == x.Id));
                if (unfunded != null)
                {
                    ranked.Remove(unfunded);
                    continue;
                }
                // Native drain sums in ID order. Verify both orders rather than trusting rounded share subtraction.
                if (result.Sum(x => x.Drain) > budget || result.OrderBy(x => x.Id).Sum(x => x.Drain) > budget)
                {
                    var last = result.Last(x => x.Drain > 0d);
                    var request = selected.First(x => x.Id == last.Id);
                    long low = 0, high = last.Level;
                    while (low < high)
                    {
                        var distance = high - low;
                        var middle = low + distance / 2 + distance % 2;
                        last.Drain = Drain(request.BaseDrain, request.Growth, middle, request.ConsumesGold);
                        if (result.Sum(x => x.Drain) <= budget && result.OrderBy(x => x.Id).Sum(x => x.Drain) <= budget)
                            low = middle;
                        else
                            high = middle - 1;
                    }
                    if (low == 0)
                    {
                        ranked.Remove(request);
                        continue;
                    }
                    last.Level = low;
                    last.Drain = Drain(request.BaseDrain, request.Growth, low, request.ConsumesGold);
                }
                return result.ToArray();
            }
            return new DiggerBudgetAllocation[0];
        }
    }
}
