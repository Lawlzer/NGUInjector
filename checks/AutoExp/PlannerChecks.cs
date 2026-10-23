using System;
using System.Linq;
using System.Numerics;
using NGUInjector.Managers;
using static AutoExpProgram;

internal static class PlannerChecks
{
    internal static int[] Defaults() => new[] { 5, 1, 5, 160000, 4, 5, 160000, 4 };
    private static bool[] All() => new[] { true, true, true, true, true, true };
    private const long FullCap = 9223372036854775750L; // Largest multiple of 250 below long.MaxValue.

    internal static void Register()
    {
        Tests.Add(("planner/defaults buy five energy bundles and one magic for 13680", () =>
            Amounts(new decimal[6], 13680, new long[] { 25, 800000, 20, 5, 160000, 4 })));
        Tests.Add(("planner/balanced existing BASE stats get the same increments", () =>
            Amounts(new decimal[] { 25, 800000, 20, 5, 160000, 4 }, 13680, new long[] { 25, 800000, 20, 5, 160000, 4 })));
        Tests.Add(("planner/excess energy power is not purchased", () =>
            Amounts(new decimal[] { 100, 0, 0, 0, 0, 0 }, 9930, new long[] { 0, 800000, 20, 5, 160000, 4 })));
        Tests.Add(("planner/excess magic waits while energy catches up", () =>
            Amounts(new decimal[] { 0, 0, 0, 100, 3200000, 80 }, 8550, new long[] { 25, 800000, 20, 0, 0, 0 })));
        Tests.Add(("planner/only an existing energy cap deficit is filled", () =>
            Amounts(new decimal[] { 25, 799000, 20, 5, 160000, 4 }, 4, new long[] { 0, 1000, 0, 0, 0, 0 })));
        Tests.Add(("planner/editable independent energy and magic ratios", IndependentRatios));
        Tests.Add(("planner/ratio scales do not change purchases", ScaleInvariance));
        Tests.Add(("planner/locked magic does not consume the energy budget", () =>
            Amounts(new decimal[6], 8550, new long[] { 25, 800000, 20, 0, 0, 0 },
                unlocked: new[] { true, true, true, false, false, false })));
        Tests.Add(("planner/locked energy cap leaves power and bars eligible", () =>
            Amounts(new decimal[] { 0, 99999, 0, 0, 0, 0 }, 5350, new long[] { 25, 0, 20, 0, 0, 0 },
                unlocked: new[] { true, false, true, false, false, false })));
        Tests.Add(("planner/all stats locked", () => Amounts(new decimal[6], long.MaxValue, new long[6], unlocked: new bool[6])));
        Tests.Add(("planner/zero and negative budgets", NonpositiveBudgets));
        Tests.Add(("planner/exact price and one EXP short for all six quanta", QuantumPrices));
        Tests.Add(("planner/cap purchases round down to multiples of 250", CapRounding));
        Tests.Add(("planner/hardcaps leave no room for fractional power or partial cap quanta", Hardcaps));
        Tests.Add(("planner/at and above hardcaps never purchase", () =>
            Amounts(new decimal[] { 10, 10001, 11, 11, 10000, 10 }, long.MaxValue, new long[6], cap: 10000, powerBar: 10)));
        foreach (var stat in new[] { 0, 2, 3, 5 })
            Tests.Add(($"planner/stat {stat} int input bounded to one billion per pass", () => IntInput(stat)));
        Tests.Add(("planner/long caps and longmax budget saturate exactly", LongCaps));
        Tests.Add(("planner/extreme magic-heavy ratios with longmax budget", () => ExtremeRatios(
            new[] { 1, 1000000000, 1, 1000000000, 1, 1000000000, 1, 1000000000 },
            new long[] { 0, 0, 0, 1000000000, 0, 1000000000 })));
        Tests.Add(("planner/extreme energy-heavy ratios with longmax budget", () => ExtremeRatios(
            new[] { 1000000000, 1, 1000000000, 1, 1000000000, 1, 1000000000, 1 },
            new long[] { 1000000000, 0, 1000000000, 0, 0, 0 })));
        Tests.Add(("planner/extreme normalized range still buys one affordable energy power", () =>
            Amounts(new decimal[] { 100, 100000, 1, 100000000000000000m, 10000, 1 }, 150,
                new long[] { 1, 0, 0, 0, 0, 0 }, new[] { 1000000000, 1, 1000000000, 1, 1, 1, 1000000000, 1 })));
        Tests.Add(("planner/large balanced energy budget stops at first transaction boundary", () =>
            Amounts(new decimal[] { 5, 160000, 4, 0, 0, 0 }, 10000000000000L,
                new long[] { 1000000000, 32000000000000L, 800000000, 0, 0, 0 },
                unlocked: new[] { true, true, true, false, false, false })));
        Tests.Add(("planner/exactly affordable high endpoint saturates all aligned stats", HighEndpoint));
        Tests.Add(("planner/2000 randomized independent cost and headroom invariants", Randomized));
    }

