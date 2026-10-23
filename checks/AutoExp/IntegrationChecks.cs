using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text;
using Game = AutoExpGame;
using static AutoExpProgram;

internal static class IntegrationChecks
{
    private static readonly long[] Balanced = { 25, 800000, 20, 5, 160000, 4 };
    private static readonly long[] Prices = { 3750, 3200, 1600, 2250, 1920, 960 };

    internal static void Register()
    {
        Tests.Add(("integration/toggle off leaves EXP and all BASE stats alone", () =>
        {
            var c = Reset();
            Game.Settings.CustomAutoExpEnabled = false;
            Tick(c, new long[6], 13680);
        }));
        foreach (var exp in new[] { 0L, -1L })
            Tests.Add(($"integration/no spend with EXP {exp}", () =>
            {
                var c = Reset();
                c.realExp = exp;
                var before = c.BaseStats();
                new Game().Tick();
                Sequence(before, c.BaseStats(), "no EXP must not mutate stats");
                Equal(exp, c.realExp, "unavailable EXP unchanged, not clamped");
                Equal(0, c.Calls.Count, "no purchase API with unavailable EXP");
                Equal(0, Game.Errors.Count, "no EXP is not an error");
            }));
        foreach (var missing in new[] { "character", "settings", "energy controller" })
            Tests.Add(("integration/missing " + missing + " safely skipped", () =>
            {
                var c = Reset();
                if (missing == "character") Game.Character = null;
                if (missing == "settings") c.settings = null;
                if (missing == "energy controller") c.energyPurchases = null;
                new Game().Tick();
                Equal(13680L, c.realExp, "missing state EXP unchanged");
                Equal(0, c.Calls.Count, "missing state purchase calls");
                Equal(0, Game.Errors.Count, "missing state is not an error");
            }));
        Tests.Add(("integration/boss 16 locks auto purchases", () =>
        {
            var c = Reset(); c.highestBoss = 16;
            Tick(c, new long[6], 13680);
        }));
        foreach (var stat in Enumerable.Range(0, 6))
        {
            var captured = stat;
            Tests.Add(($"integration/manual input {stat} pauses purchases only on its menu", () =>
            {
                var c = Reset();
                c.menuID = captured < 3 ? 13 : 32;
                Purchases controller = captured < 3 ? c.energyPurchases : c.magicPurchases;
                var input = captured % 3 == 0 ? controller.powerInput : captured % 3 == 1 ? controller.capInput : controller.barInput;
                input.isFocused = true;
                Tick(c, new long[6], 13680);
                input.isFocused = false;
                Tick(c, Balanced, 0);
            }));
        }
        foreach (var boss in new[] { 17, 36 })
            Tests.Add(($"integration/boss {boss} buys energy only", () =>
            {
                var c = Reset(8550); c.highestBoss = boss;
                Tick(c, new long[] { 25, 800000, 20, 0, 0, 0 }, 0);
            }));
        foreach (var missing in new[] { "magic", "magic controller" })
            Tests.Add(("integration/missing " + missing + " still buys energy", () =>
            {
                var c = Reset(8550);
                if (missing == "magic") c.magic = null;
                else c.magicPurchases = null;
                Tick(c, new long[] { 25, 800000, 20, 0, 0, 0 }, 0);
            }));
        Tests.Add(("integration/energy cap 99999 locked independently", () =>
        {
            var c = Reset(5350); c.highestBoss = 36; c.capEnergy = 99999;
            Tick(c, new long[] { 25, 0, 20, 0, 0, 0 }, 0);
        }));
        Tests.Add(("integration/energy cap 100000 unlocks exactly", () =>
        {
            var c = Reset(1); c.highestBoss = 36; c.capEnergy = 100000;
            Tick(c, new long[] { 0, 250, 0, 0, 0, 0 }, 0);
        }));
        Tests.Add(("integration/boss 37 routes all six APIs using BASE only and restores inputs", AllSix));
        foreach (var menu in new[] { 13, 32 })
            Tests.Add(($"integration/menu {menu} restores visible inputs and costs after success", () => RestoredDisplay(menu)));
        Tests.Add(("integration/all eight edited settings reach the real planner", EditedSettings));
        foreach (var magic in new[] { false, true })
        {
            foreach (var sample in new (float Power, long Requested, long Gain)[]
            {
                (16777215f, 1, 1),
                (16777215f, 2, 1),
                (16777215f, 3, 3),
                (16777215f, 4, 3),
                (16777216f, 1, 0),
                (16777216f, 2, 2),
                (16777216f, 3, 2),
                (16777218f, 1, 0),
                (16777218f, 2, 2),
                (16777218f, 3, 2),
                (1f, 16777216, 16777215)
            })
                Tests.Add(($"integration/{(magic ? "magic" : "energy")} float boundary power {sample.Power:R} request {sample.Requested} buys {sample.Gain}", () =>
                    FloatBoundary(magic, sample.Power, sample.Requested, sample.Gain)));
            Tests.Add(($"integration/{(magic ? "magic" : "energy")} unrepresentable power avoids spending", () => FloatNoop(magic)));
            Tests.Add(($"integration/{(magic ? "magic" : "energy")} float to decimal rounding must not disguise a power noop", () => FloatRoundingNoop(magic)));
            Tests.Add(($"integration/{(magic ? "magic" : "energy")} rounded BASE power must not exceed the hardcap", () => FloatHardcap(magic)));
            Tests.Add(($"integration/{(magic ? "magic" : "energy")} partially unrepresentable power keeps unused EXP", () =>
            {
                var c = PowerFixture(magic, magic ? 4950 : 1650, 100000192f);
                var increments = new long[6];
                increments[magic ? 3 : 0] = 8;
                Tick(c, increments, magic ? 1350 : 450);
            }));
            Tests.Add(($"integration/{(magic ? "magic" : "energy")} rounding upward waits for an affordable representable gain", () =>
            {
                var c = PowerFixture(magic, magic ? 2250 : 750, 100000192f);
                Tick(c, new long[6], magic ? 2250 : 750);
            }));
        }
        Tests.Add(("integration/game cost disagreement refuses all six purchases", CostDisagreement));
        Tests.Add(("integration/current EXP rechecked between private purchases", BudgetChanges));
        for (var stat = 0; stat < 6; stat++)
        foreach (var afterMutation in new[] { false, true })
        {
            var captured = stat;
            Tests.Add(($"integration/private stat {stat} throws {(afterMutation ? "after" : "before")} mutation and restores inputs", () =>
                ThrowingPurchase(captured, afterMutation)));
        }
        Tests.Add(("integration/300 randomized multi-tick EXP conservation and no R3", Randomized));
        foreach (var stat in Enumerable.Range(0, 6))
        {
            Tests.Add(($"finite/stat {stat} lane grant refusal changes no inputs and can retry", () => GrantRefusal(stat)));
            Tests.Add(($"finite/stat {stat} claim admitted during preflight revokes purchase grant", () => GrantRevoked(stat)));
            Tests.Add(($"finite/stat {stat} changed preflight price refuses native purchase", () => ChangedPrice(stat)));
            Tests.Add(($"finite/stat {stat} newer borrowed input is never overwritten", () => ForeignInput(stat)));
            Tests.Add(($"finite/stat {stat} committed disk identity never replays", () => CommittedReplay(stat)));
        }
        foreach (var magic in new[] { false, true })
        {
            Tests.Add(($"finite/{(magic ? "magic" : "energy")} fractional BASE power keeps its fraction", () => FloatBoundary(magic, 1.5f, 1, 1)));
            Tests.Add(($"finite/{(magic ? "magic" : "energy")} final presentation exception cannot replay purchase", () => PresentationFailure(magic)));
            Tests.Add(($"finite/{(magic ? "magic" : "energy")} unexpected native currency change blocks all dependent purchases", () => UnknownCurrency(magic)));
        }
        foreach (var lifecycle in new[] { "Quiescing", "Unloaded" })
            Tests.Add(($"finite/lifecycle {lifecycle} refuses all six purchases", () => LifecycleRefusal(lifecycle)));
        Tests.Add(("finite/native settings claim refuses EXP until released", ClaimRefusal));
        Tests.Add(("finite/result storage failure withholds successor until durable flush", () => ResultStorage(false)));
        Tests.Add(("finite/imported invocation after result storage failure never replays", () => ResultStorage(true)));
    }

