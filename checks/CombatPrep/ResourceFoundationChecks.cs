using System;
using System.Linq;
using System.Numerics;
using NGUInjector.Managers;

internal static partial class Program
{
    private static void CheckResourceFoundation()
    {
        Equal("EnergyPower,EnergyPower2,EnergyPower3,MagicPower,MagicPower2,MagicPower3,AllPower,EnergyCap,EnergyCap3,MagicCap,MagicCap3,AllCap,NGU,NGU2",
            string.Join(",", Enum.GetNames(typeof(ResourceSpec))), "shared resource spec wire order");
        Throws(() => ResourceProjection.NormalizeRaw(null), "raw normalization rejects null");
        foreach (var count in new[] { 0, 13, 15, 28 })
            Throws(() => ResourceProjection.NormalizeRaw(new float[count]), "raw normalization rejects wrong count " + count);
        var raw = new[] { 100f, 1000, 100000, 100, 1000, 100000, 100000, 10000, 1000000, 10000, 1000000, 1000000, 10000, 1000000 };
        var normalized = ResourceProjection.NormalizeRaw(raw);
        True(normalized.All(x => x == 1f), "all 14 literal raw unit scales normalize to one");
        True(!ReferenceEquals(raw, normalized), "raw normalization returns a defensive copy");
        Equal(100f, raw[0], "raw normalization leaves source unchanged");
        normalized[0] = 9;
        Equal(100f, raw[0], "changing normalized output does not change raw input");
        raw[1] = 7;
        Equal(1f, normalized[1], "changing raw input does not change normalized output");
        foreach (var value in new[] { 0f, -0f, float.Epsilon, -float.Epsilon, 1f, -1f, 99.99999f, 100f,
            100.00001f, 999.99994f, 1000f, 1000.00006f, 9999.999f, 10000f, 10000.001f,
            99999.99f, 100000f, 100000.01f, 999999.94f, 1000000f, 1000000.06f,
            float.MaxValue, -float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN })
        {
            var input = Enumerable.Repeat(value, 14).ToArray();
            var actual = ResourceProjection.NormalizeRaw(input);
            // Literal independent per-index divisions; do not read the production divisor table.
            var expected = new[] { value / 100f, value / 1000f, value / 100000f, value / 100f, value / 1000f,
                value / 100000f, value / 100000f, value / 10000f, value / 1000000f, value / 10000f,
                value / 1000000f, value / 1000000f, value / 10000f, value / 1000000f };
            for (var i = 0; i < actual.Length; i++)
                Equal(BitConverter.SingleToInt32Bits(expected[i]), BitConverter.SingleToInt32Bits(actual[i]), "literal raw normalization bits " + value + "/" + i);
            True(input.All(x => x.Equals(value)), "raw boundary normalization preserves source");
        }
        var random = new Random(1701822);
        foreach (var energy in new[] { true, false })
        foreach (var capacity in new[] { true, false })
        for (var i = 0; i < 160; i++)
        {
            var b = Enumerable.Range(0, 14).Select(_ => (float)(random.NextDouble() * 12)).ToArray();
            var p = new ResourceProjection { Energy = energy, Capacity = capacity,
                BaseCap = i % 7 == 0 ? long.MaxValue : random.Next(1, int.MaxValue), BasePower = i % 11 == 0 ? float.MaxValue : (float)random.NextDouble() * 1e8f,
                CapLimit = i % 3 == 0 ? long.MaxValue : 1000000000000L, PowerLimit = i % 5 == 0 ? 1e5 : 1e30,
                Itopod = (float)random.NextDouble() * 3, Macguffin = (float)random.NextDouble() * 4,
                Quirk = (float)random.NextDouble() * 2, Wish = (float)random.NextDouble() * 2,
                Potion1 = i % 2 == 0 ? 1.7f : 1, Potion2 = i % 3 == 0 ? 1.7f : 1 };
            Equal(ResourceLiteralOracle(p, b), p.Project(b), "literal native resource arithmetic " + energy + "/" + capacity + "/" + i);
        }
        foreach (var energy in new[] { true, false })
        {
            var p = new ResourceProjection { Energy = energy, Capacity = true, BaseCap = 5, CapLimit = 100 };
            var b = new float[14]; b[energy ? 7 : 9] = .5f;
            Equal(8d, p.Project(b), "midpoint rounds to even upward");
            p.BaseCap = 3; Equal(4d, p.Project(b), "midpoint rounds to even downward");
            p.Capacity = false; p.BasePower = 100; p.PowerLimit = 10; p.Potion1 = 2; p.Potion2 = 3;
            Equal(60d, p.Project(b), "potions applied after power clamp");
            p.BasePower = 0; Equal(6d, p.Project(b), "minimum before power potions");
        }

        Equal("3,2", string.Join(",", ResourceAllocation.SplitPair(10, 10, 5, .5)), "pair odd remainder belongs to first lane");
        Equal("1,4", string.Join(",", ResourceAllocation.SplitPair(1, 10, 5, .5)), "pair redistributes first cap");
        Equal("4,1", string.Join(",", ResourceAllocation.SplitPair(10, 1, 5, .5)), "pair redistributes second cap");
        Equal("0,1", string.Join(",", ResourceAllocation.SplitPair(10, 1, 5, 0)), "zero first weight is excluded");
        Equal("1,0", string.Join(",", ResourceAllocation.SplitPair(1, 10, 5, 1)), "zero second weight is excluded");
        Equal("4611686018427387903,4611686018427387904", string.Join(",", ResourceAllocation.SplitPair(long.MaxValue, long.MaxValue, long.MaxValue, .5)),
            "pair preserves historical double second-share rounding at Int64 max");
        for (var i = 0; i < 300; i++)
        {
            var caps = Enumerable.Range(0, 7).Select(_ => i % 3 == 0 ? long.MaxValue : (long)random.Next(0, 1000)).ToArray();
            var weights = Enumerable.Range(0, 7).Select(_ => random.Next(0, 5) == 0 ? 0 : i % 2 == 0 ? int.MaxValue - random.Next(10) : random.Next(100)).ToArray();
            var available = i % 3 == 0 ? long.MaxValue : random.Next(10000);
            var savedCaps = caps.ToArray(); var savedWeights = weights.ToArray();
            var actual = ResourceAllocation.ShareCapped(caps, available, weights);
            Equal(string.Join(",", ShareIntegerOracle(caps, available, weights)), string.Join(",", actual), "BigInteger largest-remainder oracle " + i);
            True(actual.Select((x, j) => x >= 0 && x <= caps[j] && (weights[j] != 0 || x == 0)).All(x => x), "weighted caps and exclusions");
            True(actual.Aggregate(BigInteger.Zero, (sum, x) => sum + x) <= available, "weighted allocation never overflows budget");
            True(caps.SequenceEqual(savedCaps) && weights.SequenceEqual(savedWeights), "sharing does not mutate inputs");
        }
        Equal("1,1,0", string.Join(",", ResourceAllocation.ShareCapped(new[] { 9L, 9L, 9L }, 2, new[] { 1, 1, 1 })), "weighted equal-remainder index tie break");
        Equal("0,0", string.Join(",", ResourceAllocation.ShareCapped(new[] { 9L, 9L }, -1, new[] { 1, 1 })), "negative available is empty");
    }

