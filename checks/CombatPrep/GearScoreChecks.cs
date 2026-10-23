using System;
using System.Reflection;

// Entry point for the workstation's existing GearSearchSourceChecks.exe source-isolation
// harness. It compiles the actual optimizer's scoring/search source, not a scoring copy.
internal static class GearSearchChecks
{
    private const BindingFlags Members = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static int Run(Assembly assembly)
    {
        var optimizer = assembly.GetType("NGUInjector.Managers.GearOptimizer", true);
        var stat = optimizer.GetNestedType("StatKey", BindingFlags.NonPublic);
        var goal = assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true);
        var mode = assembly.GetType("NGUInjector.Managers.FightScoreMode", true);
        var totals = new double[Enum.GetValues(stat).Length];
        foreach (var value in Enum.GetValues(stat)) totals[(int) value] = 100;
        Action<string, double> put = (name, value) => totals[(int) Enum.Parse(stat, name)] = value;
        put("EnergyCap", 300); put("EnergyPower", 400); put("MagicCap", 900); put("MagicPower", 900);
        put("AdvancedTrainingSpeed", 200); put("WandoosSpeed", 200);
        var score = optimizer.GetMethod("ScoreTotals", Members);
        var at = (double) score.Invoke(null, new[] { Enum.Parse(goal, "AdvancedTraining"), Enum.Parse(mode, "SqrtPowerToughness"), totals });
        var w = (double) score.Invoke(null, new[] { Enum.Parse(goal, "Wandoos"), Enum.Parse(mode, "SqrtPowerToughness"), totals });
        Near(12, at, "AT = cap * sqrt(power) * speed");
        Near(6 * Math.Sqrt(3), w, "W = sqrt(Ecap*Mcap) * speed, no resource powers");
        var stateType = optimizer.GetNestedType("SearchState", BindingFlags.NonPublic);
        var optionType = optimizer.GetNestedType("SearchOption", BindingFlags.NonPublic);
        foreach (var name in new[] { "AdvancedTraining", "Wandoos" })
        {
            var state = Activator.CreateInstance(stateType, true);
            stateType.GetProperty("Goal", Members).SetValue(state, Enum.Parse(goal, name));
            stateType.GetProperty("BestScore", Members).SetValue(state, name == "Wandoos" ? w : at);
            var relevant = (Array) optimizer.GetMethod("GetRelevantStats", Members).Invoke(null, new[] { Enum.Parse(goal, name) });
            stateType.GetProperty("RelevantStats", Members).SetValue(state, relevant);
            stateType.GetField("MainOptions", Members).SetValue(state, Array.CreateInstance(optionType.MakeArrayType(), 0));
            stateType.GetField("AccessoryOptions", Members).SetValue(state, Array.CreateInstance(optionType.MakeArrayType(), 0));
            stateType.GetField("LinearWeights", Members).SetValue(state, new double[totals.Length]);
            stateType.GetField("LinearMainBounds", Members).SetValue(state, new double[1]);
            stateType.GetField("LinearAccessoryBounds", Members).SetValue(state, new double[1, 1]);
            optimizer.GetMethod("UpdateLinearBounds", Members).Invoke(null, new[] { totals, state });
            var weights = (double[]) stateType.GetField("LinearWeights", Members).GetValue(state);
            Near(name == "Wandoos" ? 2 : 2.5, (double) stateType.GetField("LinearExponent", Members).GetValue(state), name + " AM-GM exponent");
            Near(name == "Wandoos" ? 0 : .5 / 400, weights[(int) Enum.Parse(stat, "EnergyPower")], name + " power bound weight");
            Near(0, weights[(int) Enum.Parse(stat, "MagicPower")], name + " excludes magic power bound weight");
        }
        Console.WriteLine("Combat prep gear scoring checks passed: 8 assertions.");
        return 8;
    }

    private static void Near(double expected, double actual, string name)
    {
        if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(expected - actual) > 1e-12 * Math.Max(1, expected))
            throw new Exception(name + ": expected " + expected + ", got " + actual);
    }
}