    private static Character Reset(long budget = 13680)
    {
        var c = new Character();
        c.realExp = budget;
        c.MinimumExp = budget;
        Game.Character = c;
        Game.Settings = new ExpSettings();
        Game.Logs.Clear();
        Game.Errors.Clear();
        return c;
    }

    private static void Tick(Character c, long[] increments, long remaining, bool rejectedCall = false)
    {
        var before = c.BaseStats();
        var inputs = c.settings.Inputs();
        var unrelated = Unrelated(c);
        var exp = c.realExp;
        PrepareDisplays(c);
        new Game().Tick();
        Sequence(before.Select((v, i) => v + increments[i]), c.BaseStats(), "exact BASE post-state; finite evidence: "
            + string.Join("; ", NativeBoundary.Results.Select(r => NativeBoundary.Get(r, "Reason") + " / " + NativeBoundary.Get(r, "Error")))
            + string.Join("; ", Game.Errors.Select(e => e.Error.ToString())));
        Equal(remaining, c.realExp, "exact remaining EXP");
        Check(c.MinimumExp >= 0, "EXP must never transiently go negative");
        Sequence(inputs, c.settings.Inputs(), "all original custom inputs restored");
        Sequence(unrelated, Unrelated(c), "speed/R3/popup state untouched");
        Equal(0, c.res3Purchases.Calls, "R3 API must never be called");
        Equal(0, NativeBoundary.TotalStatCalls, "no total-stat API may replace BASE observations");
        Equal(0, Game.Errors.Count, "finite leaf exceptions are retained as result evidence, not swallowed outer errors");
        AssertDisplays(c, inputs);
        foreach (var call in c.Calls)
        {
            var temporary = (long[])inputs.Clone();
            temporary[call.Stat] = call.Amount;
            Sequence(temporary, call.Inputs, "only the routed custom field is temporarily changed");
        }
        if (!rejectedCall)
        {
            var expectedCalls = Enumerable.Range(0, 6).Where(i => increments[i] > 0).ToArray();
            Sequence(expectedCalls, c.Calls.Select(call => call.Stat), "private API routing");
            Sequence(expectedCalls.Select(i => increments[i]), c.Calls.Select(call => call.Amount), "private API input amounts");
            Equal(exp > remaining ? 1 : 0, Game.Logs.Count, "spend-only success log count");
            if (exp > remaining)
                Equal($"Auto use EXP spent {exp - remaining} EXP balancing BASE Energy/Magic stats.", Game.Logs[0], "spend log");
        }
    }

