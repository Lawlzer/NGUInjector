using System;
using System.Diagnostics;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    // Literal game update rule, deliberately independent of the grouped production integrator.
    private static long TickOracle(PresetNgu row, double a, double seconds)
    {
        var level = (long)row.Level; var initial = level; var progress = 0f;
        var limit = row.Target < 0 ? 0 : row.Target == 0 ? row.HardCap : Math.Min(row.Target, row.HardCap);
        for (var tick = 0L; tick < (long)Math.Floor(seconds * 50) && level < limit; tick++)
        {
            progress += (float)(a / (level + 1));
            if (progress >= 1f) { level++; progress = 0f; }
        }
        return level - initial;
    }

    private static void OracleGain(double level, double a, double seconds, double target = 0, double cap = 1000000000)
    {
        var row = new PresetNgu { Level = level, Target = target, HardCap = cap, Weight = 1, Coefficient = 1 };
        var expected = TickOracle(row, a, seconds);
        var actual = LoadoutPresetPlanner.NguGain(row, a, seconds);
        var tolerance = expected * LoadoutPresetPlanner.NguRelativeError;
        Check(LoadoutPresetPlanner.Finite(actual) && actual <= expected && expected - actual <= tolerance,
            "float tick oracle: L=" + level + " a=" + a + " seconds=" + seconds + " expected=" + expected + " actual=" + actual);
    }

    private static void NguNumericChecks()
    {
        foreach (var level in new[] { 0d, 1, 10000, 999999999 })
        foreach (var a in new[] { .05, .3333333, 1, 2, 10, 200, 250, 251, 300, 10001, 500000000 })
        foreach (var seconds in new[] { .01, 1, 10, 100 }) OracleGain(level, a, seconds);
        foreach (var a in new[] { .00000005, .0000001, .000001, .0001, .1, 1, 250, 1e20 }) OracleGain(0, a, 604800);
        OracleGain(10000, 1, 604800);
        OracleGain(0, 250, 10, 300);
        OracleGain(0, 251, 10, 300);
        OracleGain(0, 1e20, 604800, 0, 37);
        foreach (var a in new[] { 250d, 251 })
            Near(LoadoutPresetPlanner.NguGain(new PresetNgu { HardCap = 1000000000 }, a, 10), 375,
                "no midpoint doubling at numerator " + a);
        Near(LoadoutPresetPlanner.NguGain(new PresetNgu { HardCap = 1000000000 }, 300, 10), 400, "one-tick to two-tick band transition");

        foreach (var progress in new[] { 1f, .5f, .33333334f, .25f, .2f, .1f, .01f, .0001f, .000001f })
        {
            var sum = 0f; var ticks = 0L;
            while (sum < 1) { sum += progress; ticks++; }
            Check(LoadoutPresetPlanner.NguTicks(progress, 1) == ticks, "ULP-run tick counting matches repeated float addition " + progress);
        }
        Check(LoadoutPresetPlanner.NguTicks(0, 1) == long.MaxValue, "zero progress cannot finish");
        Check(LoadoutPresetPlanner.NguTicks(1e-20, 1) == long.MaxValue, "float-stagnant bar cannot finish");
        var random = new Random(157);
        for (var i = 0; i < 80; i++) OracleGain(random.Next(0, 20000), Math.Pow(10, random.NextDouble() * 7 - 2), random.Next(1, 1000));

        foreach (var track in new[] { "normal", "evil", "sadistic" })
        {
            var row = new PresetNgu { HardCap = 1000000000, Level = 1000000000, Weight = 1, Coefficient = 1 };
            Near(LoadoutPresetPlanner.NguGain(row, 1e20, 10), 0, track + " hardcap yields zero levels");
            Check(LoadoutPresetPlanner.NguShares(new[] { row }, 1000001000, 1, 5)[0] == 0, track + " hardcap receives no allocation");
            row.Level--; row.Target = 2000000000;
            Near(LoadoutPresetPlanner.NguGain(row, 1e20, 10), 1, track + " target above hardcap is clipped");
        }
        var s = Snapshot(); s.EnergyNgu[0].HardCap = 10000;
        var clone = s.ProgressionCopy();
        Near(clone.EnergyNgu[0].HardCap, 10000, "progression clone preserves captured track hardcap");
        Near(Evaluate(s, Request()).Energy, 0, "hardcapped target cannot consume phase allocation");

        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            var gain = LoadoutPresetPlanner.NguGain(new PresetNgu { HardCap = 1000000000 }, 1e20, 604800);
            Near(gain, 30240000, "maximum horizon jumps whole one-tick band");
        }
        Check(timer.ElapsedMilliseconds < 2000, "maximum-horizon integration does not enumerate thirty million levels");

        // Exercise the controller's float prefix and ordered post-level factors, not only an ideal numerator.
        for (var fixture = 0; fixture < 40; fixture++)
        {
            var factors = Enumerable.Range(0, 10).Select(_ => (double)(float)(1 + random.NextDouble())).ToArray();
            factors[9] = fixture % 2 == 0 ? 3 : 1;
            var row = new PresetNgu { Level = fixture % 2 == 0 ? 10000 : 0, HardCap = 1000000000, Weight = 1,
                SpeedDivider = 17.3f, TickDivider = fixture % 3 == 0 ? 10000000 : 1, TickFactors = factors };
            row.Coefficient = factors.Aggregate(1d, (a, f) => a * f) / row.SpeedDivider / row.TickDivider;
            const float power = 201.7f; const long share = 79;
            var speed = factors[0] * 1.2; var digger = factors[4] * 1.5;
            var level = (long)row.Level; var progress = 0f;
            for (var tick = 0; tick < 5000; tick++)
            {
                double perTick = power / row.SpeedDivider * (float)share;
                perTick /= level + 1;
                for (var i = 0; i < factors.Length; i++) perTick *= i == 0 ? speed : i == 4 ? digger : factors[i];
                perTick /= row.TickDivider;
                progress += (float)perTick;
                if (progress >= 1) { level++; progress = 0; }
            }
            var expected = level - row.Level;
            var actual = LoadoutPresetPlanner.NguGain(row, LoadoutPresetPlanner.NguNumerator(row, power, share), 100, speed, digger);
            Check(LoadoutPresetPlanner.Finite(actual) && actual <= expected && expected - actual <= expected * LoadoutPresetPlanner.NguRelativeError,
                "controller-ordered NGU tick oracle " + fixture + " expected=" + expected + " actual=" + actual);
        }

        s = Snapshot(); var r = Request(); r.Horizon = 604800;
        s.EnergyNgu = Enumerable.Range(0, 9).Select(_ => new PresetNgu { HardCap = 1000000000, Coefficient = .001, Weight = 1 }).ToArray();
        s.MagicNgu = Enumerable.Range(0, 7).Select(_ => new PresetNgu { HardCap = 1000000000, Coefficient = .001, Weight = 1 }).ToArray();
        timer.Restart(); var bounded = LoadoutPresetPlanner.Plan(s, r); timer.Stop();
        Check(timer.ElapsedMilliseconds < 3500, "whole maximum-horizon mixed search includes expensive NGU integration within time budget");
        Check(bounded.Candidates.All(x => x.Score.HasValue), "maximum-horizon numeric workload stays certifiable");
        Console.WriteLine("Maximum-horizon plan: " + bounded.Evaluated + " evaluations in " + timer.ElapsedMilliseconds + " ms");
    }

    // Independently transcribed Character getters from the installed Assembly-CSharp, not captured clamp inversion.
    private static double ResourceOracle(ResourceProjection p, float[] b)
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
        if (p.Capacity)
        {
            if (value >= p.CapLimit) value = p.CapLimit;
            if (value < 1) value = 1;
            return Math.Max(1, Math.Min(p.CapLimit, Convert.ToInt64(value)));
        }
        if (value < 1) value = 1;
        if (value >= p.PowerLimit) value = p.PowerLimit;
        value *= p.Potion1; value *= p.Potion2;
        return (float)value;
    }

    private static void ResourceNumericChecks()
    {
        var random = new Random(210);
        foreach (var energy in new[] { true, false })
        foreach (var capacity in new[] { true, false })
        for (var i = 0; i < 100; i++)
        {
            var p = new ResourceProjection { Energy = energy, Capacity = capacity,
                BaseCap = i % 3 == 0 ? 123456789012345678 : random.Next(1, 1000000), BasePower = (float)(random.NextDouble() * 1e12),
                Itopod = (float)(1 + random.NextDouble() * 7), Macguffin = (float)(1 + random.NextDouble() * 7),
                Quirk = (float)(1 + random.NextDouble() * 7), Wish = (float)(1 + random.NextDouble() * 7),
                CapLimit = 9000000000000000000, PowerLimit = 1000000000000000000,
                Potion1 = i % 2 == 0 ? 2.2f : 1, Potion2 = i % 3 == 0 ? 2.2f : 1 };
            var bonuses = Enumerable.Range(0, 12).Select(_ => (float)(random.NextDouble() * 100)).ToArray();
            Check(p.Project(bonuses) == ResourceOracle(p, bonuses), "ordered game getter fixture " + energy + "/" + capacity + "/" + i);
        }
        foreach (var energy in new[] { true, false })
        {
            var bonuses = new float[12];
            var p = new ResourceProjection { Energy = energy, Capacity = true, BaseCap = 4, CapLimit = 100 };
            bonuses[energy ? 7 : 9] = 99;
            Near(p.Project(bonuses), 100, "current raw cap saturates");
            bonuses[energy ? 7 : 9] = 49;
            Near(p.Project(bonuses), 100, "less gear stays resource-cap saturated");
            bonuses[energy ? 7 : 9] = 9;
            Near(p.Project(bonuses), 40, "candidate crosses below resource cap");
            p.Capacity = false; p.BasePower = 4; p.PowerLimit = 100; p.Potion1 = 2.2f; p.Potion2 = 2.2f;
            bonuses[energy ? 0 : 3] = 49;
            Check(p.Project(bonuses) == (double)(float)(100d * 2.2f * 2.2f), "power clamp precedes BOTH potions");
            bonuses[energy ? 0 : 3] = 9;
            Check(p.Project(bonuses) == (double)(float)(40d * 2.2f * 2.2f), "power below clamp retains potion order");
            p.BasePower = 0;
            Check(p.Project(bonuses) == (double)(float)(1d * 2.2f * 2.2f), "minimum power precedes potions");
            p.Capacity = true; p.BaseCap = 1; p.Quirk = 2.5f; bonuses = new float[12];
            Near(p.Project(bonuses), 2, "capacity midpoint rounds to even");
            p.Quirk = 3.5f;
            Near(p.Project(bonuses), 4, "capacity odd midpoint rounds upward to even");
            p.BaseCap = 16777217; p.Quirk = 1; p.CapLimit = 9000000000000000000; bonuses[energy ? 7 : 9] = .5f;
            Near(p.Project(bonuses), 25165824, "base capacity converts to float before equipment, even below saturation");
        }

        var s = Snapshot(); var r = Request();
        s.Energy = s.Magic = 100; s.EnergyCapacity.BaseCap = s.MagicCapacity.BaseCap = 4;
        s.EnergyCapacity.CapLimit = s.MagicCapacity.CapLimit = 100;
        Stat(s.Current[0], PresetStat.EnergyCap, 9900); Stat(s.Current[0], PresetStat.MagicCap, 9900);
        Stat(s.Items[1], PresetStat.EnergyCap, 4900); Stat(s.Items[1], PresetStat.MagicCap, 4900);
        var candidate = LoadoutPresetPlanner.Evaluate(s, r, new[] { s.Items[1] }, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 6).ToArray(), "cap", "Cap");
        Near(candidate.EnergyPool, 100, "saturated E-cap reduction retains actual pool, not ratio-halved pool");
        Near(candidate.MagicPool, 100, "saturated M-cap reduction retains actual pool");
        s.EnergyPowerProjection.BasePower = s.MagicPowerProjection.BasePower = 4;
        s.EnergyPowerProjection.PowerLimit = s.MagicPowerProjection.PowerLimit = 100;
        s.EnergyPower = s.MagicPower = 100;
        Stat(s.Current[0], PresetStat.EnergyPower, 9900); Stat(s.Current[0], PresetStat.MagicPower, 9900);
        Stat(s.Items[1], PresetStat.EnergyPower, 4900); Stat(s.Items[1], PresetStat.MagicPower, 4900);
        candidate = LoadoutPresetPlanner.Evaluate(s, r, new[] { s.Items[1] }, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 6).ToArray(), "power", "Power");
        Near(candidate.EnergyNguPower, 100, "saturated E-power reduction does not invert captured clamp");
        Near(candidate.MagicNguPower, 100, "saturated M-power reduction does not invert captured clamp");
        s.Energy = 31; s.Magic = 47;
        candidate = Evaluate(s, r);
        Near(candidate.EnergyPool, 31, "larger capacity cannot refill actual energy");
        Near(candidate.MagicPool, 47, "larger capacity cannot refill actual magic");
        s.ReservedEnergy = 90;
        Near(Evaluate(s, r).EnergyPool, 10, "basic training remains reserved after forward cap projection");
        s = Snapshot();
        const long actualPool = 8999999999999999999;
        s.Energy = s.Magic = actualPool;
        s.EnergyCapacity.BaseCap = s.MagicCapacity.BaseCap = 9000000000000000000;
        s.EnergyCapacity.CapLimit = s.MagicCapacity.CapLimit = 9000000000000000000;
        candidate = Evaluate(s, r);
        Check(candidate.EnergyPool == actualPool && candidate.MagicPool == actualPool, "Int64 actual pools never round upward to a fictitious refill");
        s.ReservedEnergy = 97;
        Check(Evaluate(s, r).EnergyPool == 8999999999999999903, "large-pool cap reservation preserves the final resource units");

        s = Snapshot();
        s.Slots = new[] { -1, -2, -3, -4, -5, -6, 10000 };
        s.Factors = new[] { 1d, 1, 1, 1, 1, .37, 1 };
        s.Current = s.Slots.Select((slot, i) => new PresetItem { Id = 100 + i, Group = LoadoutPresetPlanner.Group(slot), SourceSlot = slot }).ToArray();
        for (var i = 0; i < s.Current.Length; i++)
        {
            s.Current[i].ResourceSpecs[0] = i == 4 ? 16777216f : i + 1;
            s.Current[i].Stats[0] = i == 4 ? 16777216f : i + 1;
        }
        var raw = 16777216f + 1f + 2f + 3f + 4f + 6f * .37f; raw += 7f;
        Check(LoadoutPresetPlanner.ResourceBonuses(s, s.Current)[0] == raw / 100f, "raw specs sum in game slot order before tier conversion, with offhand factor");
        Check(LoadoutPresetPlanner.Totals(s, s.Current)[0] == raw, "adventure gear uses game float sum and offhand order");
        s.NguFactors = new[] { 1.1f, 1.2f, 1.15f, 3.7f, 1.4f, 8.2f };
        var b = LoadoutPresetPlanner.ResourceBonuses(s, s.Current);
        b[12] = 8.43f; b[13] = 137.234f;
        var totalNgu = 1f + b[12] + b[13];
        foreach (var f in s.NguFactors) totalNgu *= f;
        Check(LoadoutPresetPlanner.NguBonus(s, b) == totalNgu, "shared NGU speed getter retains float factor order");
        s = Snapshot();
        for (var i = 0; i < 14; i++) s.Current[0].ResourceSpecs[i] = 1000000f;
        var expected = new[] { 10000f, 1000, 10, 10000, 1000, 10, 10, 100, 1, 100, 1, 1, 100, 1 };
        Check(LoadoutPresetPlanner.ResourceBonuses(s, s.Current).SequenceEqual(expected), "all resource and NGU spec tiers use verified game divisors");
    }

    private static void AdventureNumericChecks()
    {
        AdventureRegenNumericChecks();
        var factors = Enumerable.Repeat(1d, 14).ToArray();
        var expected = (double)(100f + (100f + (float)Math.Pow(9900f, .5f)));
        Check(LoadoutPresetPlanner.AdventureStat(100, 10000, 0, factors, 1) == expected, "candidate cube softcap decreases with removed gear");
        Near(LoadoutPresetPlanner.AdventureStat(100, 10000, 20000, factors, 1), 30100, "cube below candidate softcap is fully effective");
        Near(LoadoutPresetPlanner.AdventureStat(100, 1000, 900, factors, 1), 2000, "cube exactly on candidate softcap");
        Near(LoadoutPresetPlanner.AdventureStat(100, 0, 900, factors, 1), 1000, "base and gear retained without cube");
        factors[0] = 3; factors[4] = 99; factors[7] = 1.5f;
        Near(LoadoutPresetPlanner.AdventureStat(100, 0, 900, factors, 2), 9000, "planned digger replaces captured multiplier in game order");
        factors[13] = float.MaxValue;
        Check(LoadoutPresetPlanner.AdventureStat(100, 0, 900, factors, 2) == (double)1e36f, "adventure overflow clamps after ordered factors");

        var s = Snapshot(); var r = Request(); s.BasePower = 100; s.CubePower = 10000;
        s.Current[0].Stats[0] = 900; s.Power = 1e36; r.MinimumPower = 500;
        var weaker = LoadoutPresetPlanner.Evaluate(s, r, new[] { s.Items[1] }, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 6).ToArray(), "weak", "Weak");
        Check(!weaker.Feasible && !weaker.Score.HasValue, "gear-removal preview cannot retain old effective cube or invert clamped attack");
        s.BaseToughness = 100; s.CubeToughness = 10000; s.Current[0].Stats[1] = 900; r.MinimumPower = 0; r.MinimumToughness = 500;
        weaker = LoadoutPresetPlanner.Evaluate(s, r, new[] { s.Items[1] }, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 6).ToArray(), "weak", "Weak");
        Check(!weaker.Feasible, "toughness cube softcap is recomputed independently");

        s = Snapshot(); r = Request(); r.Weights = new[] { 0d, 0, 0, 0, 1, 0 };
        s.Available[3] = s.Available[4] = s.Available[5] = true;
        s.PpPerKill = Enumerable.Repeat(200d, 100).ToArray(); s.ItopodBoostPerKill = Enumerable.Repeat(1d, 100).ToArray();
        s.Zones = new[] { new PresetZone { Id = 0, IdlePower = 900, IdleToughness = 900, OneHitPower = 2000,
            NormalRatio = 1, BoostValues = new[] { 100d }, Chances = new[] { .5 }, Caps = new[] { 1d } } };
        Check(Evaluate(s, r).Feasible, "ordinary multi-hit farm passes ordinary recommendations");
        s.BeastMode = true;
        var beast = Evaluate(s, r);
        Check(!beast.Feasible && !beast.Score.HasValue && beast.Zone == -1, "Beast Mode refuses both regular and ITOPOD farms without claiming survival");
        Check(beast.Warnings.Any(x => x.Contains("Beast Mode")), "Beast Mode rejection is explained");
        var plan = LoadoutPresetPlanner.Plan(s, r);
        Check(plan.Candidates.All(x => !x.Feasible), "mixed farming search cannot bypass Beast Mode restriction");
        r = Request();
        Check(Evaluate(s, r).Feasible && s.BeastMode, "nonfarming preset remains usable and never toggles Beast Mode");
    }

    private static void ProjectedBudgetDifferentialChecks()
    {
        var fixtures = new[] {
            new { Cap = 1L, Pool = 0L, Reserved = 0L, Quirk = 1f },
            new { Cap = 1L, Pool = 1L, Reserved = 2L, Quirk = 1f },
            new { Cap = 1L, Pool = 100L, Reserved = 0L, Quirk = 2.5f },
            new { Cap = 1L, Pool = 100L, Reserved = 0L, Quirk = 3.5f },
            new { Cap = 16777217L, Pool = 16777217L, Reserved = 3L, Quirk = 1.5f },
            new { Cap = 9007199254740993L, Pool = 9007199254740993L, Reserved = 97L, Quirk = 1f },
            new { Cap = 9000000000000000000L, Pool = 8999999999999999999L, Reserved = 97L, Quirk = 1f },
            new { Cap = long.MaxValue, Pool = long.MaxValue, Reserved = 1L, Quirk = 1f },
            new { Cap = 100L, Pool = 1037L, Reserved = 7L, Quirk = 1f },
            new { Cap = 100L, Pool = 31L, Reserved = 10L, Quirk = .99999994f },
            new { Cap = 100L, Pool = 1037L, Reserved = 7L, Quirk = 1.0000001f } };
        for (var fixture = 0; fixture < fixtures.Length; fixture++)
        for (var equipment = 0; equipment < 5; equipment++)
        for (var path = 0; path < 3; path++)
        {
            var f = fixtures[fixture];
            var s = ActivitySnapshot();
            s.Available = new bool[14];
            s.Available[0] = s.Available[1] = s.Available[8] = s.Available[12] = s.Available[13] = true;
            s.Energy = s.Magic = f.Pool; s.ReservedEnergy = f.Reserved;
            s.ObservedCurrentPools = equipment != 0;
            s.EnergyCapacity.BaseCap = s.MagicCapacity.BaseCap = f.Cap;
            s.EnergyCapacity.CapLimit = s.MagicCapacity.CapLimit = f.Cap;
            s.EnergyCapacity.Quirk = s.MagicCapacity.Quirk = f.Quirk;
            foreach (var p in new[] { s.EnergyPowerProjection, s.MagicPowerProjection })
            { p.BasePower = 13.37f; p.Itopod = 1.11f; p.Macguffin = 1.23f; p.PowerLimit = 12; p.Potion1 = 1.7f; p.Potion2 = 2.2f; }
            foreach (var row in s.EnergyNgu.Concat(s.MagicNgu)) row.Coefficient = 1e-30;
            if (equipment >= 3) s.Current = new[] { new PresetItem { Group = -5, SourceSlot = -5 } };
            var items = equipment <= 1 ? s.Current.ToArray() : new[] { new PresetItem {
                Id = s.Current[0].Id, Group = -5, SourceSlot = equipment == 4 ? 42 : s.Current[0].SourceSlot } };
            var r = path == 0 ? Request() : ActivityRequest(0, 1, 8);
            if (path == 2)
            {
                r.ResourcePrioritiesReviewed = true;
                r.EnergyPriorities = new[] { "WAN", "NGU" }; r.MagicPriorities = new[] { "WAN", "NGU" };
            }

            // Pre-consolidation budget expressions, including the authoritative unchanged-pool exception.
            var bonuses = LoadoutPresetPlanner.ResourceBonuses(s, items);
            var eCap = s.EnergyCapacity.Project(bonuses); var mCap = s.MagicCapacity.Project(bonuses);
            var eBudget = eCap >= long.MaxValue ? long.MaxValue : !(eCap > 0) ? 0 : (long)Math.Floor(eCap);
            var mBudget = mCap >= long.MaxValue ? long.MaxValue : !(mCap > 0) ? 0 : (long)Math.Floor(mCap);
            var unchanged = equipment == 1 || equipment == 3;
            var energy = unchanged ? f.Pool : Math.Min(f.Pool, Math.Max(0, eBudget - f.Reserved));
            var magic = unchanged ? f.Pool : Math.Min(f.Pool, mBudget);
            var c = LoadoutPresetPlanner.Evaluate(s, r, items, LoadoutPresetPlanner.NormalizeWeights(r.Weights),
                Enumerable.Repeat(1d, 14).ToArray(), "budget", "Budget");
            var name = fixture + "/" + equipment + "/" + path;
            Check(c.EnergyPool == energy && c.MagicPool == magic, "projected budgets preserve exact legacy pools " + name);
            Check(c.Metrics[12] == eCap && c.Metrics[13] == mCap, "total capacity objectives remain independent of pools/reservation " + name);
            Check(c.EnergyNguPower == ResourceOracle(s.EnergyPowerProjection, bonuses)
                && c.MagicNguPower == ResourceOracle(s.MagicPowerProjection, bonuses), "all budget paths preserve native power/potions " + name);
            Check(c.Feasible == (eBudget >= f.Reserved), "retained basic feasibility uses integer cap, even with observed over-cap pools " + name);
            Check(checked(c.Energy + (c.Activities?.EnergyTotal ?? 0)) == energy
                && checked(c.MagicNgu + c.Blood + (c.Activities?.MagicTotal ?? 0)) == magic,
                "core/activity/priority funding conserves every available integer " + name);
            Check(s.Energy == f.Pool && s.Magic == f.Pool && s.ReservedEnergy == f.Reserved,
                "projected budgets leave snapshot pools and reservation unchanged " + name);
        }
    }

    private static void AdventureRegenNumericChecks()
    {
        var factors = Enumerable.Repeat(1d, 14).ToArray();
        Check(LoadoutPresetPlanner.AdventureRegen(100, 7, 100, 0, factors) == 10d,
            "regen keeps independent base regen instead of scaling base toughness by .03");
        Check(LoadoutPresetPlanner.AdventureRegen(100, 37, 100, 0, factors) == 40d,
            "changing base regen does not move toughness cube softcap");
        Check(LoadoutPresetPlanner.AdventureRegen(100, 1, 1000, 0, factors) == (double)(float)(1d + 130f * (double).03f),
            "regen cube above softcap uses base toughness plus gear toughness");
        Check(LoadoutPresetPlanner.AdventureRegen(100, 1, 1000, 900, factors) == (double)(float)(1d + 900f * (double).03f + 1000f * (double).03f),
            "adding gear crosses cube back to fully effective at exact softcap");
        Check(LoadoutPresetPlanner.AdventureRegen(100, 1, 1000, 901, factors) == (double)(float)(1d + 901f * (double).03f + 1000f * (double).03f),
            "cube stays fully effective below candidate toughness softcap");

        var s = Snapshot();
        s.Slots = new[] { -1, -2, -3, -4, -5, -6, 10000 };
        s.Factors = new[] { 1d, 1, 1, 1, 1, .37, 1 };
        s.Current = s.Slots.Select((slot, i) => new PresetItem { Id = 100 + i, Group = LoadoutPresetPlanner.Group(slot), SourceSlot = slot }).ToArray();
        for (var i = 0; i < s.Current.Length; i++) s.Current[i].Stats[1] = i == 4 ? 16777216f : i + 1;
        var gear = 16777216f;
        gear += 1f; gear += 2f; gear += 3f; gear += 4f; gear += 6f * .37f; gear += 7f;
        var totals = LoadoutPresetPlanner.Totals(s, s.Current);
        Check(totals[1] == gear, "regen input aggregates toughness with native slot rounding and offhand factor");
        Check(LoadoutPresetPlanner.AdventureRegen(100, 13, 0, totals[1], factors) == (double)(float)(13d + gear * (double).03f),
            "regen scales aggregated gear toughness once, not individually rounded slot regen");

        foreach (var threshold in new[] { .1f, 14000f, 150000f, 25000000f, 1e36f })
        foreach (var offset in new[] { -1, 0, 1 })
        {
            var adjacent = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(threshold), 0) + offset), 0);
            var expected = (double)Math.Min(1e36f, Math.Max(.1f, adjacent));
            Check(LoadoutPresetPlanner.AdventureRegen(0, adjacent, 0, 0, factors) == expected,
                "regen preserves adjacent binary32 threshold/clamp " + threshold + "/" + offset);
        }
        Check(LoadoutPresetPlanner.AdventureRegen(0, 0, 0, 0, factors) == (double).1f, "regen minimum is native .1f, not adventure stat minimum one");
        Check(LoadoutPresetPlanner.AdventureRegen(0, double.NaN, 0, 0, factors) == 0, "native NaN regen returns zero");
        Check(LoadoutPresetPlanner.AdventureRegen(0, double.PositiveInfinity, 0, 0, factors) == (double)1e36f, "positive infinite regen clamps");
        Check(LoadoutPresetPlanner.AdventureRegen(0, double.NegativeInfinity, 0, 0, factors) == (double).1f, "negative infinite regen hits minimum before infinity handling");
        factors[0] = 1.23456789; factors[3] = 1.87654321; factors[4] = 1.3456789;
        factors[5] = 1.11f; factors[9] = 1.31f; factors[13] = 1.2f;
        // Native's first local is stored only after its five-factor prefix.
        double ordered = (float)((17.321f + 12345.67f * (double).03f + 99.875f * (double).03f)
            * factors[0] * factors[1] * factors[2] * factors[3] * factors[4]);
        for (var i = 5; i < factors.Length; i++) ordered = (float)(ordered * factors[i]);
        Check(LoadoutPresetPlanner.AdventureRegen(100, 17.321f, 99.875f, 12345.67f, factors) == ordered,
            "regen retains captured F-stack factors and native local-store precision");
        var halfBits = BitConverter.ToInt32(BitConverter.GetBytes(.5f), 0);
        Check(LoadoutPresetPlanner.AdventureRegenParity(.5, BitConverter.ToSingle(BitConverter.GetBytes(halfBits + 7), 0)), "regen parity allows seven ULP below one");
        Check(!LoadoutPresetPlanner.AdventureRegenParity(.5, BitConverter.ToSingle(BitConverter.GetBytes(halfBits + 8), 0)), "regen parity rejects eight ULP below one");
        foreach (var clamp in new[] { .1f, 1e36f })
        {
            var inside = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(clamp), 0) + (clamp == .1f ? 1 : -1)), 0);
            Check(LoadoutPresetPlanner.AdventureRegenParity(clamp, clamp), "exact regen clamp accepted " + clamp);
            Check(!LoadoutPresetPlanner.AdventureRegenParity(clamp, inside), "regen clamp transitions never hidden by ULP allowance " + clamp);
        }
        foreach (var invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
            Check(!LoadoutPresetPlanner.AdventureRegenParity(invalid, invalid), "invalid regen parity fails closed " + invalid);
    }

    private static void SharingNumericChecks()
    {
        var shares = LoadoutPresetPlanner.Share(new[] { 100d, 100 }, 1, new[] { 1d, 1 });
        Check(shares.SequenceEqual(new long[] { 1, 0 }), "one resource is apportioned with stable tie break");
        shares = LoadoutPresetPlanner.Share(new[] { 100d, 100, 100 }, 2, new[] { 1d, 2, 3 });
        Check(shares.SequenceEqual(new long[] { 0, 1, 1 }), "largest fractional remainders receive final resources");
        shares = LoadoutPresetPlanner.Share(new[] { 1d, 100, 100 }, 8, new[] { 100d, 1, 1 });
        Check(shares.SequenceEqual(new long[] { 1, 4, 3 }), "capped target redistributes before remainder apportionment");
        shares = LoadoutPresetPlanner.Share(new[] { double.MaxValue, double.MaxValue }, long.MaxValue, new[] { (double)int.MaxValue, int.MaxValue });
        Check(shares[0] == 4611686018427387904L && shares[1] == 4611686018427387903L, "Int64 budget conserves the last unit without double conversion");
        for (var percent = 0; percent <= 100; percent++)
        {
            const long pool = 8999999999999999999;
            var first = LoadoutPresetPlanner.ResourcePercent(pool, percent);
            var second = LoadoutPresetPlanner.ResourcePercent(pool, 100 - percent);
            Check(first >= 0 && second >= 0 && first + second <= pool && pool - first - second <= 1,
                "Int64 magic split neither double counts nor creates resource " + percent);
        }
        var random = new Random(983);
        for (var trial = 0; trial < 200; trial++)
        {
            var caps = Enumerable.Range(0, 9).Select(_ => (double)random.Next(0, 1000)).ToArray();
            var weights = Enumerable.Range(0, 9).Select(_ => (double)random.Next(0, 10)).ToArray();
            var budget = random.Next(0, 10000);
            shares = LoadoutPresetPlanner.Share(caps, budget, weights);
            var totalCapacity = caps.Where((_, i) => weights[i] > 0).Sum();
            Check(shares.Sum() == Math.Min(budget, totalCapacity), "apportionment conserves eligible budget " + trial);
            Check(shares.Select((amount, i) => amount >= 0 && amount <= caps[i] && (weights[i] > 0 || amount == 0)).All(x => x), "apportionment honors caps and exclusions " + trial);
        }
    }

    private static void DeadlineAndReplayNumericChecks()
    {
        var s = Snapshot(); var r = Request(); var plan = LoadoutPresetPlanner.Plan(s, r);
        var fresh = s.ProgressionCopy();
        var item = plan.Candidates[1].Items[0];
        var altered = new PresetItem { Id = item.Id, Level = item.Level, SourceSlot = item.SourceSlot, Group = item.Group,
            Name = item.Name, Stats = item.Stats.ToArray(), ResourceSpecs = item.ResourceSpecs.ToArray() };
        altered.ResourceSpecs[0]++;
        fresh.Items = fresh.Items.Select(x => x.SourceSlot == item.SourceSlot ? altered : x).ToArray();
        Rejected(() => LoadoutPresetPlanner.Replay(fresh, r, plan), "gear", "replay rejects changed raw specs even when union-stat percentages match");

        // A clock already consumed by capture must not grant search/replay a fresh operation budget.
        var clock = Stopwatch.StartNew();
        System.Threading.Thread.Sleep(2700);
        var elapsed = clock.ElapsedMilliseconds;
        var fallback = LoadoutPresetPlanner.Plan(s, r, clock);
        Check(fallback.Budget && fallback.Sequence == null && clock.ElapsedMilliseconds - elapsed < 500, "elapsed capture clock skips expensive search and sequence");
        Near(fallback.Candidates[1].Score.Value, fallback.Candidates[0].Score.Value, "budget fallback still uses the frozen normalization");
        Check(fallback.Specialists.All(x => x.Warnings.Any(w => w.Contains("budget exhausted"))), "budget fallback does not claim unsearched full-focus policies");
        System.Threading.Thread.Sleep(310);
        Rejected(() => LoadoutPresetPlanner.Replay(s, r, plan, clock), "time budget", "replay enforces the same whole-operation deadline");
    }

    private static void ManualGearNumericChecks()
    {
        var before = assertions;
        var s = Snapshot(); var r = Request();
        Check(r.GearWeights.Length == 5 && r.GearWeights.All(x => x == 0) && r.GearFightMode == 0,
            "manual gear API defaults to five zero bonuses and sqrt fight mode");
        var plain = LoadoutPresetPlanner.Plan(s, r);
        r.GearWeights = new double[5];
        var zero = LoadoutPresetPlanner.Plan(s, r);
        Check(zero.Normalization.SequenceEqual(plain.Normalization) && zero.ReferenceProduction.SequenceEqual(plain.ReferenceProduction),
            "zero gear bonuses preserve exact specialist normalization");
        foreach (var pair in plain.Candidates.Concat(plain.Specialists).Zip(zero.Candidates.Concat(zero.Specialists), (a, b) => new { a, b }))
        {
            Check(pair.a.Score == pair.b.Score && pair.a.Metrics.SequenceEqual(pair.b.Metrics) && pair.a.Items.SequenceEqual(pair.b.Items),
                "zero gear bonuses preserve exact score, gear and production: " + pair.a.Key);
            Check(pair.b.Score == LoadoutPresetPlanner.Score(pair.b.Metrics, LoadoutPresetPlanner.NormalizeWeights(r.Weights), zero.Normalization, pair.b.Feasible),
                "zero gear bonus score is exactly the unchanged production scorer: " + pair.b.Key);
        }
        Check(zero.Sequence.Score == plain.Sequence.Score && zero.Sequence.Metrics.SequenceEqual(plain.Sequence.Metrics),
            "zero gear bonuses retain advisory sequence scoring exactly");

        // Independent ratios: power 2, toughness 4, move 2, drop 2, gold 3.
        s = Snapshot(); r = Request(); r.Weights = new[] { 2d, 3, 0, 0, 0, 0 };
        Stat(s.Current[0], PresetStat.Power, 1000); Stat(s.Current[0], PresetStat.Toughness, 1000);
        Stat(s.Current[0], PresetStat.MoveCooldown, 100); Stat(s.Current[0], PresetStat.Drop, 100); Stat(s.Current[0], PresetStat.Gold, 100);
        s.Drop = 2;
        var item = s.Items[1];
        Stat(item, PresetStat.Power, 3000); Stat(item, PresetStat.Toughness, 7000);
        Stat(item, PresetStat.MoveCooldown, 300); Stat(item, PresetStat.Drop, 300); Stat(item, PresetStat.Gold, 500);
        var weights = LoadoutPresetPlanner.NormalizeWeights(r.Weights); var norms = Enumerable.Repeat(1d, 12).ToArray();
        var baseCandidate = LoadoutPresetPlanner.Evaluate(s, r, new[] { item }, weights, norms, "base", "Base");
        var originalWeights = r.Weights.ToArray();
        r.GearWeights = new[] { 1d, 2, 3, 4, 5 };
        var pushes = new[] { Math.Sqrt(8) * 2, 16d, 6d };
        for (var mode = 0; mode < 3; mode++)
        {
            r.GearFightMode = mode;
            var bonus = LoadoutPresetPlanner.Evaluate(s, r, new[] { item }, weights, norms, "bonus", "Bonus");
            Near(bonus.Score.Value, (5 * baseCandidate.Score.Value + pushes[mode] + 2 * 2 + 3 * 4 + 4 * 2 + 5 * 3) / 20,
                "base plus additive relative gear utility uses raw weight mass and fight mode " + mode);
            Check(bonus.Metrics.SequenceEqual(baseCandidate.Metrics) && bonus.RawMetrics.SequenceEqual(baseCandidate.RawMetrics)
                && bonus.EnergyShares.SequenceEqual(baseCandidate.EnergyShares) && bonus.MagicShares.SequenceEqual(baseCandidate.MagicShares)
                && bonus.Blood == baseCandidate.Blood && bonus.GrossGold == baseCandidate.GrossGold,
                "gear utility cannot rewrite production, resource allocations or gold income " + mode);
            Check(!bonus.Metrics[10].HasValue && !bonus.Metrics[11].HasValue,
                "bonus adventure stats work without extending or fabricating base objective metrics " + mode);
            var again = LoadoutPresetPlanner.Evaluate(s, r, s.Current, weights, norms, "current", "Current");
            var after = LoadoutPresetPlanner.Evaluate(s, r, new[] { item }, weights, norms, "after", "After");
            Check(after.Score == bonus.Score, "bonus denominators never move with evaluated gear " + mode);
            Near(again.Score.Value, (5 * Evaluate(s, new PresetRequest { Weights = originalWeights, Swap = 0, Horizon = 1000 }).Score.Value + 15) / 20,
                "equipped gear remains unit relative utility under every fight mode " + mode);
        }
        Check(r.Weights.SequenceEqual(originalWeights) && r.GearWeights.SequenceEqual(new[] { 1d, 2, 3, 4, 5 }),
            "candidate evaluation leaves both caller-owned weight arrays unchanged");
        var unscaled = LoadoutPresetPlanner.Evaluate(s, r, new[] { item }, weights, norms, "scale", "Scale");
        r.Weights = r.Weights.Select(x => x * 10).ToArray(); r.GearWeights = r.GearWeights.Select(x => x * 10).ToArray();
        var scaled = LoadoutPresetPlanner.Evaluate(s, r, new[] { item }, weights, norms, "scale", "Scale");
        Near(scaled.Score.Value, unscaled.Score.Value, "scaling base and gear weights together preserves score");
        r.GearWeights = r.GearWeights.Select(x => x / 10).ToArray();
        var baseHeavy = LoadoutPresetPlanner.Evaluate(s, r, new[] { item }, weights, norms, "base-heavy", "Base heavy");
        Near(baseHeavy.Score.Value, (50 * baseCandidate.Score.Value + 6 + 4 + 12 + 8 + 15) / 65,
            "scaling only raw base weights increases retained base importance");
        Check(originalWeights.SequenceEqual(new[] { 2d, 3, 0, 0, 0, 0 }) && item.Stats[(int)PresetStat.Gold] == 500,
            "manual gear scoring never mutates base weights or item definitions");

        s = Snapshot(); r = Request(); r.Weights = new[] { 10d, 0, 0, 0, 0, 0 };
        var producer = s.Items[1];
        var fighter = new PresetItem { Id = 8000, SourceSlot = 8000, Group = -5 };
        Stat(fighter, PresetStat.Power, 9000);
        s.Items = s.Current.Concat(new[] { producer, fighter }).ToArray();
        var productionOnly = LoadoutPresetPlanner.Plan(s, r);
        r.GearWeights[1] = .1;
        var weakBonus = LoadoutPresetPlanner.Plan(s, r);
        r.GearWeights[1] = 10;
        var strongBonus = LoadoutPresetPlanner.Plan(s, r);
        Check(productionOnly.Candidates[1].Items.Single() == producer && weakBonus.Candidates[1].Items.Single() == producer
            && strongBonus.Candidates[1].Items.Single() == fighter,
            "additive gear selection retains production at low bonus weight and trades it at high bonus weight");
        Check(strongBonus.Candidates[1].Metrics[0] < productionOnly.Candidates[1].Metrics[0]
            && strongBonus.Normalization.SequenceEqual(productionOnly.Normalization)
            && strongBonus.ReferenceProduction.SequenceEqual(productionOnly.ReferenceProduction),
            "gear tradeoff changes real retained production, not its metric formula or frozen reference");
        var selected = strongBonus.Candidates[1];
        var retained = LoadoutPresetPlanner.Score(selected.Metrics, LoadoutPresetPlanner.NormalizeWeights(r.Weights), strongBonus.Normalization, true).Value;
        Near(selected.Score.Value, (10 * retained + 10 * 10) / 20, "selected mixed gear earns base production plus relative power, not a replacement objective");
        s.Available[0] = false;
        Check(!Evaluate(s, r).Score.HasValue, "positive gear utility cannot hide an unavailable positive base objective");
        s.Available[0] = true; r.MinimumPower = 20000;
        Check(!Evaluate(s, r).Feasible && !Evaluate(s, r).Score.HasValue, "gear utility never rescues an infeasible combat constraint");

        foreach (var goal in Enumerable.Range(0, 5))
        {
            s = Snapshot(); r = Request(); r.Weights = new[] { 1d, 0, 0, 0, 0, 0 };
            s.Energy = s.Magic = 0; s.Available = new bool[12]; s.Available[0] = true;
            var distractors = Enumerable.Range(0, 500).Select(i => {
                var other = new PresetItem { Id = 1000 + i, SourceSlot = 1000 + i, Group = -5 };
                Stat(other, PresetStat.EnergyPower, 1 + i); return other;
            }).ToArray();
            var winner = new PresetItem { Id = 9999, SourceSlot = 9999, Group = -5 };
            Stat(winner, new[] { PresetStat.MoveCooldown, PresetStat.Power, PresetStat.Toughness, PresetStat.Drop, PresetStat.Gold }[goal], 10000);
            s.Items = s.Current.Concat(distractors).Concat(new[] { winner }).ToArray();
            var baseline = LoadoutPresetPlanner.Plan(s, r);
            r.GearWeights[goal] = 100;
            var plan = LoadoutPresetPlanner.Plan(s, r); var best = plan.Candidates[1];
            Check(best.Items.Single() == winner && best.Score > plan.Candidates[0].Score,
                "bonus-aware shortlist selects gear despite zero base production and resource distractors " + goal);
            Check(best.Metrics[0] == 0 && best.Energy == 0 && best.MagicNgu == 0 && best.Blood == 0 && best.Activities == null && plan.Sequence == null,
                "immediate bonus introduces no excluded resource activities or time-sharing " + goal);
            Check(plan.Normalization.SequenceEqual(baseline.Normalization) && plan.ReferenceProduction.SequenceEqual(baseline.ReferenceProduction),
                "bonus search retains bonus-free frozen production references " + goal);
            foreach (var candidate in plan.Candidates.Concat(plan.Specialists))
            {
                var replay = LoadoutPresetPlanner.Evaluate(s, r, candidate.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights),
                    plan.Normalization, candidate.Key, candidate.Label, candidate);
                Check(candidate.Feasible ? candidate.Score == replay.Score : !candidate.Score.HasValue,
                    "search/final/replay scoring agrees; unavailable full-focus policies remain unscored " + goal + "/" + candidate.Key);
            }
        }

        s = Snapshot(); s.Items = s.Current.ToArray(); s.Available = new bool[12]; s.Available[6] = true; s.Gold = 1e12;
        s.TimeMachineEnergy = new PresetActivityBar { Unlocked = true, Level = 10, Coefficient = .001 };
        s.TimeMachineMagic = new PresetActivityBar { Unlocked = true, Level = 10, Coefficient = .001 };
        r = Request(); r.Weights = new double[12]; r.Weights[6] = 1; r.BloodPercent = 0;
        var production = Evaluate(s, r);
        r.GearWeights[3] = 50; r.GearWeights[4] = 25;
        var looter = new PresetItem { Id = 9000, SourceSlot = 9000, Group = -5 };
        Stat(looter, PresetStat.Drop, 900); Stat(looter, PresetStat.Gold, 400);
        var loot = LoadoutPresetPlanner.Evaluate(s, r, new[] { looter }, LoadoutPresetPlanner.NormalizeWeights(r.Weights), norms, "loot", "Loot");
        Check(loot.Metrics.SequenceEqual(production.Metrics) && loot.RawMetrics.SequenceEqual(production.RawMetrics)
            && loot.Activities.TimeMachineEnergy == production.Activities.TimeMachineEnergy
            && loot.Activities.TimeMachineMagic == production.Activities.TimeMachineMagic
            && loot.Activities.AT.All(x => x == 0) && loot.Activities.AugmentEnergy == 0 && loot.Activities.WandoosEnergy == 0
            && loot.Energy == 0 && loot.MagicNgu == 0 && loot.Blood == 0 && loot.GrossGold == production.GrossGold,
            "drop and gold bonuses stay independent of TM production and excluded activities");
        Near(loot.Score.Value, (production.Score.Value + 50 * 10 + 25 * 5) / 76,
            "extra-activity final score retains additive drop/gold utility after production rescore");
        r.GearWeights = new double[5];
        var zeroActivity = Evaluate(s, r);
        Check(zeroActivity.Score == production.Score && zeroActivity.Metrics.SequenceEqual(production.Metrics),
            "zero bonuses preserve exact extra-activity scoring");

        foreach (var invalid in new[] { new double[4], new[] { -1d, 0, 0, 0, 0 }, new[] { double.NaN, 0, 0, 0, 0 }, new[] { double.PositiveInfinity, 0, 0, 0, 0 } })
        {
            r.GearWeights = invalid;
            var rejected = false;
            try { Evaluate(s, r); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid manual gear weights fail closed");
        }
        Console.WriteLine("Manual gear numeric checks: " + (assertions - before) + " assertions");
    }

    private static void NumericChecks()
    {
        var before = assertions; var clock = Stopwatch.StartNew();
        ResourceAggregationChecks.Run(Check); ProjectedBudgetDifferentialChecks();
        ManualGearNumericChecks(); ResourceNumericChecks(); AdventureNumericChecks(); SharingNumericChecks(); NguNumericChecks(); DeadlineAndReplayNumericChecks();
        Console.WriteLine("Numeric/oracle checks: " + (assertions - before) + " assertions in " + clock.ElapsedMilliseconds + " ms");
    }
}