    private static long[] Amounts(decimal[] stats, long budget, long[] expected, int[] ratios = null,
        bool[] unlocked = null, long cap = 1000000000000000000L, long powerBar = 1000000000000000000L)
    {
        ratios ??= Defaults();
        unlocked ??= All();
        var beforeStats = (decimal[])stats.Clone();
        var beforeRatios = (int[])ratios.Clone();
        var beforeUnlocked = (bool[])unlocked.Clone();
        var result = ExpPurchasePlanner.Plan(stats, ratios, unlocked, budget, cap, powerBar);
        Sequence(expected, result, "purchase amounts (E power/cap/bars, M power/cap/bars)");
        Sequence(beforeStats, stats, "planner must not mutate BASE snapshot");
        Sequence(beforeRatios, ratios, "planner must not mutate ratios");
        Sequence(beforeUnlocked, unlocked, "planner must not mutate unlocks");
        Invariants(stats, unlocked, budget, cap, powerBar, result, "concrete case");
        return result;
    }

    private static void IndependentRatios()
    {
        // E 2:1000:1, M 7:2000:3, and E:M cap 2:3. Costs: 1536 + 11682 = 13218.
        Amounts(new decimal[6], 13218, new long[] { 8, 4000, 4, 21, 6000, 9 },
            new[] { 2, 3, 2, 1000, 1, 7, 2000, 3 });
        Amounts(new decimal[6], 17430, new long[] { 50, 800000, 20, 5, 160000, 4 },
            new[] { 5, 1, 10, 160000, 4, 5, 160000, 4 });
        Amounts(new decimal[6], 15280, new long[] { 25, 800000, 40, 5, 160000, 4 },
            new[] { 5, 1, 5, 160000, 8, 5, 160000, 4 });
    }

    private static void ScaleInvariance()
    {
        foreach (var ratios in new[]
        {
            new[] { 2, 3, 2, 1000, 1, 7, 2000, 3 },
            new[] { 26, 39, 2, 1000, 1, 7, 2000, 3 },
            new[] { 2, 3, 14, 7000, 7, 7, 2000, 3 },
            new[] { 2, 3, 2, 1000, 1, 77, 22000, 33 },
            new[] { 26, 39, 14, 7000, 7, 77, 22000, 33 }
        })
            Amounts(new decimal[6], 13218, new long[] { 8, 4000, 4, 21, 6000, 9 }, ratios);
    }

    private static void NonpositiveBudgets()
    {
        foreach (var budget in new[] { 0L, -1L, long.MinValue })
            Amounts(new decimal[6], budget, new long[6]);
    }

    private static void QuantumPrices()
    {
        var prices = new long[] { 150, 1, 80, 450, 3, 240 };
        var quanta = new long[] { 1, 250, 1, 1, 250, 1 };
        for (var stat = 0; stat < 6; stat++)
        {
            var unlocked = new bool[6];
            unlocked[stat] = true;
            Amounts(new decimal[6], prices[stat] - 1, new long[6], unlocked: unlocked);
            var expected = new long[6];
            expected[stat] = quanta[stat];
            Amounts(new decimal[6], prices[stat], expected, unlocked: unlocked);
        }
    }

    private static void CapRounding()
    {
        Amounts(new decimal[6], 2, new long[] { 0, 500, 0, 0, 0, 0 }, unlocked: new[] { false, true, false, false, false, false });
        Amounts(new decimal[6], 8, new long[] { 0, 0, 0, 0, 500, 0 }, unlocked: new[] { false, false, false, false, true, false });
    }

    private static void Hardcaps()
    {
        // Align each resource's final 10:10000:10 endpoint; saturation is simultaneous,
        // not permission to buy other stats past the first transaction boundary.
        var ratios = new[] { 1, 1, 1, 1000, 1, 1, 1000, 1 };
        Amounts(new decimal[] { 9.5m, 9760, 9, 9.5m, 9760, 9 }, long.MaxValue,
            new long[] { 0, 0, 1, 0, 0, 1 }, ratios, cap: 10000, powerBar: 10);
        Amounts(new decimal[] { 8, 9750, 7, 8, 9750, 7 }, long.MaxValue,
            new long[] { 2, 250, 3, 2, 250, 3 }, ratios, cap: 10000, powerBar: 10);
    }