    private static void PrepareDisplays(Character c)
    {
        var menu = c.menuID;
        c.menuID = 13;
        c.energyPurchases?.refresh();
        c.menuID = 32;
        c.magicPurchases?.refresh();
        c.menuID = menu;
        foreach (var boundary in new Purchases[] { c.energyPurchases, c.magicPurchases })
        {
            if (boundary == null) continue;
            boundary.Displays.Clear();
            boundary.CostQueries.Clear();
            boundary.RefreshCalls = 0;
        }
    }

    private static void AssertDisplays(Character c, long[] original)
    {
        for (var lane = 0; lane < 2; lane++)
        {
            Purchases boundary = lane == 0 ? c.energyPurchases : c.magicPurchases;
            if (boundary == null) continue;
            var calls = c.Calls.Where(call => call.Stat / 3 == lane).ToArray();
            var refreshes = calls.Sum(call => boundary.ThrowBeforeStat == call.Stat ? 0 : 2);
            Equal(refreshes, boundary.RefreshCalls, "private and durable-commit presentation refreshes, lane " + lane);
            var visible = c.menuID == (lane == 0 ? 13 : 32);
            Equal(visible ? refreshes : 0, boundary.Displays.Count, "only active EXP menu renders, lane " + lane);
            DisplayMatches(boundary.Display, original, lane, boundary.CostOffset, "final restored display");
            if (!visible) continue;
            var index = 0;
            foreach (var call in calls)
            {
                if (boundary.ThrowBeforeStat == call.Stat) continue;
                DisplayMatches(boundary.Displays[index++], call.Inputs, lane, boundary.CostOffset, "private purchase temporary display");
                DisplayMatches(boundary.Displays[index++], original, lane, boundary.CostOffset, "finally display after restoring settings");
            }
        }
    }

    private static void DisplayMatches(PurchaseDisplay display, long[] inputs, int lane, long offset, string message)
    {
        var amounts = inputs.Skip(lane * 3).Take(3).ToArray();
        var costs = new[] { amounts[0] * 150 * (lane == 0 ? 1 : 3) + offset,
            amounts[1] / 250 * (lane == 0 ? 1 : 3) + offset, amounts[2] * 80 * (lane == 0 ? 1 : 3) + offset };
        Sequence(amounts.Select(amount => amount.ToString(CultureInfo.InvariantCulture)), display.Inputs, message + " input text");
        Sequence(costs, display.Costs, message + " individual costs");
        Equal(costs.Sum(), display.AllCost, message + " buy-all cost");
    }

    private static decimal[] Unrelated(Character c) => new decimal[]
    {
        (decimal)c.energySpeed, c.magic == null ? 0m : (decimal)c.magic.magicBarSpeed,
        (decimal)c.res3.res3Power, c.res3.capRes3, c.res3.res3PerBar, c.settings.expPopups ? 1 : 0
    };

    private static void AllSix()
    {
        var c = Reset();
        Tick(c, Balanced, 0);
        Sequence(Prices, c.Calls.Select(call => call.Cost), "exact game API costs");
        foreach (var boundary in new Purchases[] { c.energyPurchases, c.magicPurchases })
        {
            var queries = boundary.CostQueries.Where(q => !q.Rendering).GroupBy(q => q.Api).ToArray();
            Sequence(new[] { "power", "cap", "bar" }, queries.Select(q => q.Key), "all native cost routes checked");
            var expected = boundary.Magic ? new long[] { 5, 160000, 4 } : new long[] { 25, 800000, 20 };
            for (var kind = 0; kind < 3; kind++)
            {
                Check(queries[kind].Count() >= 2, "native price is rechecked before and inside purchase");
                Check(queries[kind].All(q => q.Amount == expected[kind]), "every preflight/native cost query uses the exact amount");
            }
        }
        Check(NativeBoundary.Results.All(r => (bool)NativeBoundary.Get(r, "Committed") && (bool)NativeBoundary.Get(r, "Durable")), "all six native results durably committed");
    }

