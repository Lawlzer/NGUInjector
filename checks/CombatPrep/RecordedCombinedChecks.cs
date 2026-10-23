using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static partial class Program
{
    private static int AnalyzeRecordedCombined(string directory)
    {
        // An optional offline reader, never a bridge client. Original recordings are not written.
        var samples = new List<JsonElement>();
        foreach (var line in File.ReadLines(Path.Combine(directory, "runtime-samples.jsonl")))
        {
            using (var json = JsonDocument.Parse(line))
            {
                var runtime = json.RootElement.GetProperty("runtime");
                var prep = runtime.GetProperty("combatPrep");
                if (prep.GetProperty("runId").GetString() == "0325d257c22e4a1f9aa65fe89d5ebd1f") samples.Add(runtime.Clone());
            }
        }
        var unique = samples.GroupBy(x => x.GetProperty("updatedAt").GetString()).Select(x => x.First()).ToArray();
        if (unique.Length < 2) throw new Exception("Expected recorded run observations.");
        var first = unique[0];
        var last = unique.First(x => !x.GetProperty("combatPrep").GetProperty("active").GetBoolean());
        Func<JsonElement, double> rebirth = x => x.GetProperty("character").GetProperty("rebirthSeconds").GetDouble();
        Func<JsonElement, double> phaseTime = x => x.GetProperty("combatPrep").GetProperty("phaseElapsedSeconds").GetDouble();
        Console.WriteLine("Recorded run unique observations=" + unique.Length);
        Console.WriteLine("First/terminal rebirth=" + rebirth(first).ToString("R") + "/" + rebirth(last).ToString("R"));
        Console.WriteLine("Observed phase/rebirth deltas=" + (phaseTime(last) - phaseTime(first)).ToString("R") + "/" + (rebirth(last) - rebirth(first)).ToString("R"));
        Console.WriteLine("Terminal rebirth deadline gap=" + (last.GetProperty("combatPrep").GetProperty("deadlineRebirthSeconds").GetDouble() - rebirth(last)).ToString("R"));
        foreach (var lane in new[] { "augmentLevel", "upgradeLevel", "wandoosMagicLevel" })
        {
            var transitions = new List<string>();
            var events = new List<Tuple<long, double, double>>();
            for (var i = 1; i < unique.Length && rebirth(unique[i - 1]) < rebirth(last); i++)
            {
                var prior = unique[i - 1].GetProperty("combatPrep").GetProperty("actual").GetProperty(lane).GetString();
                var level = unique[i].GetProperty("combatPrep").GetProperty("actual").GetProperty(lane).GetString();
                if (prior != level)
                {
                    transitions.Add(level + " @ phase(" + phaseTime(unique[i - 1]).ToString("F6") + "," + phaseTime(unique[i]).ToString("F6") + "] rebirth(" + rebirth(unique[i - 1]).ToString("F6") + "," + rebirth(unique[i]).ToString("F6") + "]");
                    events.Add(Tuple.Create(long.Parse(level, CultureInfo.InvariantCulture), rebirth(unique[i - 1]), rebirth(unique[i])));
                }
            }
            Console.WriteLine(lane + " transition count=" + transitions.Count);
            foreach (var transition in transitions.Skip(Math.Max(0, transitions.Count - 6))) Console.WriteLine("  " + transition);
            if (lane != "wandoosMagicLevel" && events.Count > 1)
            {
                var early = events[0]; var late = events[events.Count - 1];
                var bars = late.Item1 - early.Item1;
                var denominators = (early.Item1 + 1 + late.Item1) * bars / 2d;
                var minSpan = late.Item2 - early.Item3; var maxSpan = late.Item3 - early.Item2;
                var lowCoefficient = denominators / (50 * maxSpan);
                var highCoefficient = denominators / (50 * minSpan - bars);
                var nextLow = late.Item2 + (late.Item1 + 1) / (50 * highCoefficient);
                var nextHigh = late.Item3 + (late.Item1 + 1) / (50 * lowCoefficient) + .02;
                Console.WriteLine("  Conditional 50 ticks/rebirth-second q=C/(L+1): C in [" + lowCoefficient.ToString("F9") + "," + highCoefficient.ToString("F9") + "]; next completion rebirth in [" + nextLow.ToString("F6") + "," + nextHigh.ToString("F6") + "]. Not measured callback counts or captured coefficients.");
            }
        }
        var finalActual = last.GetProperty("combatPrep").GetProperty("actual");
        var forecast = first.GetProperty("combatPrep").GetProperty("projected");
        var a = double.Parse(finalActual.GetProperty("augmentLevel").GetString(), CultureInfo.InvariantCulture);
        var u = double.Parse(finalActual.GetProperty("upgradeLevel").GetString(), CultureInfo.InvariantCulture);
        // Two native total observations determine only the fixed total's offset and scale,
        // not training rates or missing initial partials. Check the proposed target reconstruction.
        var startActual = first.GetProperty("combatPrep").GetProperty("actual");
        var startA = double.Parse(startActual.GetProperty("augmentLevel").GetString(), CultureInfo.InvariantCulture);
        var startU = double.Parse(startActual.GetProperty("upgradeLevel").GetString(), CultureInfo.InvariantCulture);
        Func<double, double, double> product = (al, ul) => (ul * ul + 1) * Math.Pow(al, 1.4);
        var scale = (finalActual.GetProperty("augmentMultiplier").GetDouble() - startActual.GetProperty("augmentMultiplier").GetDouble()) / (product(a, u) - product(startA, startU));
        var offset = startActual.GetProperty("augmentMultiplier").GetDouble() - scale * product(startA, startU);
        var target = offset + scale * product(166, 333);
        var residual = unique.Max(x => {
            var actual = x.GetProperty("combatPrep").GetProperty("actual");
            var al = double.Parse(actual.GetProperty("augmentLevel").GetString(), CultureInfo.InvariantCulture);
            var ul = double.Parse(actual.GetProperty("upgradeLevel").GetString(), CultureInfo.InvariantCulture);
            return Math.Abs((offset + scale * product(al, ul)) / actual.GetProperty("augmentMultiplier").GetDouble() - 1);
        });
        Console.WriteLine("All observed augment totals fixed-factor formula max relative residual=" + residual.ToString("R"));
        Console.WriteLine("Reconstructed A166/U333 forecast relative residual=" + (target / forecast.GetProperty("augmentMultiplier").GetDouble() - 1).ToString("R"));
        Console.WriteLine("Observed combined shortfall=" + (100 * (finalActual.GetProperty("combinedMultiplier").GetDouble() / forecast.GetProperty("combinedMultiplier").GetDouble() - 1)).ToString("R") + "%");
        Console.WriteLine("No initial partials, capture coefficients, native callback counts or terminal partials: an exact numerical replay is not identifiable from this recording.");
        return 0;
    }

    private static void CheckAugmentFloatTiming()
    {
        // Native advanceAug/advanceUpgrade store float progress after each callback and reset
        // it to zero on completion. The real recording's rates/partials are NOT available.
        // This fixed counterfactual proves that losing <1s can miss one level on BOTH lanes
        // at these levels without any change to targets, coefficients or nominal budget.
        var s = Snapshot(92335174, 0);
        s.Augments = new[] { Tier(4) };
        var tier = s.Augments[0];
        tier.Augment.Level = 148; tier.Upgrade.Level = 298;
        tier.Augment.Progress = .98; tier.Upgrade.Progress = .98;
        tier.Augment.CostCoefficient = tier.Upgrade.CostCoefficient = 0;
        tier.Augment.ProgressCoefficient = .36 / 46167587;
        tier.Upgrade.ProgressCoefficient = 1.44 / 46167587;
        s.CurrentAugmentTotal = 1 + 2d * (298d * 298 + 1) * 148;
        var phase = Phase("Augments", 150); phase.AugmentTierId = 4;
        var full = Project(s, phase);
        phase.DurationSeconds = 149.12;
        var shorter = Project(s, phase);
        Equal(166L, full.Augments[0].AugmentLevel, "150-second counterfactual augment threshold");
        Equal(333L, full.Augments[0].UpgradeLevel, "150-second counterfactual upgrade threshold");
        Equal(165L, shorter.Augments[0].AugmentLevel, "same capture with lost game time misses one augment");
        Equal(332L, shorter.Augments[0].UpgradeLevel, "same capture with lost game time misses one upgrade");
        foreach (var coefficient in new[] { .36, 1.44 })
        {
            var initial = coefficient == .36 ? 148 : 298;
            long nativeLevel = initial;
            float progress = .98f;
            for (var tick = 0; tick < 7500; tick++)
            {
                progress += (float)(coefficient / (nativeLevel + 1));
                if (progress >= 1) { nativeLevel++; progress = 0; }
            }
            Equal(coefficient == .36 ? full.Augments[0].AugmentLevel : full.Augments[0].UpgradeLevel,
                nativeLevel, "native float callback transcription agrees on fixed counterfactual completed levels");
        }
    }
}
