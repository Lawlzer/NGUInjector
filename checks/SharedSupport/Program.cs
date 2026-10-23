using System;
using System.Linq;
using NGUInjector.Managers;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    private static SupportSelection Select(int[] beards, params SupportDiggerLevel[] diggers) { return new SupportSelection(beards, diggers); }
    private static SupportContext Context()
    {
        return new SupportContext { BeardSlots = 2, DiggerSlots = 2, UnlockedBeards = Enumerable.Range(0, 7).ToArray(),
            GrossGold = 100, GoldActiveGross = 400, GoldInactiveGross = 100,
            BeardTemporary = new[] { 2f, 4f, 3f, 5f }, BeardPermanent = new[] { 1.25f, 1.5f, 2f, 3f },
            BeardWTemporary = 1.234567f, BeardWPermanent = 2.345678f,
            BeardStatTemporary = 1.23456789012345, BeardStatPermanent = 2.34567890123456,
            DiggerLevelBonus = 1.234567f,
            Diggers = Enumerable.Range(0, 12).Select(id => new SupportDigger { Id = id, Maximum = 100, Growth = 2,
                BaseDrain = 10, ConsumesGold = true, StartBonus = .123456789, PerLevel = .000123456789 }).ToArray() };
    }
    private static void Main()
    {
        var ids = new[] { 4, 0 }; var levels = new[] { new SupportDiggerLevel(2, 9007199254740993L), new SupportDiggerLevel(0, 3) };
        var selection = Select(ids, levels); var key = selection.StableKey; ids[0] = 6; levels[0] = new SupportDiggerLevel(1, 1);
        selection.Beards[0] = 6; selection.Diggers[0] = new SupportDiggerLevel(1, 1);
        Check(selection.StableKey == key && selection.Beards.SequenceEqual(new[] { 0, 4 }) && selection.Diggers[1].Level == 9007199254740993L, "selection owns defensive canonical arrays and exact longs");
        Check(key == Select(new[] { 0, 4 }, new SupportDiggerLevel(0, 3), new SupportDiggerLevel(2, 9007199254740993L)).StableKey, "permutation independent exact stable key");
        var stateLevels = new[] { new SupportDiggerLevel(11, 9) }; var state = new SupportState(selection, stateLevels);
        stateLevels[0] = new SupportDiggerLevel(11, 1); state.Levels[0] = new SupportDiggerLevel(11, 2);
        Check(state.Levels[0].Level == 9 && state.StableKey != new SupportState(selection, stateLevels).StableKey, "state owns and keys inactive levels");
        var c = Context();
        var e = SupportProjection.Evaluate(c, Select(new[] { 0, 4 }, new SupportDiggerLevel(2, 4)), 1, true);
        Check(e.Feasible && e.DiggerStat == (1d + (.123456789 + Math.Pow(4, 3d) * .000123456789)) * (float)c.DiggerLevelBonus, "stat digger is cubic DOUBLE, not linear float");
        Check(e.BeardStat == c.BeardStatTemporary * c.BeardStatPermanent && e.BeardStat != (double)(float)e.BeardStat, "stat beard preserves double precision");
        Check(e.BeardW == (double)(float)(c.BeardWTemporary * c.BeardWPermanent), "W beard uses float precision");
        Check(SupportProjection.BeardBonus(c, new int[0], 0) == c.BeardStatPermanent && SupportProjection.BeardBonus(c, new int[0], 4) == c.BeardWPermanent,
            "inactive W/Stat retain permanent bonuses even in a context without cached effective getters");
        Check(e.Drain == 80 && e.GrossGold == 100 && e.NetGold == 20 && e.Budget == 100, "named financial evaluation");
        Check(!SupportProjection.Evaluate(c, e.Selection, .5, true).Feasible && SupportProjection.Evaluate(c, e.Selection, .5, false).Feasible, "current mode ignores policy budget, not sustainability");
        Check(!SupportProjection.Evaluate(c, Select(new int[0], new SupportDiggerLevel(2, 5)), 1, false).Feasible, "current mode still rejects negative net GPS");
        Check(!SupportProjection.Evaluate(c, Select(new[] { 0, 0 }), 1, true).Feasible, "duplicate beards rejected, not normalized away");
        Check(!SupportProjection.Evaluate(c, Select(new int[0], new SupportDiggerLevel(0, 1), new SupportDiggerLevel(0, 2)), 1, true).Feasible, "duplicate diggers rejected");
        Check(!SupportProjection.Evaluate(c, Select(new int[0], new SupportDiggerLevel(0, 101)), 1, true).Feasible, "purchased bound enforced");
        Check(!SupportProjection.Evaluate(c, Select(new int[0], new SupportDiggerLevel(0, 0)), 1, true).Feasible, "zero active digger level rejected");
        c.BeardsDisabled = true;
        Check(SupportProjection.BeardBonus(c, new[] { 0 }, 0) == 1 && SupportProjection.BeardBonus(c, new[] { 4 }, 4) == 1, "disabled beard suppresses permanent and temporary factors");
        Check(!SupportProjection.Evaluate(c, Select(new[] { 4 }), 1, true).Feasible, "disabled beard cannot be selected");
        Check(SupportProjection.Plan(c, new[] { 0, 4 }, new DiggerBudgetRequest[0], 1).Selection.Beards.Length == 0, "disabled planning selects empty");
        c = Context(); c.Diggers[4].BaseDrain = 120; c.DiggerSlots = 1;
        var requests = new[] { new DiggerBudgetRequest { Id = 4, Maximum = 100, Weight = 2 }, new DiggerBudgetRequest { Id = 0, Maximum = 100, Weight = 1 } };
        var goldOn = SupportProjection.Plan(c, new[] { 6, 6, 0, 4 }, requests, .75);
        var goldOff = SupportProjection.Plan(c, new[] { 4 }, requests, .75);
        Check(goldOn.Selection.Beards.SequenceEqual(new[] { 0, 6 }) && goldOn.Selection.Diggers.Single().Id == 4, "caller priorities only, legal distinct capacity selection");
        Check(goldOff.Selection.Diggers.Single().Id == 0 && goldOff.Selection.Diggers.Single().Level == 3 && goldOff.Budget == 75, "Gold-off GPS funds affordable backfill before allocation");
        c.BeardsAvailable = false; c.DiggersAvailable = false;
        Check(SupportProjection.Plan(c, new[] { 6 }, requests, 1).Selection.StableKey == "|", "unavailable support plans empty");
        c = Context(); c.GoldActiveGross = c.GoldInactiveGross = 0; c.Diggers[0].ConsumesGold = false; c.DiggerSlots = 1;
        e = SupportProjection.Plan(c, new int[0], requests, 0);
        Check(e.Feasible && e.Drain == 0 && e.Selection.Diggers.Single().Id == 0 && e.Selection.Diggers.Single().Level == 100, "free digger still takes a slot at zero GPS");
        Check(!SupportProjection.Evaluate(c, e.Selection, double.NaN, true).Feasible, "nonfinite budget fraction rejected");
        var random = new Random(529);
        for (var i = 0; i < 400; i++)
        {
            c = Context(); var level = random.Next(1, 90); var id = i % 12;
            c.Diggers[id].ConsumesGold = false;
            e = SupportProjection.Evaluate(c, Select(new int[0], new SupportDiggerLevel(id, level)), 1, true);
            var effect = SupportProjection.DiggerBonus(c, e.Selection, id);
            var expected = id == 2 ? Math.Max(1d, (1d + (c.Diggers[id].StartBonus + Math.Pow(level, 3) * c.Diggers[id].PerLevel)) * (float)c.DiggerLevelBonus)
                : (double)Math.Max(1f, (float)((float)(1f + (float)(c.Diggers[id].StartBonus + level * c.Diggers[id].PerLevel)) * (float)c.DiggerLevelBonus));
            Check(e.Feasible && effect == expected, "literal native getter precision oracle " + i);
            var baseGold = random.NextDouble() * 1e20; var blood = (float)(1 + random.NextDouble()); var beard = (float)(1 + random.NextDouble());
            var ngu = 1 + random.NextDouble(); var challenge = 1 + random.NextDouble();
            var gps = baseGold * 7L * 13L * 3L * 17L * blood * beard * ngu * challenge;
            Check(SupportProjection.ForwardGrossGold(baseGold, 7, 13, 3, 17, blood, beard, ngu, challenge, false) == gps, "forward native ordered GPS oracle " + i);
            Check(SupportProjection.ForwardGrossGold(baseGold, 7, 13, 3, 17, blood, beard, ngu, challenge, true) == 0, "time machine challenge zero GPS " + i);
        }
        Console.WriteLine("PASS: " + assertions + " shared support assertions. Pure numeric checks only.");
    }
}