    private static void RestoredDisplay(int menu)
    {
        var c = Reset();
        c.menuID = menu;
        Tick(c, Balanced, 0);
        if (menu == 13)
        {
            Sequence(new[] { "-17", "3000000123", "29" }, c.energyPurchases.Display.Inputs, "restored visible energy inputs");
            Sequence(new long[] { -2550, 12000000, 2320 }, c.energyPurchases.Display.Costs, "restored visible energy costs");
            Equal(11999770L, c.energyPurchases.Display.AllCost, "restored visible energy buy-all cost");
        }
        else
        {
            Sequence(new[] { "43", "4000000321", "-59" }, c.magicPurchases.Display.Inputs, "restored visible magic inputs");
            Sequence(new long[] { 19350, 48000003, -14160 }, c.magicPurchases.Display.Costs, "restored visible magic costs");
            Equal(48005193L, c.magicPurchases.Display.AllCost, "restored visible magic buy-all cost");
        }
    }

    private static void EditedSettings()
    {
        var c = Reset(13218);
        Game.Settings.SetRatios(new[] { 2, 3, 2, 1000, 1, 7, 2000, 3 });
        c.energyPower = 800; c.capEnergy = 400000; c.energyBars = 400;
        c.magic.magicPower = 2100; c.magic.capMagic = 600000; c.magic.magicPerBar = 900;
        Tick(c, new long[] { 8, 4000, 4, 21, 6000, 9 }, 0);
    }

    private static Character PowerFixture(bool magic, long budget, float power)
    {
        var c = Reset(budget);
        Game.Settings.SetRatios(Enumerable.Repeat(1, 8).ToArray());
        c.energyPower = 1000000000; c.capEnergy = 1000000000; c.energyBars = 1000000000;
        c.magic.magicPower = 1000000000; c.magic.capMagic = 1000000000; c.magic.magicPerBar = 1000000000;
        if (magic) c.magic.magicPower = power;
        else c.energyPower = power;
        return c;
    }

    private static void FloatNoop(bool magic)
    {
        var budget = magic ? 450L : 150L;
        var c = PowerFixture(magic, budget, 100000000f);
        Equal(100000000f, 100000000f + 1f, "fixture: one power really is a float noop");
        Tick(c, new long[6], budget);
        Equal(0, c.energyPurchases.CostQueries.Count + c.magicPurchases.CostQueries.Count, "noop skipped before any cost API");
    }

    private static void FloatBoundary(bool magic, float power, long requested, long gain)
    {
        var price = magic ? 450L : 150L;
        var c = PowerFixture(magic, requested * price, power);
        c.menuID = magic ? 32 : 13;
        var increments = new long[6];
        increments[magic ? 3 : 0] = gain;
        Tick(c, increments, (requested - gain) * price);
        Sequence(gain > 0 ? new[] { gain * price } : new long[0], c.Calls.Select(call => call.Cost),
            "paid cost matches the actual representable power gain");
    }

    private static void FloatRoundingNoop(bool magic)
    {
        // The decimal cast rounds this float to seven significant digits. The guard must
        // compare the game's actual float addition, not addition to that rounded decimal.
        var budget = magic ? 1800L : 600L;
        var c = PowerFixture(magic, budget, 100000192f);
        var power = magic ? c.magic.magicPower : c.energyPower;
        Equal(100000192f, power + 4, "fixture: four power really is a float noop");
        Equal(100000200m, (decimal)power, "fixture: decimal cast loses the original float value");
        Tick(c, new long[6], budget);
    }

    private static void FloatHardcap(bool magic)
    {
        var budget = magic ? 45000L : 15000L;
        var c = PowerFixture(magic, budget, 999999936f);
        c.CapLimit = c.PowerBarLimit = 1000000000;
        // Exactly 64 real units of headroom, not the 100 suggested by (decimal)power.
        var increments = new long[6];
        increments[magic ? 3 : 0] = 64;
        Tick(c, increments, magic ? 16200 : 5400);
    }

    private static void CostDisagreement()
    {
        var c = Reset();
        c.energyPurchases.CostOffset = 1;
        c.magicPurchases.CostOffset = 1;
        Tick(c, new long[6], 13680);
        Equal(3, c.energyPurchases.CostQueries.Count, "every energy cost rejected before mutation");
        Equal(3, c.magicPurchases.CostQueries.Count, "every magic cost rejected before mutation");
    }

