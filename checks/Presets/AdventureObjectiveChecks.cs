using System;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    private static PresetRequest AdventureRequest(int goal)
    {
        var r = Request(); r.Weights = new double[12]; r.Weights[goal] = 1;
        return r;
    }

    static partial void AdventureObjectiveChecks()
    {
        var before = assertions;
        ObjectiveCompatibilityChecks(); LegacyObjectiveArrayChecks(); AdventureGearObjectiveChecks(); AdventureSupportObjectiveChecks(); AdventureActivityObjectiveChecks();
        Console.WriteLine("Direct adventure objective checks: " + (assertions - before));
    }

    private static void ObjectiveCompatibilityChecks()
    {
        Check(LoadoutPresetPlanner.Keys.SequenceEqual(new[] { "energyNgu", "magicNgu", "blood", "pp", "boosts", "zone",
            "timeMachine", "augments", "wandoos", "advancedTraining", "adventurePower", "adventureToughness", "energyCap", "magicCap" }),
            "objective indices 0..9 retain exact meanings; direct adventure stats append at 10/11");
        Check(LoadoutPresetPlanner.Labels.Length == 14 && LoadoutPresetPlanner.Units.Length == 14
            && LoadoutPresetPlanner.Units[10] == "power" && LoadoutPresetPlanner.Units[11] == "toughness",
            "adventure metadata uses instant stat units, never levels per second");
        Check(new PresetSnapshot().Available.Length == 14 && new PresetSnapshot().Reasons.Length == 14
            && new PresetCandidate().Metrics.Length == 14, "default objective arrays include appended instant lanes");
        foreach (var count in new[] { 6, 10, 12, 14 })
        {
            var weights = Enumerable.Range(1, count).Select(i => (double)i).ToArray();
            var normalized = LoadoutPresetPlanner.NormalizeWeights(weights);
            Check(normalized.Length == 14 && normalized.Skip(count).All(x => x == 0), "accepted request dimension pads to fourteen: " + count);
            for (var i = 0; i < count; i++) Near(normalized[i], weights[i] / weights.Sum(), "weight meaning retained " + count + "/" + i);
            Check(weights.SequenceEqual(Enumerable.Range(1, count).Select(i => (double)i)), "normalization preserves request weights: " + count);
        }
        foreach (var weights in new[] { new double[12], new[] { double.NaN, 1d, 0, 0, 0, 0 },
            new[] { double.PositiveInfinity, 1d, 0, 0, 0, 0 }, new[] { -1d, 1, 0, 0, 0, 0 } }
            .Concat(new[] { 7, 9, 11, 13 }.Select(count => Enumerable.Repeat(1d, count).ToArray())))
        {
            var rejected = false;
            try { LoadoutPresetPlanner.NormalizeWeights(weights); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid objective dimensions or weights remain rejected");
        }
        var s = Snapshot(); var r = Request();
        var legacy = LoadoutPresetPlanner.Plan(s, r);
        foreach (var count in new[] { 10, 12, 14 })
        {
            r.Weights = new double[count]; r.Weights[0] = r.Weights[1] = 1;
            var extended = LoadoutPresetPlanner.Plan(s, r);
            Check(extended.Candidates[1].Items.SequenceEqual(legacy.Candidates[1].Items), "legacy padding retains selected gear: " + count);
            Near(extended.Candidates[1].Score.Value, legacy.Candidates[1].Score.Value, "legacy padding retains score: " + count);
            Check(extended.Normalization.SequenceEqual(legacy.Normalization) && extended.ReferenceProduction.SequenceEqual(legacy.ReferenceProduction),
                "legacy padding retains frozen references: " + count);
            Check(extended.Sequence != null, "legacy sequence remains supported: " + count);
        }
        r = ActivityRequest(9);
        r.Weights = r.Weights.Take(10).ToArray();
        var activity = Evaluate(ActivitySnapshot(), r);
        var paddedRequest = r.Copy(); paddedRequest.Weights = r.Weights.Concat(new double[2]).ToArray();
        var paddedActivity = Evaluate(ActivitySnapshot(), paddedRequest);
        Near(activity.Metrics[9].Value, paddedActivity.Metrics[9].Value, "ten-weight AT retains production semantics after padding");
        Check(activity.Activities.AT.SequenceEqual(paddedActivity.Activities.AT), "ten-weight AT retains allocations after padding");
    }

    private static void LegacyObjectiveArrayChecks()
    {
        var s = Snapshot(); var r = Request(); r.PlannedSupport = true;
        s.DiggerSlots = 1;
        s.Diggers = new[] { new SupportDigger { Id = 4, Maximum = 2, BaseDrain = 1, Growth = 2, ConsumesGold = true, PerLevel = .5 } };
        var weights = LoadoutPresetPlanner.NormalizeWeights(r.Weights);
        var full = LoadoutPresetPlanner.Evaluate(s, r, s.Current, weights, Enumerable.Repeat(1d, 12).ToArray(), "full", "Full");
        foreach (var count in new[] { 6, 10, 12 })
        {
            var available = new bool[count]; Array.Copy(s.Available, available, s.Available.Length);
            s.Available = available; s.Reasons = new string[count]; s.Reasons[5] = "native farming unavailable";
            var shortened = LoadoutPresetPlanner.Evaluate(s, r, s.Current, weights.Take(count).ToArray(), Enumerable.Repeat(1d, count).ToArray(), "short", "Short");
            Near(shortened.Score.Value, full.Score.Value, "short weight/normalization arrays support planned-support evaluation " + count);
            Check(shortened.Support.Diggers.Single().Level == full.Support.Diggers.Single().Level,
                "short weights preserve the actual planned digger selection " + count);
            Check(shortened.Metrics.Length == 14 && shortened.RawMetrics.Length == 14 && shortened.Metrics.Skip(count).All(x => !x.HasValue),
                "legacy availability produces fourteen metrics with unknown appended lanes, not fabricated zeros " + count);
            Check(s.Available.Length == count && s.Reasons.Length == count && s.Reasons[5] == "native farming unavailable",
                "evaluation does not resize or overwrite caller-owned legacy snapshot arrays " + count);
        }
        s = Snapshot(); r = Request(); var original = LoadoutPresetPlanner.Plan(s, r);
        foreach (var count in new[] { 6, 10, 12 })
        {
            Func<PresetCandidate, PresetCandidate> shorten = candidate => {
                var copy = candidate.Copy(); copy.Metrics = candidate.Metrics.Take(count).ToArray(); copy.RawMetrics = candidate.RawMetrics.Take(count).ToArray();
                return copy;
            };
            var cached = new PresetPlan { Normalization = original.Normalization.Take(count).ToArray(), ReferenceKeys = original.ReferenceKeys.Take(count).ToArray(),
                ReferenceProduction = original.ReferenceProduction.Take(count).ToArray(), Candidates = original.Candidates.Select(shorten).ToArray(),
                Specialists = original.Specialists.Select(shorten).ToArray() };
            var replay = LoadoutPresetPlanner.Replay(s, r, cached);
            Check(replay.Normalization.Length == 14 && replay.ReferenceKeys.Length == 14 && replay.ReferenceProduction.Length == 14,
                "legacy cached replay expands every objective metadata array to fourteen " + count);
            Check(replay.Normalization.Take(count).SequenceEqual(cached.Normalization) && replay.Normalization.Skip(count).All(x => x == 1)
                && replay.ReferenceKeys.Skip(count).All(x => x == null) && replay.ReferenceProduction.Skip(count).All(x => !x.HasValue),
                "replay preserves original denominators and leaves missing references unknown " + count);
            Near(replay.Candidates[1].Score.Value, original.Candidates[1].Score.Value, "legacy replay padding does not renormalize the score " + count);
            var phase = LoadoutPresetPlanner.ProjectPhase(s.ProgressionCopy(), cached.Candidates[0], 25);
            Check(phase.Length == 14 && phase.Skip(count).All(x => !x.HasValue) && cached.Candidates[0].RawMetrics.Length == count,
                "legacy phase metrics pad unknown lanes without mutating the cached policy " + count);
            var unavailableRequest = AdventureRequest(10);
            Check(!Evaluate(s, unavailableRequest).Score.HasValue && !Evaluate(s, unavailableRequest).Metrics[10].HasValue,
                "legacy snapshots without adventure parity cannot acquire a new immediate metric " + count);
        }
    }

    private static void AdventureGearObjectiveChecks()
    {
        var s = Snapshot(); s.Energy = s.Magic = 0; s.Available = new bool[12]; s.Available[10] = s.Available[11] = true;
        s.BasePower = s.BaseToughness = 100; s.CubePower = s.CubeToughness = 10000;
        s.PowerFactors[0] = 1.25; s.ToughnessFactors[0] = 1.5;
        var offhand = new PresetItem { Id = 100, SourceSlot = -6, Group = -5 };
        var fighters = Enumerable.Range(0, 4).Select(i => new PresetItem { Id = 200 + i, SourceSlot = 100 + i, Group = -5 }).ToArray();
        fighters[0].Stats[0] = 5000; fighters[0].Stats[1] = 200;
        fighters[1].Stats[0] = 2500; fighters[1].Stats[1] = 300;
        fighters[2].Stats[0] = 100; fighters[2].Stats[1] = 8000;
        fighters[3].Stats[0] = 200; fighters[3].Stats[1] = 4000;
        // Enough misleading resource/AT gear to require objective-aware shortlist seeding.
        var distractors = Enumerable.Range(0, 20).Select(i => {
            var item = new PresetItem { Id = 300 + i, SourceSlot = 200 + i, Group = -5 };
            Stat(item, PresetStat.EnergyPower, 1e6 + i); Stat(item, PresetStat.MagicPower, 1e6 + i);
            Stat(item, PresetStat.AdvancedTrainingSpeed, 1e6 + i); return item;
        }).ToArray();
        s.Current = new[] { s.Current[0], offhand }; s.Slots = new[] { -5, -6 }; s.Factors = new[] { 1d, .25 };
        s.Items = s.Current.Concat(distractors).Concat(fighters).ToArray();
        foreach (var goal in new[] { 10, 11 })
        {
            var r = AdventureRequest(goal); r.Horizon = 1; r.Swap = 3600;
            var plan = LoadoutPresetPlanner.Plan(s, r); var best = plan.Candidates[1];
            var first = goal == 10 ? 0 : 2;
            Check(best.Feasible && best.Score > 0 && best.Items.SequenceEqual(new[] { fighters[first], fighters[first + 1] }),
                "direct adventure objective selects native-stat gear and strongest primary without E/M: " + goal);
            Check(best.Activities == null && best.Energy == 0 && best.MagicNgu == 0 && !best.Metrics[9].HasValue,
                "direct stats do not allocate AT or masquerade as AT level rate: " + goal);
            var gear = LoadoutPresetPlanner.Totals(s, best.Items);
            Near(best.Metrics[10].Value, LoadoutPresetPlanner.AdventureStat(100, 10000, gear[0], s.PowerFactors, 1), "power objective uses cube and native factor projection");
            Near(best.Metrics[11].Value, LoadoutPresetPlanner.AdventureStat(100, 10000, gear[1], s.ToughnessFactors, 1), "toughness objective uses its independent cube/factors");
            Check(best.Metrics[goal] == best.RawMetrics[goal] && plan.Sequence == null,
                "instant stat is not swap-uptime scaled and cannot become time-shared production: " + goal);
            Check(plan.Specialists[goal].Items.SequenceEqual(best.Items) && plan.ReferenceProduction[goal] == best.Metrics[goal],
                "direct objective has a genuine specialist and frozen reference: " + goal);
            var phase = LoadoutPresetPlanner.ProjectPhase(s.ProgressionCopy(), best, 25);
            Near(phase[goal].Value, best.Metrics[goal].Value, "phase projection never multiplies instant stats by seconds");
            r.Horizon = 604800; r.Swap = 0;
            var longer = LoadoutPresetPlanner.Plan(s, r);
            Check(longer.Candidates[1].Items.SequenceEqual(best.Items), "horizon does not change direct-stat gear: " + goal);
            Near(longer.Candidates[1].Metrics[goal].Value, best.Metrics[goal].Value, "horizon does not change direct-stat value");
            Near(longer.Candidates[1].Score.Value, best.Score.Value, "horizon does not change direct-stat normalized score");
            var replay = LoadoutPresetPlanner.Replay(s, r, plan).Candidates[1];
            Check(replay.Items.SequenceEqual(best.Items) && replay.Metrics[goal] == best.Metrics[goal], "direct-stat replay retains gear and native projection");
            s.Available[goal] = false; s.Reasons[goal] = "adventure parity unavailable";
            var unavailable = Evaluate(s, r);
            Check(!unavailable.Metrics[goal].HasValue && !unavailable.Score.HasValue, "positive unavailable direct stat cannot silently disappear");
            s.Available[goal] = true;
        }
        s = Snapshot(); s.Energy = s.Magic = 0; s.BeastMode = true;
        s.Available = new bool[12]; s.Available[10] = s.Available[11] = true;
        var instant = AdventureRequest(10); instant.Weights[11] = 1; instant.Zone = 43;
        var stats = Evaluate(s, instant);
        Check(stats.Feasible && stats.Score.HasValue && stats.Zone == -1 && stats.Metrics[10] == 1000 && stats.Metrics[11] == 1000,
            "only indices 3..5 require a farming route: Beast Mode and an unavailable selected zone do not block direct stats");
        instant.Weights[4] = 1;
        Check(!Evaluate(s, instant).Feasible, "adding the actual boost-farming lane restores farming safety constraints");
    }

    private static void AdventureSupportObjectiveChecks()
    {
        foreach (var goal in new[] { 10, 11 })
        {
            var s = Snapshot(); s.Items = s.Current; s.Energy = s.Magic = 0;
            s.Available = new bool[12]; s.Available[10] = s.Available[11] = true;
            s.UnlockedBeards = new[] { 5 }; s.BeardSlots = 1;
            s.BeardTemporary[2] = 3; s.BeardPermanent[2] = 2;
            s.PowerFactors[3] = s.ToughnessFactors[3] = 2;
            s.DiggerSlots = 1; s.Diggers = new[] { new SupportDigger {
                Id = 3, Maximum = 4, BaseDrain = 100, Growth = 2, ConsumesGold = true, PerLevel = .5 } };
            var r = AdventureRequest(goal); r.PlannedSupport = true; r.Horizon = 1; r.Swap = 3600;
            var planned = Evaluate(s, r);
            Check(planned.Support.Beards.SequenceEqual(new[] { 5 }) && planned.Support.Diggers.Single().Id == 3
                && planned.Support.Diggers.Single().Level == 3 && planned.Support.DiggerWeights[3] > 0,
                "automatic support funds Adventure beard/digger for direct stat with no farming or constraints: " + goal);
            Near(planned.Metrics[goal].Value, 15000, "native support changes replace captured adventure factors once");
            Check(planned.Support.Drain == 400 && planned.Support.Drain <= planned.Support.Budget && planned.Feasible,
                "instant-stat support still respects sustainable gold budget");
            var plan = LoadoutPresetPlanner.Plan(s, r);
            Near(plan.Candidates[1].Metrics[goal].Value, planned.Metrics[goal].Value, "whole direct-stat plan retains the best affordable support");
            var replay = LoadoutPresetPlanner.Replay(s, r, plan).Candidates[1];
            Check(replay.Support.Beards.SequenceEqual(planned.Support.Beards)
                && replay.Support.Diggers.Single().Level == planned.Support.Diggers.Single().Level
                && replay.Metrics[goal] == planned.Metrics[goal], "whole direct-stat replay retains exact support and instant value");
            r.PlannedSupport = false;
            var current = Evaluate(s, r);
            Check(current.Support.Beards.Length == 0 && current.Support.Diggers.Length == 0, "current support never searches new activation");
            Near(current.Metrics[goal].Value, 2000, "current support keeps captured permanent adventure bonus");
            r.PlannedSupport = true; r.DiggerWeights = new double[12];
            Check(Evaluate(s, r).Support.Diggers.Length == 0, "explicit zero digger preferences still exclude direct-stat support");
        }
    }

    private static void AdventureActivityObjectiveChecks()
    {
        var s = ActivitySnapshot(); s.Available[10] = s.Available[11] = true;
        s.Current[0].Stats[0] = 200; s.Current[0].Stats[1] = 300;
        var r = AdventureRequest(10); r.Weights[9] = r.Weights[11] = 1;
        var mixed = Evaluate(s, r);
        Check(mixed.Feasible && mixed.Activities.AT.Sum() > 0 && mixed.Metrics[9] > 0, "mixed direct-stat and AT request retains actual AT production");
        Near(mixed.Metrics[10].Value, 1200, "activity evaluation preserves direct power instead of clearing it as an unmodeled activity");
        Near(mixed.Metrics[11].Value, 1300, "activity evaluation preserves direct toughness instead of clearing it as an unmodeled activity");
        var pinned = LoadoutPresetPlanner.Evaluate(s, r, mixed.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights),
            Enumerable.Repeat(1d, 12).ToArray(), "pinned", "Pinned", mixed);
        Check(pinned.Metrics.SequenceEqual(mixed.Metrics) && pinned.Activities.AT.SequenceEqual(mixed.Activities.AT), "mixed activity/direct-stat replay preserves both kinds of metric");
        var changed = new PresetItem { Id = 500, SourceSlot = 500, Group = -5, Stats = s.Current[0].Stats.ToArray() };
        r.Horizon = 100; r.Swap = 10;
        var swapped = LoadoutPresetPlanner.Evaluate(s, r, new[] { changed }, LoadoutPresetPlanner.NormalizeWeights(r.Weights),
            Enumerable.Repeat(1d, 12).ToArray(), "swap", "Swap");
        Check(swapped.SetupChanged && swapped.Metrics[9] > 0, "mixed activity/direct-stat fixture incurs actual setup downtime");
        Near(swapped.Metrics[9].Value, swapped.RawMetrics[9].Value * .9, "AT remains a production metric scaled by setup uptime");
        Near(swapped.Metrics[10].Value, mixed.Metrics[10].Value, "mixed power does not inherit AT downtime scaling");
        Near(swapped.Metrics[11].Value, mixed.Metrics[11].Value, "mixed toughness does not inherit AT downtime scaling");
        r = Request(); var plan = LoadoutPresetPlanner.Plan(s, r);
        Check(plan.Sequence != null, "unweighted direct stats do not disable legacy NGU sequences");
        var last = plan.Candidates.Concat(plan.Specialists).Single(x => x.Key == plan.Sequence.Second);
        Near(plan.Sequence.Metrics[10].Value, last.RawMetrics[10].Value, "legacy sequence reports final power, never summed or horizon-divided power");
        Near(plan.Sequence.Metrics[11].Value, last.RawMetrics[11].Value, "legacy sequence reports final toughness, never summed or horizon-divided toughness");
    }
}