    // Independently transcribed native formulas with literal spec indexes, not production branches.
    private static double ResourceLiteralOracle(ResourceProjection p, float[] b)
    {
        double value;
        if (p.Capacity && p.Energy)
            value = (double)((float)p.BaseCap * p.Itopod * p.Macguffin) * (1d + b[7] + b[8] + b[11]);
        else if (p.Capacity)
            value = (float)p.BaseCap * p.Itopod * p.Macguffin * (1f + b[9] + b[10] + b[11]);
        else if (p.Energy)
        {
            value = p.BasePower * p.Itopod * (1f + b[0] + b[1] + b[2] + b[6]);
            value *= p.Macguffin;
        }
        else value = p.BasePower * p.Itopod * p.Macguffin * (1f + b[3] + b[4] + b[5] + b[6]);
        value *= p.Quirk; value *= p.Wish;
        if (!p.Capacity) return (float)(Math.Max(1, Math.Min(p.PowerLimit, value)) * p.Potion1 * p.Potion2);
        if (value >= p.CapLimit) return p.CapLimit;
        return Math.Min(p.CapLimit, Math.Max(1L, Convert.ToInt64(Math.Max(1, value))));
    }

    private static long[] ShareIntegerOracle(long[] caps, long available, int[] weights)
    {
        var result = new long[caps.Length];
        var active = Enumerable.Range(0, caps.Length).Where(i => caps[i] > 0 && weights[i] > 0).ToList();
        BigInteger budget = Math.Max(0, available);
        while (active.Count > 0 && budget > 0)
        {
            var total = active.Aggregate(BigInteger.Zero, (sum, i) => sum + weights[i]);
            var quotas = active.ToDictionary(i => i, i => budget * weights[i] / total);
            var full = active.Where(i => quotas[i] >= caps[i]).ToArray();
            if (full.Length > 0)
            {
                foreach (var i in full) { result[i] = caps[i]; budget -= caps[i]; active.Remove(i); }
                continue;
            }
            var order = active.OrderByDescending(i => budget * weights[i] % total).ThenBy(i => i).ToArray();
            foreach (var i in active) result[i] = (long)quotas[i];
            var left = (int)(budget - quotas.Values.Aggregate(BigInteger.Zero, (sum, x) => sum + x));
            for (var i = 0; i < left; i++) result[order[i]]++;
            break;
        }
        return result;
    }
}