    private static void BudgetChanges()
    {
        var c = Reset();
        // Consume between finite leaves, after the first result is durable, not inside its native accounting proof.
        c.energyPurchases.Presented = () => { if (c.energyPurchases.RefreshCalls == 2) c.realExp = 0; };
        Tick(c, new long[] { 25, 0, 0, 0, 0, 0 }, 0);
        Equal(1, c.Calls.Count, "stale planned budget must not reach another private purchase");
    }

    private static void ThrowingPurchase(int stat, bool afterMutation)
    {
        var c = Reset();
        c.menuID = stat < 3 ? 13 : 32;
        Purchases boundary = stat < 3 ? c.energyPurchases : c.magicPurchases;
        if (afterMutation) boundary.ThrowAfterStat = stat;
        else boundary.ThrowBeforeStat = stat;
        var count = afterMutation ? 6 : stat;
        var increments = Balanced.Select((amount, i) => i < count ? amount : 0).ToArray();
        Tick(c, increments, 13680 - Prices.Take(count).Sum(), rejectedCall: !afterMutation);
        Equal(afterMutation ? 6 : stat + 1, c.Calls.Count, "unknown outcome blocks dependents; exact commitment permits subsequent purchases");
        Sequence(Enumerable.Range(0, afterMutation ? 6 : stat + 1), c.Calls.Select(call => call.Stat), "routing through throwing API");
        var result = NativeBoundary.Results[stat];
        Equal(afterMutation ? "ModelAppliedRenderFailed" : "UnknownOutcome", NativeBoundary.Get(result, "Disposition").ToString(), "native exception classification");
        Equal(afterMutation, NativeBoundary.Get(result, "Committed"), "commit follows exact base/currency evidence");
        Equal(true, NativeBoundary.Get(result, "Durable"), "exception evidence is durable before return");
        var error = NativeBoundary.Get(result, "Error") as TargetInvocationException;
        Check(error != null, "real reflection exception retained in finite result");
        Equal($"private purchase {(afterMutation ? "after" : "before")} mutation {stat}",
            error.InnerException?.Message, "private purchase exception preserved");
        Equal(count > 0 ? 1 : 0, Game.Logs.Count, "spend-only log follows observed debit, not exception absence");
        // Recreate the original opportunity, then reload disk evidence. Neither unknown nor committed leaves may replay.
        var calls = c.Calls.Count;
        var original = new decimal[] { 25, 800000, 20, 5, 160000, 4 };
        SetBase(c, stat, original[stat]); c.realExp = 13680;
        NativeBoundary.Set(NativeBoundary.MainInstance, "_finiteActionLedger", null);
        NativeBoundary.Call("BuyCustomExpStat", stat, Balanced[stat]);
        Equal(calls, c.Calls.Count, "imported exception evidence refuses repeat native invocation");
        Equal(original[stat], c.BaseStats()[stat], "refused replay changes no BASE stat");
        Equal(13680L, c.realExp, "refused replay debits no EXP");
    }

    private static void Randomized()
    {
        var random = new Random(250);
        var purchases = 0;
        for (var sample = 0; sample < 300; sample++)
        {
            var budget = random.Next(1, 1000000);
            var c = Reset(budget);
            c.highestBoss = random.Next(16, 40);
            c.energyPower = random.Next(1, 1000); c.capEnergy = random.Next(99990, 1000000); c.energyBars = random.Next(1, 1000);
            c.magic.magicPower = random.Next(1, 1000); c.magic.capMagic = random.Next(10000, 1000000); c.magic.magicPerBar = random.Next(1, 1000);
            c.CapLimit = 1000000; c.PowerBarLimit = 1000;
            var inputs = c.settings.Inputs();
            var unrelated = Unrelated(c);
            for (var tick = 0; tick < 5; tick++) new Game().Tick();
            purchases += c.Calls.Count;
            var spent = c.Calls.Sum(call => call.Amount / (call.Stat % 3 == 1 ? 250 : 1)
                * (call.Stat % 3 == 0 ? 150 : call.Stat % 3 == 1 ? 1 : 80) * (call.Stat < 3 ? 1 : 3));
            Equal(budget - spent, c.realExp, "random conservation sample " + sample);
            Check(c.MinimumExp >= 0, "random transient EXP underflow sample " + sample);
            Check(c.BaseStats().Select((v, i) => v <= (i % 3 == 1 ? c.CapLimit : c.PowerBarLimit)).All(x => x), "random hardcap sample " + sample);
            Sequence(inputs, c.settings.Inputs(), "random original custom inputs restored");
            Sequence(unrelated, Unrelated(c), "random unrelated state unchanged");
            Equal(0, c.res3Purchases.Calls, "random no R3");
            Equal(0, Game.Errors.Count, "random no swallowed exceptions");
        }
        Check(purchases > 300, "random conservation exercised actual native purchases, not a grant-refused noop");
    }