    private static void IntInput(int stat)
    {
        var unlocked = new bool[6];
        unlocked[stat] = true;
        var expected = new long[6];
        expected[stat] = 1000000000L;
        Amounts(new decimal[6], long.MaxValue, expected, unlocked: unlocked, powerBar: long.MaxValue);
    }

    private static void LongCaps()
    {
        var ratios = Defaults();
        ratios[0] = 1; // Equal cap weights reach their upper endpoints together.
        Amounts(new decimal[6], long.MaxValue, new long[] { 0, FullCap, 0, 0, FullCap, 0 },
            ratios, unlocked: new[] { false, true, false, false, true, false }, cap: long.MaxValue);
    }

    private static void ExtremeRatios(int[] ratios, long[] expected)
    {
        Amounts(new decimal[6], long.MaxValue, expected, ratios, cap: long.MaxValue, powerBar: long.MaxValue);
    }

    private static void HighEndpoint()
    {
        // Each stat costs 17 energy bundles or 17 triple-price magic bundles: 15708 total.
        var ratios = new[] { 1, 1, 1, 250, 1, 1, 250, 1 };
        foreach (var budget in new[] { 15708L, long.MaxValue })
            Amounts(new decimal[6], budget, new long[] { 17, 4250, 17, 17, 4250, 17 },
                ratios, cap: 4250, powerBar: 17);
    }

    private static void Randomized()
    {
        var random = new Random(13680);
        for (var sample = 0; sample < 2000; sample++)
        {
            var ratios = Enumerable.Range(0, 8).Select(_ => random.Next(4) == 0 ? 1000000000 : random.Next(1, 1000000)).ToArray();
            var unlocked = Enumerable.Range(0, 6).Select(_ => random.Next(4) != 0).ToArray();
            var cap = sample % 7 == 0 ? long.MaxValue : random.NextInt64(1000, 1000000000000L);
            var powerBar = sample % 9 == 0 ? long.MaxValue : random.NextInt64(1, 5000000000L);
            var stats = Enumerable.Range(0, 6).Select(i => (decimal)random.NextInt64(0, i % 3 == 1 ? cap : powerBar)).ToArray();
            // Include fractional BASE power, locked lanes, near/above-cap stats, and affordable small budgets.
            if (sample % 3 == 0) stats[0] += 0.5m;
            if (sample % 5 == 0) stats[1] = (decimal)cap + 1;
            var budget = sample % 11 == 0 ? long.MaxValue : random.NextInt64(0, 1000000000000L);
            var beforeStats = (decimal[])stats.Clone();
            var beforeRatios = (int[])ratios.Clone();
            var beforeUnlocked = (bool[])unlocked.Clone();
            var result = ExpPurchasePlanner.Plan(stats, ratios, unlocked, budget, cap, powerBar);
            Invariants(stats, unlocked, budget, cap, powerBar, result, "seed 13680 sample " + sample);
            Sequence(beforeStats, stats, "random BASE snapshot unchanged");
            Sequence(beforeRatios, ratios, "random ratios unchanged");
            Sequence(beforeUnlocked, unlocked, "random unlocks unchanged");
        }
    }

    private static void Invariants(decimal[] stats, bool[] unlocked, long budget, long cap, long powerBar, long[] amounts, string context)
    {
        Equal(6, amounts.Length, context + " result length");
        BigInteger cost = 0;
        for (var i = 0; i < 6; i++)
        {
            var amount = amounts[i];
            Check(amount >= 0, context + " negative amount " + i);
            Check(unlocked[i] || amount == 0, context + " locked stat " + i);
            Check(amount <= Math.Max(0m, (i % 3 == 1 ? cap : powerBar) - stats[i]), context + " headroom " + i);
            if (i % 3 == 1)
            {
                Equal(0L, amount % 250, context + " cap quantum");
                cost += (BigInteger)(amount / 250) * (i < 3 ? 1 : 3);
            }
            else
            {
                Check(amount <= 1000000000 && amount <= int.MaxValue, context + " custom int bound " + i);
                cost += (BigInteger)amount * (i % 3 == 0 ? 150 : 80) * (i < 3 ? 1 : 3);
            }
        }
        Check(cost <= BigInteger.Max(0, budget), context + " planned cost exceeds budget: " + cost);
    }
}
