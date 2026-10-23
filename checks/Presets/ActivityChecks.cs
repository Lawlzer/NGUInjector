using System;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    private static PresetActivityBar ActivityBar(double coefficient = .001, double level = 10)
    { return new PresetActivityBar { Unlocked = true, Level = level, Coefficient = coefficient }; }

    private static PresetSnapshot ActivitySnapshot()
    {
        var s = Snapshot(); s.Items = s.Current.ToArray();
        s.Available = new[] { true, true, true, false, false, false, true, true, true, true, false, false };
        s.Gold = 1e12;
        s.EnergyNgu[0].Level = s.MagicNgu[0].Level = 10;
        s.TimeMachineEnergy = ActivityBar(); s.TimeMachineMagic = ActivityBar();
        s.WandoosEnergy = ActivityBar(.00001); s.WandoosEnergy.LevelExponent = 0; s.WandoosEnergy.PowerExponent = 0;
        s.WandoosMagic = ActivityBar(.00001); s.WandoosMagic.LevelExponent = 0; s.WandoosMagic.PowerExponent = 0;
        s.WandoosOs = 2;
        s.AdvancedTraining = Enumerable.Range(0, 5).Select(_ => ActivityBar()).ToArray();
        foreach (var bar in s.AdvancedTraining) bar.PowerExponent = .5;
        s.Augments = new[] { new PresetAugment { Id = 4, BaseBoost = 1, Augment = ActivityBar(), Upgrade = ActivityBar() },
            new PresetAugment { Id = 6, BaseBoost = 2, Augment = ActivityBar(), Upgrade = ActivityBar() } };
        foreach (var tier in s.Augments)
        { tier.Augment.CostCoefficient = tier.Upgrade.CostCoefficient = .01; tier.Upgrade.CostExponent = 2; }
        return s;
    }

    private static PresetRequest ActivityRequest(params int[] goals)
    {
        var r = Request(); r.Weights = new double[LoadoutPresetPlanner.Keys.Length];
        foreach (var goal in goals) r.Weights[goal] = 1;
        return r;
    }

    static partial void ActivityChecks()
    {
        var before = assertions;
        SetupOnlyChecks();
        var old = Snapshot(); var oldRequest = Request();
        var padded = LoadoutPresetPlanner.NormalizeWeights(oldRequest.Weights);
        Check(padded.Length == 14 && padded.Skip(6).All(x => x == 0), "old six weights pad to fourteen without changing values");
        var oldResult = Evaluate(old, oldRequest);
        Check(oldResult.Metrics.Length == 14 && oldResult.Metrics.Skip(6).All(x => !x.HasValue), "old six availability pads unavailable, not invented zero gains");
        Check(old.Available.Length == 6 && oldRequest.Weights.Length == 6, "numeric compatibility never mutates caller arrays");
        var s = ActivitySnapshot();
        foreach (var goal in new[] { 6, 7, 8, 9 })
        {
            var c = Evaluate(s, ActivityRequest(goal));
            Check(c.Feasible && c.Score > 0 && c.Metrics[goal] > 0, "one-hot extra activity produces genuine gains " + goal);
            Check(c.Energy + c.Activities.EnergyTotal <= c.EnergyPool && c.MagicNgu + c.Blood + c.Activities.MagicTotal <= c.MagicPool,
                "one-hot energy and magic conserved " + goal);
            Check(c.Activities.Os == 2 && (goal != 7 || c.Activities.AugmentId == 6), "installed OS and native AUG tier index retained " + goal);
            if (goal == 9) Check(c.Activities.AT[0] > 0 && c.Activities.AT[1] > 0 && c.Activities.AT.Skip(2).All(x => x == 0), "AT default means Adventure, not Wandoos");
        }
        var r = ActivityRequest(0, 9); s.Magic = 0;
        var mixed = Evaluate(s, r);
        Check(mixed.Metrics[0] > 0 && mixed.Metrics[9] > 0 && mixed.Energy > 0 && mixed.Activities.AT.Sum() > 0,
            "actual weighted mix funds NGU and AT concurrently, not winning-weight dispatch");
        r.Weights[0] = 9; var nguHeavy = Evaluate(s, r);
        r.Weights[0] = 1; r.Weights[9] = 9; var atHeavy = Evaluate(s, r);
        Check(nguHeavy.Energy > atHeavy.Energy && nguHeavy.Activities.AT.Sum() < atHeavy.Activities.AT.Sum(), "differentiated weights change real conserved allocations");
        s = ActivitySnapshot(); s.RequiredATTargets = new long[] { 11, 11 }; r = ActivityRequest(0);
        var excludedMandatory = Evaluate(s, r);
        Check(!excludedMandatory.Feasible && !excludedMandatory.Score.HasValue && excludedMandatory.Warnings.Any(x => x.Contains("Mandatory stage AT")),
            "mandatory completion rejects a pure NGU policy instead of applying zero AT forever");
        r = ActivityRequest(0, 9); r.Weights[0] = 100;
        var mandatoryMix = Evaluate(s, r);
        Check(mandatoryMix.Feasible && mandatoryMix.Activities.AT[0] > 0 && mandatoryMix.Activities.AT[1] > 0,
            "weighted search cannot sacrifice unfinished mandatory stat lanes to a larger NGU weight");
        s.Energy = 1;
        Check(!Evaluate(s, r).Feasible, "one unit of energy cannot masquerade as funded mandatory training on two lanes");
        s = ActivitySnapshot(); s.RequiredATTargets = new long[] { 10, 10 };
        s.AdvancedTraining[0].Unlocked = s.AdvancedTraining[1].Unlocked = false;
        r = ActivityRequest(9); r.ATWeights = new int[5];
        var completeMandatory = Evaluate(s, r);
        Check(completeMandatory.Feasible && completeMandatory.Score == 0 && completeMandatory.Metrics[9] == 0
            && completeMandatory.Activities.AT.All(x => x == 0), "completed mandatory stage is known-zero without requiring excluded completed rows");
        s = ActivitySnapshot();
        r = ActivityRequest(9); r.ATWeights = new[] { 0, 0, 0, 1, 3 };
        var atW = Evaluate(s, r);
        Check(atW.Activities.AT.Take(3).All(x => x == 0) && atW.Activities.AT[4] > atW.Activities.AT[3] && atW.Metrics[9] > 0,
            "AT Wandoos targets use exact indices 3/4, both consume energy");
        r = ActivityRequest(6); s.Available[6] = false; s.Reasons = new string[LoadoutPresetPlanner.Keys.Length]; s.Reasons[6] = "parity refused";
        var unavailable = Evaluate(s, r);
        Check(!unavailable.Feasible && !unavailable.Score.HasValue && !unavailable.Metrics[6].HasValue
            && unavailable.Warnings.Any(x => x.Contains("parity refused")), "positive unavailable extra lane rejected with native reason");
        s = ActivitySnapshot(); r = ActivityRequest(0, 1, 2, 6, 7, 8, 9);
        var allPlan = LoadoutPresetPlanner.Plan(s, r);
        var all = allPlan.Candidates[1];
        Check(allPlan.Specialists.Length == 14 && allPlan.Normalization.Length == 14 && allPlan.ReferenceProduction.Length == 14,
            "production plan includes twelve specialist references and frozen normalization dimensions");
        Check(all.Feasible && all.Metrics.Where((_, i) => r.Weights[i] > 0).All(x => x.HasValue)
            && all.Metrics.Where((x, i) => r.Weights[i] > 0 && x > 0).Count() >= 3,
            "seven-goal mix is supported with real concurrent production, not mandatory funding of dominated lanes: "
            + all.Feasible + "; " + string.Join(",", all.Metrics.Select(x => x.ToString()).ToArray()) + "; " + string.Join(";", all.Warnings));
        Check(all.Energy + all.Activities.EnergyTotal <= all.EnergyPool && all.MagicNgu + all.Blood + all.Activities.MagicTotal <= all.MagicPool,
            "all activities share exactly one E pool and one M pool");
        Check(all.ActivityGoldSpent > 0 && all.ActivityGoldSpent <= s.Gold && all.Support.Remaining >= 0, "TM/AUG liquid gold and blood/digger recurring gold are feasible together");
        var allReplay = LoadoutPresetPlanner.Evaluate(s, r, all.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), allPlan.Normalization, "all", "All", all);
        Check(allReplay.Activities.TimeMachineEnergy == all.Activities.TimeMachineEnergy && allReplay.Activities.TimeMachineMagic == all.Activities.TimeMachineMagic
            && allReplay.Activities.WandoosEnergy == all.Activities.WandoosEnergy && allReplay.Activities.WandoosMagic == all.Activities.WandoosMagic
            && allReplay.Activities.AT.SequenceEqual(all.Activities.AT), "mixed replay preserves TM/W/AT allocations exactly");
        var replayPlan = LoadoutPresetPlanner.Replay(s, r, allPlan);
        Check(replayPlan.Specialists.Length == 14 && replayPlan.Candidates[1].Activities.AugmentId == all.Activities.AugmentId,
            "whole twelve-goal plan replay retains exact mixed policy and every specialist");
        Check(replayPlan.Specialists.Take(6).All(x => x.Activities != null && x.Activities.EnergyTotal == 0 && x.Activities.MagicTotal == 0),
            "legacy-goal specialist replay keeps zero extra allocations instead of searching new activity lanes");
        Check(allPlan.Specialists.Take(3).All(x => x.Metrics.Skip(6).Take(4).All(metric => metric == 0) && x.Score.HasValue),
            "old specialist lanes expose certified zero extra production, not false unavailability under mixed scoring");
        Check(LoadoutPresetPlanner.Sequence(s, r, new[] { all, all.Copy() }, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, LoadoutPresetPlanner.Keys.Length).ToArray()) == null,
            "stateful extra sequence rejected instead of extrapolating frozen rates");
        var phaseState = s.ProgressionCopy();
        Check(LoadoutPresetPlanner.ProjectPhase(phaseState, all, 100) == null && phaseState.Energy == s.Energy && phaseState.Magic == s.Magic,
            "legacy phase projector rejects fixed extra allocations before mutating state or double-counting NGU resources");
        var legacyWithExtras = LoadoutPresetPlanner.Plan(s, Request());
        Check(legacyWithExtras.Sequence != null, "six-goal sequence remains available when extra activities are unlocked");
        var phases = legacyWithExtras.Candidates.Concat(legacyWithExtras.Specialists)
            .Where(x => x.Key == legacyWithExtras.Sequence.First || x.Key == legacyWithExtras.Sequence.Second);
        Check(phases.All(x => x.Activities == null || x.Activities.EnergyTotal == 0 && x.Activities.MagicTotal == 0),
            "six-goal sequence excludes extra-funded specialists even when their native activities are available");
        s = ActivitySnapshot(); r = ActivityRequest(7); var pinned = Evaluate(s, r);
        s.Augments[0].BaseBoost = 1e12;
        Check(Evaluate(s, r).Activities.AugmentId == 4, "fresh AUG search would choose a newly better different tier");
        var replay = LoadoutPresetPlanner.Evaluate(s, r, pinned.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, LoadoutPresetPlanner.Keys.Length).ToArray(), "pinned", "Pinned", pinned);
        Check(replay.Activities.AugmentId == pinned.Activities.AugmentId && replay.Activities.AugmentEnergy == pinned.Activities.AugmentEnergy
            && replay.Activities.UpgradeEnergy == pinned.Activities.UpgradeEnergy && replay.Activities.AT.SequenceEqual(pinned.Activities.AT), "replay preserves exact AUG tier and all resource amounts without search");
        s.Gold = 0;
        replay = LoadoutPresetPlanner.Evaluate(s, r, pinned.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, LoadoutPresetPlanner.Keys.Length).ToArray(), "pinned", "Pinned", pinned);
        Check(!replay.Feasible && !replay.Score.HasValue, "replay rejects lost AUG gold instead of resizing or choosing another tier");
        s = ActivitySnapshot(); r = ActivityRequest(6, 7);
        foreach (var bar in new[] { s.TimeMachineEnergy, s.TimeMachineMagic }) bar.CostCoefficient = 1000;
        var goldPolicy = Evaluate(s, r); s.Gold = goldPolicy.ActivityGoldSpent - 1;
        var goldReplay = LoadoutPresetPlanner.Evaluate(s, r, goldPolicy.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, LoadoutPresetPlanner.Keys.Length).ToArray(), "gold", "Gold", goldPolicy);
        Check(!goldReplay.Feasible, "TM and AUG reserve one shared liquid gold pool, never spend it twice");
        s = ActivitySnapshot(); r = ActivityRequest(6); s.TimeMachineEnergy.Unlocked = false;
        var magicOnlyTm = Evaluate(s, r);
        Check(magicOnlyTm.Feasible && magicOnlyTm.Metrics[6] > 0 && magicOnlyTm.Activities.TimeMachineEnergy == 0
            && magicOnlyTm.Activities.TimeMachineMagic > 0, "disabled TM energy target does not block supported magic-only TM");
        s = ActivitySnapshot(); r = ActivityRequest(7); s.Augments = new PresetAugment[0];
        Check(!Evaluate(s, r).Score.HasValue, "missing native AUG model cannot be disguised as known zero production");
        s = ActivitySnapshot(); r = ActivityRequest(8); var baseW = Evaluate(s, r);
        Stat(s.Current[0], PresetStat.EnergyPower, 300);
        Near(Evaluate(s, r).Metrics[8].Value, baseW.Metrics[8].Value, "Wandoos progress does not invent energy-power scaling");
        s = ActivitySnapshot(); r = ActivityRequest(9); var baseAT = Evaluate(s, r);
        var speedItem = new PresetItem { Id = 500, SourceSlot = 500, Group = -5 };
        speedItem.Stats[(int)PresetStat.AdvancedTrainingSpeed] = 300;
        var fasterAT = LoadoutPresetPlanner.Evaluate(s, r, new[] { speedItem }, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, LoadoutPresetPlanner.Keys.Length).ToArray(), "speed", "Speed");
        Check(fasterAT.Metrics[9] > baseAT.Metrics[9], "AT candidate equipment speed changes real bounded production");
        s = ActivitySnapshot(); r = ActivityRequest(8); var wPolicy = Evaluate(s, r); s.WandoosOs = 0;
        Check(!LoadoutPresetPlanner.Evaluate(s, r, wPolicy.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, LoadoutPresetPlanner.Keys.Length).ToArray(), "w", "W", wPolicy).Feasible,
            "replay refuses changed installed OS instead of switching it");
        s.WandoosOs = 2; s.Energy = 1;
        Check(!LoadoutPresetPlanner.Evaluate(s, r, wPolicy.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, LoadoutPresetPlanner.Keys.Length).ToArray(), "w", "W", wPolicy).Feasible,
            "replay rejects smaller pools rather than resizing fixed extra allocations");
        r = ActivityRequest(9); r.ATWeights = new int[5];
        Check(!Evaluate(s, r).Score.HasValue, "positive AT with no eligible target weights is unavailable, not silently ignored");
        foreach (var exponent in new[] { 0d, 1 })
        foreach (var numerator in new[] { .01, .3, 1, 17, 250, 10000 })
        {
            var bar = ActivityBar(numerator / 100, 10); bar.LevelExponent = exponent; bar.PowerExponent = 0;
            var actual = LoadoutPresetPlanner.ActivityGain(bar, 100, 1, 100, 1);
            var level = bar.Level; var progress = 0f;
            for (var tick = 0; tick < 5000; tick++)
            {
                progress += (float)(numerator * LoadoutPresetPlanner.ActivityRateSafetyFactor / Math.Pow(level + 1, exponent));
                if (progress >= 1) { level++; progress = 0; }
            }
            var expected = level - bar.Level;
            Check(actual.HasValue && actual <= expected && expected - actual <= expected * LoadoutPresetPlanner.NguRelativeError,
                "bounded extra activity forecast agrees with independent float tick oracle " + exponent + "/" + numerator);
        }
        var boundedTimer = System.Diagnostics.Stopwatch.StartNew();
        var maximumGain = LoadoutPresetPlanner.ActivityGain(ActivityBar(1e20), 1000, 1000, 604800, 1);
        Check(maximumGain == 30240000 && boundedTimer.ElapsedMilliseconds < 1000, "seven-day extra forecast jumps saturated ticks without per-tick simulation");
        s = ActivitySnapshot(); r = ActivityRequest(0, 9); r.Horizon = 604800;
        s.EnergyNgu = Enumerable.Range(0, 9).Select(_ => new PresetNgu { Level = 10000, HardCap = 1000000000, Weight = 1, Coefficient = .001 }).ToArray();
        s.MagicNgu = Enumerable.Range(0, 7).Select(_ => new PresetNgu { Level = 10000, HardCap = 1000000000, Weight = 1, Coefficient = .001 }).ToArray();
        boundedTimer.Restart(); var boundedMix = LoadoutPresetPlanner.Plan(s, r); boundedTimer.Stop();
        Check(boundedTimer.ElapsedMilliseconds < 5000 && boundedMix.Evaluated <= MaximumPlanEvaluations, "seven-day weighted NGU/AT plan retains bounded work");
        Console.WriteLine("Maximum-horizon weighted activities: " + boundedMix.Evaluated + " evaluations in " + boundedTimer.ElapsedMilliseconds + " ms");
        Console.WriteLine("Extra weighted activity checks: " + (assertions - before));
    }
}