    private static void SetBase(Character c, int stat, decimal value)
    {
        if (stat == 0) c.energyPower = (float)value;
        else if (stat == 1) c.capEnergy = (long)value;
        else if (stat == 2) c.energyBars = (long)value;
        else if (stat == 3) c.magic.magicPower = (float)value;
        else if (stat == 4) c.magic.capMagic = (long)value;
        else c.magic.magicPerBar = (long)value;
    }

    private static long Quantum(int stat) => stat % 3 == 1 ? 250 : 1;
    private static long Price(int stat) => (stat % 3 == 0 ? 150 : stat % 3 == 1 ? 1 : 80) * (stat < 3 ? 1 : 3);

    private static void GrantRefusal(int stat)
    {
        var c = Reset(); var before = c.BaseStats(); var inputs = c.settings.Inputs();
        var field = stat < 3 ? "_manageEnergy" : "_manageMagic";
        var settings = NativeBoundary.Get(NativeBoundary.MainType, "Settings");
        NativeBoundary.Set(settings, field, false);
        NativeBoundary.Call("BuyCustomExpStat", stat, Quantum(stat));
        Equal(0, c.Calls.Count, "grant refusal occurs before native entry");
        Sequence(before, c.BaseStats(), "grant refusal leaves every BASE stat unchanged");
        Sequence(inputs, c.settings.Inputs(), "grant refusal never borrows a native input");
        Equal(13680L, c.realExp, "grant refusal debits no EXP");
        Equal("RejectedUnchanged", NativeBoundary.Get(NativeBoundary.Results.Single(), "Disposition").ToString(), "real core refuses disabled lane");
        NativeBoundary.Set(settings, field, true);
        NativeBoundary.Call("BuyCustomExpStat", stat, Quantum(stat));
        Equal(1, c.Calls.Count, "no invocation evidence blocks a newly valid grant");
        Equal(before[stat] + Quantum(stat), c.BaseStats()[stat], "valid retry purchases exact quantum");
        Equal(13680L - Price(stat), c.realExp, "valid retry pays exact native cost");
        Sequence(inputs, c.settings.Inputs(), "valid retry restores all original inputs");
    }

    private static void ChangedPrice(int stat)
    {
        var c = Reset(); var before = c.BaseStats(); var inputs = c.settings.Inputs();
        var controller = stat < 3 ? (Purchases)c.energyPurchases : c.magicPurchases;
        NativeBoundary.CostObserved = () => { controller.CostOffset = 1; NativeBoundary.CostObserved = null; };
        NativeBoundary.Call("BuyCustomExpStat", stat, Quantum(stat));
        Equal(0, c.Calls.Count, "changed native cost is rejected at finite preflight");
        Sequence(before, c.BaseStats(), "preflight price drift changes no BASE stat");
        Sequence(inputs, c.settings.Inputs(), "price drift never borrows inputs");
        Equal(13680L, c.realExp, "price drift spends no EXP");
        Equal("RejectedUnchanged", NativeBoundary.Get(NativeBoundary.Results.Single(), "Disposition").ToString(), "changed price is not unknown invocation");
        controller.CostOffset = 0;
        NativeBoundary.Call("BuyCustomExpStat", stat, Quantum(stat));
        Equal(1, c.Calls.Count, "restored price admits exactly one native purchase");
        Equal(13680L - Price(stat), c.realExp, "retry uses original native price");
    }

    private static void GrantRevoked(int stat)
    {
        var c = Reset(); var before = c.BaseStats(); var inputs = c.settings.Inputs();
        var settings = c.settings.Native;
        var checks = 0;
        object claim = null;
        NativeBoundary.CostObserved = () => {
            // The first cost is caller validation; the second is inside the real finite preflight.
            if (++checks != 2) return;
            claim = NativeBoundary.Call("BeginExecutionClaim", "manual-native-input", "preflight-revocation", NativeBoundary.Domain("NativeSettings"),
                (Func<bool>)(() => ReferenceEquals(Game.Character?.Native, c.Native) && ReferenceEquals(Game.Character?.settings?.Native, settings)));
        };
        try
        {
            NativeBoundary.Call("BuyCustomExpStat", stat, Quantum(stat));
            Check(claim != null, "real competing claim is admitted after grant resolution");
            Equal(0, c.Calls.Count, "revoked grant cannot enter the native purchase");
            Sequence(before, c.BaseStats(), "revoked grant changes no BASE stat");
            Sequence(inputs, c.settings.Inputs(), "revoked grant never borrows native inputs");
            Equal(13680L, c.realExp, "revoked grant debits no EXP");
            Equal("RejectedUnchanged", NativeBoundary.Get(NativeBoundary.Results.Single(), "Disposition").ToString(), "real preflight revalidates grant after native cost observation");
        }
        finally
        {
            NativeBoundary.CostObserved = null;
            if (claim != null) NativeBoundary.Call("EndExecutionClaim", claim, false);
        }
        NativeBoundary.Call("BuyCustomExpStat", stat, Quantum(stat));
        Equal(1, c.Calls.Count, "fresh grant after claim release admits exactly one purchase");
        Equal(before[stat] + Quantum(stat), c.BaseStats()[stat], "fresh grant buys exact native quantum");
        Equal(13680L - Price(stat), c.realExp, "fresh grant pays exact native price");
        Sequence(inputs, c.settings.Inputs(), "fresh grant returns borrowed input");
    }

