using System;
using System.Linq;
using NGUInjector.Managers;

internal static class ResourceAggregationChecks
{
#if RESOURCE_AGGREGATION_CHECKS
    private static int Main()
    {
        try { Run((value, name) => { if (!value) throw new Exception(name); }); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
#endif

    internal static void Run(Action<bool, string> check)
    {
        var assertions = 0;
        var random = new Random(73159);
        var values = new[] { 0f, -0f, float.Epsilon, -float.Epsilon, .1f, 1f, -1f, 16777216f,
            float.MaxValue, -float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN }
            .Concat(new[] { 100f, 1000f, 10000f, 100000f, 1000000f, 16777216f }.SelectMany(value =>
                new[] { -1, 0, 1 }.Select(offset => BitConverter.ToSingle(BitConverter.GetBytes(Bits(value) + offset), 0)))).ToArray();
        var factors = new[] { 0d, double.Epsilon, (double)float.Epsilon, .25, .37, .375, 1, 1.0000000596046448 };
        for (var fixture = 0; fixture < 160; fixture++)
        {
            var slots = fixture % 4 == 0 ? new int[0] : fixture % 4 == 1 ? new[] { -6, 10001, 10000 }
                : new[] { -1, -2, -3, -4, -5, -6, 10000, 10001, 10002 };
            if (fixture % 4 == 3) slots = slots.Reverse().ToArray();
            var specs = slots.Select((slot, i) => Enumerable.Range(0, 14).Select(stat => fixture < values.Length
                ? values[(fixture + i + stat) % values.Length]
                : fixture < 40 ? slot == -5 ? 16777216f : (i + 1) * .13f
                : (float)(random.NextDouble() * Math.Pow(2, random.Next(-140, 120)))).ToArray()).ToArray();
            var offhand = (float)factors[fixture % factors.Length];
            var weights = slots.Select(slot => slot == -6 ? factors[fixture % factors.Length] : 1d).ToArray();
            var saved = specs.Select(x => (float[])x.Clone()).ToArray();

            // The old planner stored every product/addition, weapon first, then snapshot order.
            var stored = new float[14];
            var weapon = Array.IndexOf(slots, -5);
            if (weapon >= 0) Array.Copy(specs[weapon], stored, stored.Length);
            for (var i = 0; i < slots.Length; i++) if (i != weapon)
                for (var stat = 0; stat < stored.Length; stat++)
                    stored[stat] = (float)(stored[stat] + (float)(specs[i][stat] * (float)weights[i]));

            // The old capture kept the six-slot expression and stored after each sorted accessory.
            var native = new float[14];
            for (var stat = 0; stat < native.Length; stat++)
            {
                Func<int, float> bonus = slot => {
                    var i = Array.IndexOf(slots, slot);
                    return i < 0 ? 0f : specs[i][stat];
                };
                var value = bonus(-5) + bonus(-1) + bonus(-2) + bonus(-3) + bonus(-4) + bonus(-6) * offhand;
                foreach (var slot in slots.Where(x => x >= 10000).OrderBy(x => x)) value += bonus(slot);
                native[stat] = value;
            }

            foreach (var nativePrefix in new[] { false, true })
            {
                var raw = nativePrefix ? native : stored;
                // Independent literal tier conversions, not the production divisor table.
                var expected = new[] { raw[0] / 100f, raw[1] / 1000f, raw[2] / 100000f,
                    raw[3] / 100f, raw[4] / 1000f, raw[5] / 100000f, raw[6] / 100000f,
                    raw[7] / 10000f, raw[8] / 1000000f, raw[9] / 10000f, raw[10] / 1000000f,
                    raw[11] / 1000000f, raw[12] / 10000f, raw[13] / 1000000f };
                var actual = ResourceProjection.EquipmentBonuses(slots, (i, stat) => specs[i][stat],
                    i => nativePrefix ? offhand : (float)weights[i], nativePrefix);
                for (var stat = 0; stat < expected.Length; stat++)
                {
                    check(float.IsNaN(expected[stat]) ? float.IsNaN(actual[stat]) : Bits(actual[stat]) == Bits(expected[stat]),
                        "equipment aggregation legacy bits " + fixture + "/" + nativePrefix + "/" + stat);
                    assertions++;
                }
            }
            check(specs.Select((row, i) => row.Select(Bits).SequenceEqual(saved[i].Select(Bits))).All(x => x),
                "equipment aggregation leaves captured specs unchanged " + fixture);
            assertions++;
        }

        // Probe adjacent offhand products without moving their float-store boundary.
        foreach (var offset in new[] { -1, 0, 1 })
        {
            var offhand = BitConverter.ToSingle(BitConverter.GetBytes(Bits(.37f) + offset), 0);
            var slots = new[] { -5, -6, 10000 };
            var specs = new[] { 16777216f, 13.7f, 1.13f };
            foreach (var nativePrefix in new[] { false, true })
            {
                var raw = nativePrefix ? specs[0] + 0f + 0f + 0f + 0f + specs[1] * offhand
                    : (float)(specs[0] + (float)(specs[1] * offhand));
                raw += specs[2];
                var actual = ResourceProjection.EquipmentBonuses(slots, (i, stat) => specs[i],
                    i => i == 1 ? offhand : 1f, nativePrefix);
                check(Bits(actual[0]) == Bits(raw / 100f), "adjacent offhand product/store boundary " + offset + "/" + nativePrefix);
                assertions++;
            }
        }
        Console.WriteLine("Resource aggregation differential checks: " + assertions + " assertions");
    }

    private static int Bits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value), 0); }
}
