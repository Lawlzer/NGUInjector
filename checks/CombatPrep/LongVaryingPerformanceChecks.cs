using System;
using System.Diagnostics;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    private static void CheckLongVaryingPerformance()
    {
        var s = Snapshot(1000, 1000);
        s.FreeAdvancedTraining = true;
        s.WandoosBootupDurationSeconds = 3600;
        s.AtEnergy.LevelFactor = s.AtMagic.LevelFactor = .05;
        s.Current.WandoosEnergySpeedWithoutAt = s.Current.WandoosMagicSpeedWithoutAt = 1000;
        s.Augments = Enumerable.Range(0, 7).Select(i => {
            var tier = Tier(i); tier.Augment.ProgressCoefficient = tier.Upgrade.ProgressCoefficient = 1e-8;
            tier.Augment.CostCoefficient = tier.Upgrade.CostCoefficient = 0;
            return tier;
        }).ToArray();
        s.OperatingSystems = Enumerable.Range(0, 3).Select(i => {
            var os = Os(i, "normal"); os.EnergyBaseTime = os.MagicBaseTime = Math.Pow(1000, i) * 1e9;
            return os;
        }).ToArray();
        s.Profiles = new[] { "Augments", "AT", "Wandoos", "NGU" }.SelectMany(goal => Enumerable.Range(0, 2).Select(i => {
            var p = s.Current.Copy(); p.Key = goal + i; p.Goal = goal;
            p.OwnedGearRefs = new[] { "owned-" + goal + i };
            return p;
        })).ToArray();
        // OS 1 and 2 stay under one bar/tick for the ENTIRE horizon. Unlike the earlier
        // benchmark, their positive free-AT-varying rates cannot use saturated batching.
        var finalAtFactor = 1 + .05 * 4320000;
        True(1000d * 1000 * finalAtFactor / 1e12 < 1, "24-hour middle OS remains unsaturated");
        True(1000d * 1000 * finalAtFactor / 1e15 < 1, "24-hour slow OS remains unsaturated");
        var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
        var result = CombatPrepPlanner.Plan(s, 86400);
        watch.Stop(); var milliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        True(result.Success, "24-hour unsaturated varying search succeeds: " + result.Error);
        True(result.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates && result.EvaluatedCandidates >= 500, "full free-AT candidate shape stays bounded");
        Equal(86400d, result.DurationSeconds, "worst-case benchmark retains exact original horizon");
        True(result.Alternatives.All(x => x.Phases.All(p => p.Kind != "AT")), "free-AT benchmark cannot fabricate paid AT phases");
        foreach (var candidate in result.Alternatives.Where(x => x.Phases.Length > 0))
        {
            Near(86400, candidate.Phases.Sum(x => x.DurationSeconds), "varying benchmark preserves phase budget");
            True(candidate.EnergyResourceSeconds <= 86400000 && candidate.MagicResourceSeconds <= 86400000, "varying benchmark resource cost is bounded by real pools");
        }
        Console.WriteLine("24-hour unsaturated free-AT/native-boot/7-tier/3-OS/9-profile: " + watch.ElapsedMilliseconds + "ms wall, " + milliseconds.ToString("0") + "ms CPU, " + result.EvaluatedCandidates + " policies.");
        True(milliseconds < 24000, "unsaturated varying search must meet the existing 24-hour 24000ms process-CPU bound; measured " + milliseconds.ToString("0"));
    }
}