    private static void ForeignInput(int stat)
    {
        var c = Reset(); var inputs = c.settings.Inputs(); var before = c.BaseStats();
        var controller = stat < 3 ? (Purchases)c.energyPurchases : c.magicPurchases;
        controller.Presented = () => NativeBoundary.Set(c.settings.Native, GameSettings.Fields[stat], 999L);
        NativeBoundary.Call("BuyCustomExpStat", stat, Quantum(stat));
        inputs[stat] = 999;
        Sequence(inputs, c.settings.Inputs(), "foreign borrowed-input edit survives return");
        Equal(before[stat] + Quantum(stat), c.BaseStats()[stat], "foreign input cannot change already invoked amount");
        Equal(13680L - Price(stat), c.realExp, "foreign input pays only captured amount");
        Equal(true, NativeBoundary.Get(NativeBoundary.Results.Single(), "Committed"), "exact native commitment retained");
        Equal(1, c.Calls.Count, "foreign edit causes no extra invocation");
    }

    private static void CommittedReplay(int stat)
    {
        var c = Reset(); var original = c.BaseStats()[stat]; var inputs = c.settings.Inputs();
        NativeBoundary.Call("BuyCustomExpStat", stat, Quantum(stat));
        Equal(true, NativeBoundary.Property(NativeBoundary.Results.Single(), "Verified"), "first clean purchase has real current durable proof");
        SetBase(c, stat, original); c.realExp = 13680;
        NativeBoundary.Set(NativeBoundary.MainInstance, "_finiteActionLedger", null);
        NativeBoundary.Call("BuyCustomExpStat", stat, Quantum(stat));
        var repeat = NativeBoundary.Results.Last();
        Equal(true, NativeBoundary.Get(repeat, "PreviouslyCommitted"), "disk ledger recognizes exact original opportunity");
        Equal(false, NativeBoundary.Property(repeat, "Verified"), "history is not fresh authority");
        Equal(1, c.Calls.Count, "imported commitment cannot invoke twice");
        Equal(original, c.BaseStats()[stat], "replay changes no BASE value");
        Equal(13680L, c.realExp, "replay changes no currency");
        Sequence(inputs, c.settings.Inputs(), "replay borrows no native input");
    }

    private static void LifecycleRefusal(string lifecycle)
    {
        var c = Reset();
        NativeBoundary.Call("SetExecutionLifecycle", Enum.Parse(NativeBoundary.MainType.GetNestedType("ExecutionLifecycle", NativeBoundary.All), lifecycle));
        Tick(c, new long[6], 13680);
        Equal(6, NativeBoundary.Results.Count, "all six planned leaves reach real lifecycle refusal");
        Check(NativeBoundary.Results.All(r => NativeBoundary.Get(r, "Disposition").ToString() == "RejectedUnchanged"), "quiesced core rejects every native leaf");
    }

    private static void ClaimRefusal()
    {
        var c = Reset(); var native = c.Native; var settings = c.settings.Native;
        var claim = NativeBoundary.Call("BeginExecutionClaim", "manual-native-input", "autoexp-check", NativeBoundary.Domain("NativeSettings"),
            (Func<bool>)(() => ReferenceEquals(Game.Character?.Native, native) && ReferenceEquals(Game.Character?.settings?.Native, settings)));
        Tick(c, new long[6], 13680);
        Equal(6, NativeBoundary.Results.Count, "real conflicting claim withholds all six finite actions");
        NativeBoundary.Call("EndExecutionClaim", claim, false);
        NativeBoundary.Results.Clear();
        Tick(c, Balanced, 0);
    }

