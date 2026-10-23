using System;
using System.Linq;
using NGUInjector.Managers;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message)
    {
        Check(Equals(expected, actual), message + ": expected " + expected + ", got " + actual);
    }
    private static DiggerBudgetRequest Request(int id, double weight, double drain, long maximum = 100)
    {
        return new DiggerBudgetRequest { Id = id, Weight = weight, BaseDrain = drain, Growth = 2, Maximum = maximum };
    }
    private static void Main()
    {
        Arithmetic();
        Planning();
        SelectedSupportBudgets();
        ExactSupportDoesNotResize();
        CapturedPolicyIsAuthoritative();
        Console.WriteLine("PASS support regressions: " + assertions + " assertions; production allocation, support projection and activity policy, no legacy writers.");
    }

    private static void Arithmetic()
    {
        Equal(1L, DiggerAllocation.AffordableLevel(100, 2, 100, 150), "level-one offset");
        Equal(3L, DiggerAllocation.AffordableLevel(100, 2, 100, 750), "level-three offset");
        Equal(0L, DiggerAllocation.AffordableLevel(100, 2, 100, 99), "unaffordable level one");
        Equal(long.MaxValue, DiggerAllocation.AffordableLevel(100, 1, long.MaxValue, 100), "constant drain integer limit");
        Equal(long.MaxValue, DiggerAllocation.AffordableLevel(double.NaN, double.NaN, long.MaxValue, 0, false), "non-consuming free maximum");
        Equal(0L, DiggerAllocation.AffordableLevel(100, 2, 100, double.NaN), "invalid budget");
        Equal(0L, DiggerAllocation.AffordableLevel(100, 2, 100, double.PositiveInfinity), "infinite budget fails closed");
        Equal(0L, DiggerAllocation.AffordableLevel(100, double.NaN, 100, 1000), "invalid growth");
        Equal(0L, DiggerAllocation.AffordableLevel(-1, 2, 100, 1000), "negative drain");
        foreach (var growth in new[] { 1d, 1.0001, 1.1, 2d, 10d })
        foreach (var baseDrain in new[] { 0d, double.Epsilon, 1e-100, 1d, 100d, 1e100 })
        for (var level = 1; level <= 100; level++)
        {
            var drain = baseDrain == 0 ? 0 : baseDrain * Math.Pow(growth, level - 1);
            if (double.IsInfinity(drain)) continue;
            foreach (var budget in new[] { Math.BitDecrement(drain), drain, Math.BitIncrement(drain) }.Where(x => x >= 0))
            {
                var expected = Enumerable.Range(1, 100).Where(x => (baseDrain == 0 ? 0 : baseDrain * Math.Pow(growth, x - 1)) <= budget)
                    .Select(x => (long)x).DefaultIfEmpty(0).Max();
                var actual = DiggerAllocation.AffordableLevel(baseDrain, growth, 100, budget);
                Equal(expected, actual, "forward verified adjacent floating boundary");
                Check(DiggerAllocation.Drain(baseDrain, growth, actual) <= budget, "forward drain within budget");
            }
        }
        var huge = DiggerAllocation.AffordableLevel(1, 1.0000000000000002, long.MaxValue, double.MaxValue);
        Check(huge > 0 && huge < long.MaxValue, "large integer search has finite bound");
        Check(DiggerAllocation.Drain(1, 1.0000000000000002, huge) <= double.MaxValue, "large integer forward bound");
        Check(double.IsInfinity(DiggerAllocation.Drain(1, 1.0000000000000002, huge + 1)), "large integer next level rejected");
    }

    private static void Planning()
    {
        var requests = new[] { Request(1, 100, 1000), Request(0, 20, 100) };
        var plan = DiggerAllocation.Plan(requests, 1, 150);
        Equal(0, plan.Single().Id, "unaffordable primary backfills slot");
        requests = new[] { Request(0, 100, 90), Request(1, 90, 50), Request(2, 10, 1) };
        plan = DiggerAllocation.Plan(requests, 2, 100);
        Equal("0,2", string.Join(",", plan.Select(x => x.Id)), "share rejection backfills and recalculates");
        Check(plan.Sum(x => x.Drain) <= 100, "whole budget");
        requests = new[] { Request(2, 10, 1), Request(1, 5, 1), Request(1, 5, 1), Request(0, 10, 1) };
        plan = DiggerAllocation.Plan(requests, 3, 100);
        Equal("0,1,2", string.Join(",", plan.Select(x => x.Id)), "aggregated weights and ID ties");
        Equal(string.Join(",", plan.Select(x => x.Level)), string.Join(",", DiggerAllocation.Plan(requests.Reverse(), 3, 100).Select(x => x.Level)), "input permutation stable");
        var free = Request(0, 100, double.NaN, long.MaxValue);
        free.ConsumesGold = false;
        plan = DiggerAllocation.Plan(new[] { free }, 1, 0);
        Equal(long.MaxValue, plan.Single().Level, "free request at zero budget");
        Equal(0d, plan.Single().Drain, "free drain");
        requests = new[] { Request(0, double.MaxValue, double.MaxValue), Request(1, double.Epsilon, 0) };
        Check(DiggerAllocation.Plan(requests, 1, 1).All(x => !double.IsNaN(x.Drain)), "extreme weights do not produce NaN");
        var random = new Random(314159);
        for (var run = 0; run < 5000; run++)
        {
            var budget = Math.Pow(10, random.Next(-200, 201)) * random.NextDouble();
            var weights = Enumerable.Range(0, 12).Select(_ => random.Next(1, 100)).ToArray();
            var total = weights.Sum();
            requests = Enumerable.Range(0, 12).Select(id => Request(id, weights[id], budget * ((double)weights[id] / total), 1)).ToArray();
            plan = DiggerAllocation.Plan(requests, 12, budget);
            Check(plan.Sum(x => x.Drain) <= budget, "floating-boundary whole plan budget");
            Check(plan.OrderBy(x => x.Id).Sum(x => x.Drain) <= budget, "native ID-order whole plan budget");
        }
    }

    private static SupportContext Context(double gross, int slots)
    {
        return new SupportContext { BeardSlots = 7, DiggerSlots = slots, UnlockedBeards = Enumerable.Range(0, 7).ToArray(),
            GrossGold = gross, GoldActiveGross = gross * 10, GoldInactiveGross = gross,
            Diggers = Enumerable.Range(0, 12).Select(id => new SupportDigger { Id = id, Maximum = 100,
                BaseDrain = 1, Growth = 2, ConsumesGold = true }).ToArray() };
    }

    private static void SelectedSupportBudgets()
    {
        var fraction = ActivityPolicy.Current.DiggerBudgetFraction;
        Equal(.75, fraction, "actual policy retains its 75 percent gross-GPS budget");
        foreach (var budget in new[] { 150d, 750d })
        {
            var context = Context(budget / fraction, 1);
            context.Diggers[0].BaseDrain = 100;
            var planned = SupportProjection.Plan(context, new int[0], new[] { Request(0, 1, 0) }, fraction);
            Check(planned.Feasible, "selected weighted support is feasible");
            Equal(budget == 150 ? 1L : 3L, planned.Selection.Diggers.Single().Level, "selected support uses forward level-one offset");
            Check(planned.Drain <= budget, "selected weighted support conserves policy budget");
        }
        var c = Context(200, 1);
        c.Diggers[1].BaseDrain = 1000; c.Diggers[0].BaseDrain = 100;
        var requests = new[] { Request(1, 100, 0), Request(0, 20, 0) };
        var off = SupportProjection.Plan(c, new int[0], requests, fraction);
        var on = SupportProjection.Plan(c, new[] { 6 }, requests, fraction);
        Equal(0, off.Selection.Diggers.Single().Id, "Gold-off budget backfills before mutation");
        Equal(1, on.Selection.Diggers.Single().Id, "selected Gold funds the policy primary");
        Equal(150d, off.Budget, "Gold-off budget is calculated from selected support");
        Equal(1500d, on.Budget, "Gold-on budget is calculated from selected support");
        c = Context(0, 1); c.Diggers[8].ConsumesGold = false;
        var free = SupportProjection.Plan(c, new int[0], new[] { Request(8, 1, 0) }, fraction);
        Check(free.Feasible && free.Drain == 0, "free support is feasible at zero income");
        Equal(100L, free.Selection.Diggers.Single().Level, "free support reaches purchased maximum");
        c = Context(double.NaN, 1);
        Check(!SupportProjection.Plan(c, new int[0], requests, fraction).Feasible, "invalid native income rejects support");

        var random = new Random(12345);
        for (var run = 0; run < 1000; run++)
        {
            c = Context(random.Next(1, 100000), random.Next(1, 13));
            foreach (var d in c.Diggers) d.BaseDrain = random.Next(1, 1000);
            requests = Enumerable.Range(0, 12).Select(id => Request(id, random.Next(1, 1000), 0)).ToArray();
            var result = SupportProjection.Plan(c, run % 2 == 0 ? new[] { 6 } : new int[0], requests, fraction);
            Check(result.Feasible, "random selected support remains feasible");
            Check(result.Drain <= result.GrossGold * fraction, "random selection obeys entire 75 percent budget");
            Check(result.Selection.Diggers.Length <= c.DiggerSlots, "random selection obeys native slots");
            Check(result.Selection.Diggers.All(d => d.Level > 0 && d.Level <= c.Diggers[d.Id].Maximum), "random selection obeys purchased levels");
        }
    }

    private static void ExactSupportDoesNotResize()
    {
        var c = Context(200, 2);
        var pinned = new SupportSelection(new[] { 6 }, new[] { new SupportDiggerLevel(0, 3), new SupportDiggerLevel(3, 2) });
        var key = pinned.StableKey;
        Check(SupportProjection.Evaluate(c, pinned, .75, true).Feasible, "exact acquired support is affordable");
        c.GoldActiveGross = 5;
        var result = SupportProjection.Evaluate(c, pinned, .75, true);
        Equal("Exact digger selection is no longer affordable.", result.Reason, "income loss rejects exact support instead of restoring cheaper historical levels");
        Equal(key, result.Selection.StableKey, "failed funding preserves acquired exact selection");
        c.GoldActiveGross = 2000; c.DiggerSlots = 1;
        Check(!SupportProjection.Evaluate(c, pinned, .75, true).Feasible, "slot loss rejects rather than dropping a selected digger");
        c.DiggerSlots = 2; c.Diggers[0].Maximum = 2;
        Check(!SupportProjection.Evaluate(c, pinned, .75, true).Feasible, "purchased-level loss rejects rather than recapping");
        c.Diggers[0].Maximum = 100;
        c.BeardsAvailable = false;
        Check(!SupportProjection.Evaluate(c, pinned, .75, true).Feasible, "unavailable selected beards reject");
        c.BeardsAvailable = true; c.DiggersAvailable = false;
        Check(!SupportProjection.Evaluate(c, pinned, .75, true).Feasible, "unavailable selected diggers reject");
        c.DiggersAvailable = true; c.GoldActiveGross = 7;
        Check(!SupportProjection.Evaluate(c, pinned, .75, true).Feasible, "planned support enforces configured fraction");
        Check(SupportProjection.Evaluate(c, pinned, .75, false).Feasible, "pinned current mode can exceed fraction while sustainable");
        c.GoldActiveGross = 5;
        Check(!SupportProjection.Evaluate(c, pinned, .75, false).Feasible, "pinned current mode cannot exceed actual income");
        Equal(key, pinned.StableKey, "all exact validation is read-only");
    }

    private static void CapturedPolicyIsAuthoritative()
    {
        var c = Context(200, 1);
        c.Diggers[0].BaseDrain = 100; c.Diggers[0].Maximum = 3;
        var request = Request(0, 1, 0, long.MaxValue);
        request.ConsumesGold = false;
        var result = SupportProjection.Plan(c, new int[0], new[] { request }, .75);
        Equal(1L, result.Selection.Diggers.Single().Level, "request cannot counterfeit captured drain or consumesGold");
        c.GoldInactiveGross = 1e9;
        result = SupportProjection.Plan(c, new int[0], new[] { request }, .75);
        Equal(3L, result.Selection.Diggers.Single().Level, "request cannot exceed captured purchased maximum");
        request.Maximum = 2;
        Equal(2L, SupportProjection.Plan(c, new int[0], new[] { request }, .75).Selection.Diggers.Single().Level,
            "caller may choose a narrower acquired maximum");
    }
}
