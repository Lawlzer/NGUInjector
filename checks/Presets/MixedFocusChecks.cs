using System;
using System.Diagnostics;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    private static void MixedFocusChecks()
    {
        ProductionSupportChecks(); AccessorySacrificeChecks(); FloorComparisonChecks(); BudgetSeedFallbackChecks(); ShortMagicHorizonChecks(); ZeroReferenceChecks();
    }

    private static void ProductionSupportChecks()
    {
        var s = Snapshot(); var r = Request(); r.PlannedSupport = true; r.Horizon = 10000;
        s.Items = s.Current; s.DiggerSlots = 1;
        s.Diggers = new[] {
            new SupportDigger { Id = 4, Name = "Weak E", Maximum = 1, PerLevel = .01 },
            new SupportDigger { Id = 5, Name = "Strong M", Maximum = 1, PerLevel = 10 } };
        var plan = LoadoutPresetPlanner.Plan(s, r); var mixed = plan.Candidates[1];
        Check(mixed.Support.Diggers.Single().Id == 5, "actual retained production replaces equal-weight weak E multiplier with strong M support");
        var selectedStyle = new double[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        Check(mixed.Support.DiggerWeights.SequenceEqual(selectedStyle), "auto weights expose the actual winning single-purpose style rather than the initial auto portfolio");
        var automaticReplay = LoadoutPresetPlanner.Replay(s, r, plan);
        foreach (var policy in plan.Candidates.Concat(plan.Specialists))
        {
            var replayed = automaticReplay.Candidates.Concat(automaticReplay.Specialists).Single(c => c.Key == policy.Key);
            Check(replayed.Support.DiggerWeights.SequenceEqual(policy.Support.DiggerWeights), "automatic replay preserves every role's chosen raw style");
        }
        Check(mixed.Copy().Support.DiggerWeights.SequenceEqual(selectedStyle), "candidate copies retain the chosen style");
        var w = LoadoutPresetPlanner.NormalizeWeights(r.Weights);
        Near(mixed.Score.Value, Enumerable.Range(0, 6).Where(i => w[i] > 0).Sum(i => w[i] * mixed.Metrics[i].Value / plan.Normalization[i]),
            "mixed score is weighted retained production, not multiplier size or improvement-only utility");
        Check(mixed.Metrics[0] / plan.Normalization[0] > .95 && mixed.Metrics[1] / plan.Normalization[1] > .99,
            "weak-multiplier E goal still contributes nearly its full retained-production weight");
        r.DiggerWeights = new double[12]; r.DiggerWeights[4] = 10; r.DiggerWeights[5] = 1;
        Check(Evaluate(s, r).Support.Diggers.Single().Id == 4, "explicit digger ranking cannot be replaced by a more productive excluded priority");
        r.DiggerWeights = new double[12]; r.DiggerWeights[4] = 1;
        Check(Evaluate(s, r).Support.Diggers.Single().Id == 4, "zero explicit digger weight excludes the strong M alternative");
        r.DiggerWeights = new double[12];
        Check(Evaluate(s, r).Support.Diggers.Length == 0, "explicit all-zero digger preferences remain empty");

        r = Request(); r.PlannedSupport = true; r.Weights = new[] { 1d, 0, 0, 0, 0, 0 };
        s.UnlockedBeards = new[] { 0, 1, 3, 5, 6 }; s.BeardSlots = 1;
        s.NguFactors = Enumerable.Repeat(1f, 6).ToArray(); s.BeardTemporary[0] = 2;
        s.Diggers = new[] { new SupportDigger { Id = 4, Maximum = 1, BaseDrain = 50, Growth = 2, ConsumesGold = true, PerLevel = 5 } };
        s.GoldInactiveGross = 10; s.GoldActiveGross = 1000;
        Check(Evaluate(s, r).Support.Beards.SequenceEqual(new[] { 1 }), "scarce default beard slots preserve the mandatory ordered core prefix, not a better NGU or Gold alternative");
        r.BeardPriority = new[] { 6 };
        var gold = Evaluate(s, r);
        Check(gold.Support.Beards.SequenceEqual(new[] { 6 }) && gold.Support.Diggers.Single().Id == 4,
            "explicit Gold beard changes jointly scored support affordability before digger planning");
        Check(gold.Support.Drain <= gold.Support.Budget && gold.Support.Remaining >= 0, "searched support remains within Gold-adjusted budget");
        r.BeardPriority = new[] { 3, 6 };
        var pinned = Evaluate(s, r);
        Check(pinned.Support.Beards.SequenceEqual(new[] { 3 }) && pinned.Support.Diggers.Length == 0,
            "explicit beard order fixes capped choice despite a better Gold alternative");
        r.BeardPriority = new int[0]; s.Diggers = new SupportDigger[0];
        Check(Evaluate(s, r).Support.Beards.SequenceEqual(new[] { 1 }), "production optimization cannot bypass the default mandatory core order");
        s.BeardSlots = 4;
        Check(Evaluate(s, r).Support.Beards.OrderBy(x => x).SequenceEqual(new[] { 1, 3, 5, 6 }), "all available core beards precede optional growth-only beards");
        s.UnlockedBeards = Enumerable.Range(0, 7).ToArray(); s.BeardSlots = 5; s.CurrentBeards = new[] { 1, 3, 5, 6, 4 }; r.Swap = 100;
        Check(Evaluate(s, r).Support.Beards.OrderBy(x => x).SequenceEqual(s.CurrentBeards.OrderBy(x => x)),
            "optional beard search retains the exact current optional when it avoids a real production swap cost");
        s.BeardsDisabled = true;
        Check(Evaluate(s, r).Support.Beards.Length == 0, "planned alternatives never reactivate disabled beards");

        s = Snapshot(); s.Items = s.Current; r = Request(); r.PlannedSupport = true;
        r.Weights = new[] { 1d, 0, 0, 0, 0, 0 }; r.MinimumPower = 2000;
        s.DiggerSlots = 1; s.Diggers = new[] { new SupportDigger { Id = 3, Maximum = 1, PerLevel = 2 } };
        Check(Evaluate(s, r).Feasible && Evaluate(s, r).Support.Diggers.Single().Id == 3,
            "support search includes adventure support for explicit combat constraints even with no farming weight");

        s = Snapshot(); s.Items = s.Current; r = Request(); r.PlannedSupport = true;
        r.Weights = new[] { 0d, 0, 1, 0, 0, 0 }; r.BloodPercent = 100;
        s.DiggerSlots = 1;
        s.Diggers = new[] { new SupportDigger { Id = 10, Maximum = 10, BaseDrain = 100, Growth = 2, ConsumesGold = true, PerLevel = .1 } };
        s.Rituals[0].Coefficient = 1; s.Rituals[0].Cost = 14;
        var blood = Evaluate(s, r);
        Check(blood.Metrics[2] > 0 && blood.Support.Diggers.Single().Level == 2,
            "actual blood production prefers reduced digger funding that leaves ritual GPS rather than maximum multiplier");
        Near(blood.Support.Drain, 200, "blood search pins the exact affordable lower level");

        s = Snapshot(); s.Items = s.Current; r = Request();
        s.DiggerSlots = 1; s.Diggers = new[] { new SupportDigger { Id = 4, Maximum = 10, Level = 1, PerLevel = .2 } };
        s.CurrentDiggers = s.Diggers;
        s.CurrentBeards = new[] { 3 }; s.UnlockedBeards = new[] { 3 }; s.BeardSlots = 1;
        r.BeardPriority = new[] { 6 }; r.DiggerWeights = new double[12]; r.DiggerWeights[5] = 100;
        var current = Evaluate(s, r);
        Check(current.Support.Beards.SequenceEqual(s.CurrentBeards) && current.Support.Diggers.Single().Level == 1,
            "current-support mode ignores planned preferences and never searches support replacements");
        Check(current.Support.DiggerWeights.Length == 0, "current support has no applicable chosen weights even with explicit request weights");

        r.PlannedSupport = true; r.BeardPriority = new[] { 3 }; r.DiggerWeights = new double[12]; r.DiggerWeights[4] = 1;
        plan = LoadoutPresetPlanner.Plan(s, r);
        var fresh = s.ProgressionCopy(); fresh.GoldActiveGross = fresh.GoldInactiveGross = 1e6;
        fresh.EnergyNgu[0].Level += 1;
        var replay = LoadoutPresetPlanner.Replay(fresh, r, plan);
        Check(replay.Normalization.SequenceEqual(plan.Normalization) && replay.ReferenceProduction.SequenceEqual(plan.ReferenceProduction)
            && replay.ReferenceKeys.SequenceEqual(plan.ReferenceKeys), "replay freezes reference production, keys and denominators despite natural progress");
        foreach (var policy in plan.Candidates.Concat(plan.Specialists))
        {
            var observed = replay.Candidates.Concat(replay.Specialists).Single(c => c.Key == policy.Key);
            Check(new SupportSelection(observed.Support.Beards, observed.Support.Diggers.Select(d => new SupportDiggerLevel(d.Id, d.Level)).ToArray()).StableKey
                == new SupportSelection(policy.Support.Beards, policy.Support.Diggers.Select(d => new SupportDiggerLevel(d.Id, d.Level)).ToArray()).StableKey,
                "all cached roles replay exact beard IDs and digger levels without resizing");
            Check(policy.Support.DiggerWeights.SequenceEqual(r.DiggerWeights) && observed.Support.DiggerWeights.SequenceEqual(policy.Support.DiggerWeights),
                "all explicit roles preserve raw weights through planning and replay");
        }
    }

    private static void AccessorySacrificeChecks()
    {
        foreach (var goal in new[] { 3, 4 })
        foreach (var planned in new[] { false, true })
        {
            var s = Snapshot(); var r = Request(); r.Weights[goal] = .35;
            r.PlannedSupport = planned;
            s.DiggerSlots = 2; s.Diggers = new[] { 3, 4, 5 }.Select(id => new SupportDigger { Id = id, Maximum = 1, PerLevel = .1 }).ToArray();
            var combat = Enumerable.Range(0, 3).Select(i => new PresetItem { Id = 100 + i, SourceSlot = 10000 + i, Group = 10000, Name = "Combat" }).ToArray();
            foreach (var item in combat) item.Stats[0] = 1000;
            var energy = new PresetItem { Id = 110, SourceSlot = 10, Group = 10000, Name = "E accessory" }; Stat(energy, PresetStat.EnergyPower, 400);
            var magic = new PresetItem { Id = 111, SourceSlot = 11, Group = 10000, Name = "M accessory" }; Stat(magic, PresetStat.MagicPower, 400);
            var farm = new PresetItem { Id = 112, SourceSlot = 12, Group = 10000, Name = "Farm accessory" };
            var loot = new PresetItem { Id = 113, SourceSlot = 13, Group = 10000, Name = "Loot specialist accessory" };
            loot.Stats[(int)PresetStat.Drop] = 200;
            farm.Stats[0] = 1200; farm.Stats[(int)PresetStat.Drop] = 100;
            s.Current = combat; s.Items = combat.Concat(new[] { energy, magic, farm, loot }).ToArray();
            s.Slots = new[] { 10000, 10001, 10002 }; s.Factors = new[] { 1d, 1, 1 };
            s.BasePower = 100; s.Available[3] = s.Available[4] = true;
            s.PpPerKill = Enumerable.Range(0, 100).Select(i => 200d + i).ToArray();
            s.ItopodBoostPerKill = Enumerable.Repeat(.1, 100).ToArray();
            s.Zones = new[] { new PresetZone { Id = 0, IdlePower = 1000, IdleToughness = 10, OneHitPower = 1000,
                NormalRatio = 1, BoostValues = new[] { 100d }, Chances = new[] { .2 }, Caps = new[] { 1d } } };
            var plan = LoadoutPresetPlanner.Plan(s, r); var mixed = plan.Candidates[1];
            Check(mixed.Feasible && mixed.Items.Any(i => i.Id == 110) && mixed.Items.Any(i => i.Id == 111),
                "mixed accessory sacrifice keeps both NGUs while supporting farming goal " + goal);
            Check(mixed.Metrics[0] > plan.Candidates[0].Metrics[0] * 3 && mixed.Metrics[1] > plan.Candidates[0].Metrics[1] * 3,
                "sacrificing combat accessories produces real E and M levels, not union-stat scores");
            Check(mixed.Metrics[goal] > 0 && mixed.Metrics[goal] < plan.Normalization[goal], "farming production tradeoff is retained and measured versus fixed specialist");
            Check(mixed.Score >= plan.Candidates[0].Score, "balanced accessory candidate does not regress weighted current production");
            var again = LoadoutPresetPlanner.Plan(s, r);
            Check(again.Candidates[1].Items.Select(i => i.Id).SequenceEqual(mixed.Items.Select(i => i.Id))
                && again.Normalization.SequenceEqual(plan.Normalization), "mixed accessory search and fixed references are deterministic");
        }
    }

    private static void BudgetSeedFallbackChecks()
    {
        var s = Snapshot(); var r = Request(); r.PlannedSupport = true; r.Horizon = 604800;
        r.Weights = Enumerable.Repeat(1d, 6).ToArray(); r.BloodPercent = 50; r.Zone = 0;
        r.BeardPriority = new[] { 3, 6 }; s.UnlockedBeards = new[] { 1, 3, 5, 6 }; s.BeardSlots = 1;
        r.DiggerWeights = new double[12]; r.DiggerWeights[4] = r.DiggerWeights[5] = 1;
        s.DiggerSlots = 2; s.Diggers = new[] { 4, 5 }.Select(id => new SupportDigger {
            Id = id, Maximum = 20, BaseDrain = 1, Growth = 2, ConsumesGold = true, PerLevel = .001 }).ToArray();
        s.Available = Enumerable.Repeat(true, 6).ToArray(); s.PpPerKill = Enumerable.Range(0, 100).Select(f => 200d + f).ToArray();
        s.ItopodBoostPerKill = Enumerable.Repeat(.1, 100).ToArray();
        s.Zones = new[] { new PresetZone { Id = 0, IdlePower = 900, IdleToughness = 900, OneHitPower = 1000,
            NormalRatio = 1, BoostValues = new[] { 10d }, Chances = new[] { .5 }, Caps = new[] { 1d } } };
        s.Slots = new[] { -5 }.Concat(Enumerable.Range(10000, 20)).ToArray(); s.Factors = Enumerable.Repeat(1d, 21).ToArray();
        s.Current = s.Current.Concat(Enumerable.Range(0, 20).Select(i => new PresetItem { Id = 500 + i, SourceSlot = 10000 + i, Group = 10000 })).ToArray();
        s.Items = s.Items.Concat(s.Current.Skip(1)).Concat(Enumerable.Range(0, 200).Select(i => {
            var item = new PresetItem { Id = 1000 + i, SourceSlot = 100 + i, Group = 10000 };
            Stat(item, PresetStat.EnergyPower, i % 30 * 10); Stat(item, PresetStat.MagicPower, (200 - i) % 31 * 10); return item;
        })).ToArray();
        // Zero initial levels and long horizons exercise expensive bounded native-tick integration.
        s.EnergyNgu = Enumerable.Range(0, 9).Select(i => new PresetNgu { Coefficient = .001, HardCap = 1000000000, Weight = 1 }).ToArray();
        s.MagicNgu = Enumerable.Range(0, 7).Select(i => new PresetNgu { Coefficient = .001, HardCap = 1000000000, Weight = 1 }).ToArray();
        var timer = Stopwatch.StartNew(); var plan = LoadoutPresetPlanner.Plan(s, r); timer.Stop();
        Check(plan.Budget && plan.Specialists.Any(c => c.Label.Contains("budget fallback")), "large planned-support six-goal fixture exhausts search before all specialist passes finish");
        Check(timer.ElapsedMilliseconds < 3500 && plan.Evaluated <= MaximumPlanEvaluations, "post-budget seed comparison remains a small bounded operation, not another search pass");
        var weights = LoadoutPresetPlanner.NormalizeWeights(r.Weights);
        var seeds = plan.Specialists.Select(seed => LoadoutPresetPlanner.Evaluate(s, r, seed.Items, weights, plan.Normalization, "seed", "Original-policy seed")).ToArray();
        var feasible = seeds.Where(c => c.Score.HasValue).ToArray();
        Check(feasible.Length > 0 && feasible.Max(c => c.Score.Value) > plan.Candidates[0].Score.Value + .01,
            "budget fixture has an independently rescored legal specialist-item seed materially better than current gear");
        Check(plan.Candidates[1].Score.HasValue && plan.Candidates[1].Score.Value + 1e-12 >= feasible.Max(c => c.Score.Value),
            "balanced includes feasible specialist-item seeds under original mixed request even after budget exhaustion");
        Check(plan.Candidates[1].BloodPercent == 50 && plan.Candidates[1].Zone == 0,
            "fallback never imports specialist full-focus split overrides or a different farming route");
        Check(plan.Candidates[1].Support.Beards.SequenceEqual(new[] { 3 }) && plan.Candidates[1].Support.Diggers.All(d => d.Id == 4 || d.Id == 5)
            && plan.Candidates[1].Support.Drain <= plan.Candidates[1].Support.Budget,
            "fallback preserves explicit original beard/digger preference constraints and affordability");
        var current = LoadoutPresetPlanner.Evaluate(s, r, s.Current, weights, plan.Normalization, "current", "Current");
        Check(plan.Candidates[0].Items.SequenceEqual(s.Current) && plan.Candidates[0].Metrics.SequenceEqual(current.Metrics),
            "seed fallback does not replace or rewrite the current comparison candidate");
        for (var g = 0; g < 6; g++)
        {
            var reference = plan.Specialists.Where(c => c.Feasible && c.Metrics[g].HasValue).OrderByDescending(c => c.Metrics[g].Value).FirstOrDefault();
            if (reference != null && reference.Metrics[g] > 0)
                Check(plan.ReferenceKeys[g] == reference.Key && plan.ReferenceProduction[g] == reference.Metrics[g]
                    && plan.Normalization[g] == reference.Metrics[g], "fallback preserves positive fixed specialist reference for goal " + g);
            else
                Check(plan.Normalization[g] == (plan.ReferenceProduction[g] ?? 0)
                    && (plan.Normalization[g] == 0 || plan.ReferenceKeys[g].StartsWith("reference-seed-")),
                    "missing specialist reference uses only a measured original-policy seed or remains zero for goal " + g);
        }
        Console.WriteLine("Budget seed fallback: " + plan.Evaluated + " evaluations in " + timer.ElapsedMilliseconds + " ms; current="
            + plan.Candidates[0].Score + ", balanced=" + plan.Candidates[1].Score + ", best original-policy seed=" + feasible.Max(c => c.Score.Value));

        s = Snapshot(); r = Request(); r.PlannedSupport = true; r.BloodPercent = 40; r.Weights = new[] { 0d, 0, 1, 0, 0, 0 };
        r.BeardPriority = new[] { 3 }; s.UnlockedBeards = new[] { 3, 6 }; s.BeardSlots = 1;
        r.DiggerWeights = new double[12]; r.DiggerWeights[10] = 1;
        s.DiggerSlots = 1; s.Diggers = new[] { new SupportDigger { Id = 10, Maximum = 10, BaseDrain = 1, Growth = 2, ConsumesGold = true, PerLevel = .1 } };
        plan = LoadoutPresetPlanner.Plan(s, r);
        Check(plan.Specialists[2].BloodPercent == 100 && plan.Specialists[2].Score > plan.Candidates[1].Score,
            "full-focus blood reference intentionally has a better raw score under its overridden split, so copying it would be an invalid shortcut");
        var originalSeed = LoadoutPresetPlanner.Evaluate(s, r, plan.Specialists[2].Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), plan.Normalization, null, null);
        Check(plan.Candidates[1].BloodPercent == 40 && plan.Candidates[1].Score >= originalSeed.Score,
            "final seed comparison recomputes the original fixed split rather than importing the specialist's 100-percent blood policy");
        Check(plan.Candidates[1].Support.Beards.SequenceEqual(new[] { 3 }) && plan.Candidates[1].Support.Diggers.All(d => d.Id == 10),
            "final fallback retains original explicit support preferences even for a stronger full-focus reference");
    }

    private static void ZeroReferenceChecks()
    {
        // Original mandatory-AT mix: only the AT focus is feasible; it cannot
        // supply an Energy-NGU reference, although a legal original-policy mix can.
        var s = ActivitySnapshot(); var r = ActivityRequest(0, 1, 3, 4, 5, 9, 10);
        r.Weights[3] = 1000; r.Zone = -1;
        s.Available = Enumerable.Repeat(true, 14).ToArray();
        s.RequiredATTargets = new long[] { 20, 10 };
        s.AdvancedTraining[0].Level = s.AdvancedTraining[1].Level = 0;
        s.AdvancedTraining[0].Target = 20; s.AdvancedTraining[1].Target = 10;
        s.PpPerKill = new[] { 0d, 221 }; s.ItopodBoostPerKill = new[] { 0d, .14 };
        s.ItopodHp = new[] { 0d, 600 }; s.ItopodDefense = new[] { 0d, 10 };
        s.Zones = new[] { new PresetZone { Id = 0, IdlePower = 1, IdleToughness = 1, OneHitPower = 1,
            NormalRatio = 1, BoostValues = new[] { 1d }, Chances = new[] { 1d }, Caps = new[] { 1d } } };
        var savedWeights = r.Weights.ToArray(); var timer = Stopwatch.StartNew();
        var plan = LoadoutPresetPlanner.Plan(s, r); var mixed = plan.Candidates[1];
        Check(plan.Specialists.Where(c => c.Feasible).All(c => c.Metrics[0] == 0), "mandatory AT specialist population has no positive NGU reference");
        Check(plan.ReferenceProduction[0] > 0 && plan.ReferenceKeys[0].StartsWith("reference-seed-"),
            "original-policy feasible seed supplies measured positive NGU denominator");
        Check(mixed.Feasible && mixed.Activities.AT.Take(2).All(x => x > 0) && mixed.Zone == 1000,
            "weight1000 PP wins original mixed route without sacrificing mandatory unfinished AT");
        Check(mixed.Score.HasValue && mixed.Score < 100 && r.Weights.SequenceEqual(savedWeights), "no epsilon distortion or saved weight mutation");
        for (var g = 0; g < 14; g++)
        {
            var reference = plan.Specialists.Where(c => c.Feasible && c.Metrics[g] > 0).OrderByDescending(c => c.Metrics[g]).FirstOrDefault();
            if (reference != null) Check(plan.ReferenceProduction[g] == reference.Metrics[g] && plan.ReferenceKeys[g] == reference.Key,
                "positive specialist reference is unchanged/" + g);
        }
        var fresh = s.ProgressionCopy(); fresh.EnergyNgu[0].Level++;
        var replay = LoadoutPresetPlanner.Replay(fresh, r, plan);
        Check(replay.Normalization.SequenceEqual(plan.Normalization) && replay.ReferenceProduction.SequenceEqual(plan.ReferenceProduction)
            && replay.ReferenceKeys.SequenceEqual(plan.ReferenceKeys), "fallback reference metadata freezes through replay");
        Check(plan.Evaluated <= MaximumPlanEvaluations && timer.ElapsedMilliseconds < 3500, "fallback and replay remain bounded");

        Near(LoadoutPresetPlanner.Score(new double?[] { 1e90, 221 }, new[] { 1d / 1001, 1000d / 1001 }, new[] { 0d, 221 }, true).Value,
            1, "late positive unreferenced output earns only its weight, never epsilon amplification");
        Near(LoadoutPresetPlanner.Score(new double?[] { 0 }, new[] { 1d }, new[] { 0d }, true).Value, 0, "known zero reference/output earns zero");
        Check(!LoadoutPresetPlanner.Score(new double?[] { null }, new[] { 1d }, new[] { 0d }, true).HasValue,
            "unavailable positive goal still blocks score");
        Check(!LoadoutPresetPlanner.Score(new double?[] { 100 }, new[] { 1d }, new[] { 0d }, false).HasValue,
            "missing reference never bypasses infeasibility");
        foreach (var state in new[] { "excluded", "completed", "unavailable" })
        {
            s = Snapshot(); s.Items = s.Current; r = Request(); r.Weights = new[] { 1d, 0, 0, 0, 0, 0 };
            if (state == "excluded") s.EnergyNgu[0].Weight = 0;
            if (state == "completed") s.EnergyNgu[0].Target = s.EnergyNgu[0].Level;
            if (state == "unavailable") s.Available[0] = false;
            plan = LoadoutPresetPlanner.Plan(s, r);
            Check(!(plan.ReferenceProduction[0] > 0) && plan.Normalization[0] == 0,
                "no fabricated reference for " + state);
            Check(state == "unavailable" ? !plan.Candidates[1].Score.HasValue : plan.Candidates[1].Score == 0,
                "zero/unavailable production semantics preserved for " + state);
        }
        s = ActivitySnapshot(); s.RequiredATTargets = new long[] { 20, 20 }; s.AdvancedTraining[0].Unlocked = false;
        r = ActivityRequest(0, 9); plan = LoadoutPresetPlanner.Plan(s, r);
        Check(!plan.Candidates[1].Feasible && !(plan.ReferenceProduction[0] > 0), "locked mandatory AT cannot supply infeasible fallback output");
    }

    private static void ShortMagicHorizonChecks()
    {
        var s = Snapshot(); s.Items = s.Current; var r = Request(); r.BloodPercent = 0; r.Horizon = 900;
        s.MagicNgu = new[] { new PresetNgu { Level = 10000, Coefficient = .0001, SpeedDivider = 10000, HardCap = 1000000000, Weight = 1 } };
        var shortPlan = LoadoutPresetPlanner.Plan(s, r);
        Check(shortPlan.Candidates[1].MagicNgu > 0 && shortPlan.Candidates[1].Metrics[1] == 0 && shortPlan.ReferenceProduction[1] == 0,
            "native-shaped 15-minute forecast may validly complete zero Magic levels despite positive allocation and weight");
        var productive = s.ProgressionCopy(); productive.Magic = 100000; productive.MagicCapacity = new ResourceProjection { Capacity = true, BaseCap = 100000 };
        var newlyProductive = LoadoutPresetPlanner.Replay(productive, r, shortPlan);
        Check(newlyProductive.Candidates[1].Metrics[1] > 0 && newlyProductive.Normalization[1] == 0
            && newlyProductive.ReferenceProduction[1] == 0 && newlyProductive.Candidates[1].Score < 10,
            "later productive replay stays bounded without changing a genuinely zero acquired reference");
        var row = s.MagicNgu[0]; var numerator = (float)((1f / row.SpeedDivider) * (float)s.Magic);
        var increment = (float)(numerator / (row.Level + 1)); var progress = 0f; var firstTicks = 0;
        while (progress < 1 && firstTicks < 7200 * 50) { progress += increment; firstTicks++; }
        Check(firstTicks > 900 * 50 && firstTicks < 7200 * 50 && progress >= 1,
            "independent literal float tick oracle explains zero short-horizon gains without inventing fractional production");
        r.Horizon = 7200; var longPlan = LoadoutPresetPlanner.Plan(s, r);
        Check(longPlan.Candidates[1].Metrics[1] > 0 && longPlan.ReferenceProduction[1] > 0,
            "same native-shaped Magic setup completes real levels at a longer horizon; production rules and zero-reference semantics stay unchanged");
    }

    private static void FloorComparisonChecks()
    {
        var s = Snapshot(); s.Items = s.Current; var r = Request(); r.Weights = new[] { 0d, 0, 0, 1, 0, 0 };
        s.Available[3] = s.Available[4] = true;
        s.PpPerKill = new[] { 0d, 200, 240, 1000 };
        s.ItopodHp = new[] { 0d, 500, 900, 1300 }; s.ItopodDefense = new double[4];
        s.ItopodBoostPerKill = new[] { 0d, 100, 1, 1 };
        var selected = Evaluate(s, r);
        Check(selected.Floor == 3 && selected.Hits == 2, "PP compares actual captured per-floor progress against short multihit timing, not just highest one-hit floor");
        Near(selected.Metrics[3].Value, 1000d / 6, "PP rate uses the selected floor's two-hit denominator");
        r.Weights = new[] { 0d, 0, 0, 0, 1, 0 };
        Check(Evaluate(s, r).Floor == 1, "joint route scoring can retain a lower boost-rich floor");
        r.Weights = new[] { 0d, 0, 0, 1, 0, 0 };
        var plan = LoadoutPresetPlanner.Plan(s, r);
        var fresh = s.ProgressionCopy(); fresh.PpPerKill = new[] { 0d, 1e6, 1e6, 1000 };
        var replay = LoadoutPresetPlanner.Replay(fresh, r, plan);
        Check(replay.Candidates[1].Floor == 3 && replay.Candidates[1].Hits == 2, "replay does not substitute a newly better floor");
        fresh.ItopodHp = new[] { 0d, 500, 900, 1e12 };
        Check(!LoadoutPresetPlanner.Replay(fresh, r, plan).Candidates[1].Feasible, "infeasible pinned floor fails closed rather than rerouting");
        s.ItopodHp = s.ItopodDefense = null;
        Check(Evaluate(s, r).Floor == 3 && Evaluate(s, r).Hits == 1, "missing native enemy data uses declared existing one-hit-range proxy only");
        s.PpPerKill[1] = 1e6;
        Check(Evaluate(s, r).Floor == 1, "one-hit-range comparison does not assume native PP is monotonic");

        s.PpPerKill = Enumerable.Range(0, 60).Select(f => 200d + f).ToArray();
        s.ItopodHp = Enumerable.Range(0, 60).Select(f => (double)(float)(600f * (float)Math.Pow(1.05f, f) * 1.02f)).ToArray();
        s.ItopodDefense = Enumerable.Range(0, 60).Select(f => (double)(float)(10f * (float)Math.Pow(1.05f, f) * 1.02f)).ToArray();
        s.ItopodBoostPerKill = null; s.Respawn = 100;
        var oneHit = LoadoutPresetPlanner.ItopodFloor(1000, s.IdleModifier, 59);
        selected = Evaluate(s, r);
        Check(selected.Floor > oneHit && selected.Hits > 1 && selected.Hits <= 4
            && selected.Metrics[3] > s.PpPerKill[oneHit] / 101,
            "native-shaped monotonic PP and 1.05f-scaled enemies can favor multihit when respawn dominates; no arbitrary combat multiplier is needed");
        var fixedFloorPlan = LoadoutPresetPlanner.Plan(s, r);
        s.LazyItopodPurchased = true;
        Check(Evaluate(s, r).Floor == selected.Floor, "purchased but disabled Lazy ITOPOD leaves fixed-floor search unchanged");
        s.LazyItopodPurchased = false; s.LazyItopodEnabled = true;
        Check(Evaluate(s, r).Floor == selected.Floor, "enabled flag without purchase does not activate native Lazy ITOPOD routing");
        s.LazyItopodPurchased = true;
        var constrained = Evaluate(s, r);
        Check(constrained.Feasible && constrained.Floor == oneHit && constrained.Floor != selected.Floor,
            "purchased and enabled shifter restricts actual production to native selected floor rather than the better incompatible multihit winner");
        Check(s.LazyItopodPurchased && s.LazyItopodEnabled, "forecast never toggles either native shifter preference");
        var incompatible = LoadoutPresetPlanner.Replay(s, r, fixedFloorPlan).Candidates[1];
        Check(!incompatible.Feasible && !incompatible.Score.HasValue && incompatible.Warnings.Any(x => x.Contains("Lazy ITOPOD shifter")),
            "exact replay rejects a fixed floor that the active shifter would replace, rather than silently substituting a native floor");
        var nativePlan = LoadoutPresetPlanner.Plan(s, r);
        var changedPower = s.ProgressionCopy(); changedPower.BasePower *= 1.2;
        Check(!LoadoutPresetPlanner.Replay(changedPower, r, nativePlan).Candidates[1].Feasible,
            "natural power changes moving the native shifter floor invalidate an incompatible pinned floor on replay");
        s.BasePower = 600;
        Check(!Evaluate(s, r).Feasible && Evaluate(s, r).Warnings.Any(x => x.Contains("stationary ITOPOD route")),
            "native shifter floor zero cannot be advertised as a stationary repeatable route");

        s = Snapshot(); r = Request(); r.PlannedSupport = true;
        s.UnlockedBeards = new[] { 0, 1, 2, 3, 4, 5, 6 }; s.BeardSlots = 2;
        s.DiggerSlots = 2; s.Diggers = new[] { 0, 3, 4, 5, 8, 10 }.Select(id => new SupportDigger {
            Id = id, Maximum = 30, BaseDrain = 1, Growth = 2, ConsumesGold = true, PerLevel = .1 }).ToArray();
        s.Slots = new[] { -5 }.Concat(Enumerable.Range(10000, 8)).ToArray(); s.Factors = Enumerable.Repeat(1d, 9).ToArray();
        s.Current = s.Current.Concat(Enumerable.Range(0, 8).Select(i => new PresetItem { Id = 500 + i, SourceSlot = 10000 + i, Group = 10000 })).ToArray();
        s.Items = s.Items.Concat(s.Current.Skip(1)).Concat(Enumerable.Range(0, 100).Select(i => {
            var item = new PresetItem { Id = 1000 + i, SourceSlot = 100 + i, Group = 10000 };
            Stat(item, PresetStat.EnergyPower, i % 20 * 10); Stat(item, PresetStat.MagicPower, (100 - i) % 21 * 10); return item;
        })).ToArray();
        var timer = Stopwatch.StartNew(); plan = LoadoutPresetPlanner.Plan(s, r); timer.Stop();
        Check(plan.Evaluated <= MaximumPlanEvaluations && timer.ElapsedMilliseconds < 5000, "joint planned-support search has bounded work and practical runtime");
        Check(plan.Candidates.Concat(plan.Specialists).All(c => LoadoutPresetPlanner.Legal(s, c.Items)
            && c.Support.Beards.Length <= 2 && c.Support.Diggers.Length <= 2 && c.Support.Drain <= c.Support.Budget), "joint search retains slot, identity and Gold legality");
        Console.WriteLine("Planned-support search: " + plan.Evaluated + " evaluations in " + timer.ElapsedMilliseconds + " ms");
        r.Horizon = 604800;
        s.EnergyNgu = Enumerable.Range(0, 9).Select(i => new PresetNgu { Coefficient = .001, Level = 10000, HardCap = 1000000000, Weight = 1 }).ToArray();
        s.MagicNgu = Enumerable.Range(0, 7).Select(i => new PresetNgu { Coefficient = .001, Level = 10000, HardCap = 1000000000, Weight = 1 }).ToArray();
        timer.Restart(); plan = LoadoutPresetPlanner.Plan(s, r); timer.Stop();
        Check(plan.Evaluated <= 6064 && timer.ElapsedMilliseconds < 5000, "maximum-horizon planned support shares the bounded operation clock instead of multiplying unbounded gear/support work");
        Check(plan.Budget, "expensive maximum-horizon planned search reports truncation rather than claiming exhaustive completion");
        Console.WriteLine("Maximum-horizon planned support: " + plan.Evaluated + " evaluations in " + timer.ElapsedMilliseconds + " ms");
    }
}