    private static void PresentationFailure(bool magic)
    {
        var c = Reset(); var stat = magic ? 3 : 0; var before = c.BaseStats(); var inputs = c.settings.Inputs();
        var controller = magic ? (Purchases)c.magicPurchases : c.energyPurchases;
        controller.Presented = () => { if (controller.RefreshCalls == 2) throw new InvalidOperationException("post-commit presentation failed"); };
        PrepareDisplays(c);
        new Game().Tick();
        var count = stat + 1;
        Sequence(before.Select((v, i) => v + (i < count ? Balanced[i] : 0)), c.BaseStats(), "outer refresh exception follows exact committed prefix");
        Equal(13680L - Prices.Take(count).Sum(), c.realExp, "outer presentation exception preserves exact debit");
        Sequence(inputs, c.settings.Inputs(), "outer presentation exception preserves restored inputs");
        Equal(1, Game.Errors.Count, "outer presentation exception reported once");
        Equal("post-commit presentation failed", Game.Errors[0].Error.Message, "presentation exception remains distinguishable from native invocation");
        Equal(true, NativeBoundary.Get(NativeBoundary.Results.Last(), "Durable"), "outer rendering occurs only after durable result");
        controller.Presented = null;
        SetBase(c, stat, before[stat]); c.realExp = 13680;
        NativeBoundary.Set(NativeBoundary.MainInstance, "_finiteActionLedger", null);
        NativeBoundary.Call("BuyCustomExpStat", stat, Balanced[stat]);
        Equal(count, c.Calls.Count, "outer refresh failure never replays the committed native purchase");
    }

    private static void ResultStorage(bool import)
    {
        var c = Reset(); var before = c.BaseStats(); var inputs = c.settings.Inputs();
        var identity = NativeBoundary.Call("ActionIdentity", "exp-purchase:0", "auto-exp", before[0].ToString(CultureInfo.InvariantCulture), null);
        var hash = NativeBoundary.Invoke(NativeBoundary.Plugin.GetType("NGUInjector.JsonFiles", true), "ComputeRevision", Encoding.UTF8.GetBytes((string)NativeBoundary.Get(identity, "Key")));
        var file = Path.Combine((string)NativeBoundary.Get(NativeBoundary.MainType, "_dir"), "finite-actions-v1", hash + ".json");
        FileStream held = null;
        c.energyPurchases.Presented = () => { if (held == null) held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read); };
        try
        {
            NativeBoundary.Call("BuyCustomExpStat", 0, 1L);
            var first = NativeBoundary.Results.Single();
            Equal(true, NativeBoundary.Get(first, "Committed"), "result storage fault does not erase real native commitment");
            Equal(false, NativeBoundary.Get(first, "Durable"), "result storage fault cannot publish durable success");
            Equal(before[0] + 1, c.BaseStats()[0], "native model applied exactly once before storage fault");
            Equal(13530L, c.realExp, "exact native debit before storage fault");
            Sequence(inputs, c.settings.Inputs(), "input return precedes result persistence");
            NativeBoundary.Call("BuyCustomExpStat", 0, 1L);
            Equal(1, c.Calls.Count, "dirty committed result withholds a dependent native successor");
        }
        finally { c.energyPurchases.Presented = null; held?.Dispose(); }
        if (import) NativeBoundary.Set(NativeBoundary.MainInstance, "_finiteActionLedger", null);
        NativeBoundary.Call("BuyCustomExpStat", 0, 1L);
        Equal(import ? 1 : 2, c.Calls.Count, "imported invocation blocks replay; in-session durable flush admits only successor");
        Equal(before[0] + (import ? 1 : 2), c.BaseStats()[0], "recovery never repeats predecessor accounting");
        Equal(import ? 13530L : 13380L, c.realExp, "recovery has exact currency accounting");
        Equal(import ? "UnknownOutcome" : "AppliedVerified", NativeBoundary.Get(NativeBoundary.Results.Last(), "Disposition").ToString(), "recovery follows actual durable evidence");
    }

    private static void UnknownCurrency(bool magic)
    {
        var c = Reset(); var stat = magic ? 3 : 0; var before = c.BaseStats(); var inputs = c.settings.Inputs();
        var controller = magic ? (Purchases)c.magicPurchases : c.energyPurchases;
        c.menuID = -1;
        controller.AfterPurchase = () => c.realExp = 0;
        NativeBoundary.Call("BuyCustomExpStat", stat, 1L);
        Equal(before[stat] + 1, c.BaseStats()[stat], "actual native BASE mutation is observable despite unexpected currency consumer");
        Equal(0L, c.realExp, "unexpected native currency consumer is retained, not repaired");
        Sequence(inputs, c.settings.Inputs(), "unknown native outcome still returns the owned input field");
        Equal("UnknownOutcome", NativeBoundary.Get(NativeBoundary.Results.Single(), "Disposition").ToString(), "wrong currency delta cannot claim verified commitment");
        Equal(false, NativeBoundary.Get(NativeBoundary.Results.Single(), "Committed"), "unknown accounting publishes no committed success");
        Equal(1, controller.RefreshCalls, "unknown outcome gets no durable-success presentation refresh");
        c.realExp = 13680;
        NativeBoundary.Set(NativeBoundary.MainInstance, "_finiteActionLedger", null);
        foreach (var next in Enumerable.Range(0, 6)) NativeBoundary.Call("BuyCustomExpStat", next, Quantum(next));
        Equal(1, c.Calls.Count, "imported unknown currency evidence withholds every NativeSettings-dependent leaf");
        Equal(13680L, c.realExp, "unknown evidence never spends replacement currency");
    }
}
