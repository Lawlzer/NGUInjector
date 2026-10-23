using System;
using System.Diagnostics;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    static partial void CapacityObjectiveChecks()
    {
        var before = assertions;
        Check(LoadoutPresetPlanner.Units[12] == "energy" && LoadoutPresetPlanner.Units[13] == "magic",
            "capacity objectives report instant resource units");
        foreach (var goal in new[] { 12, 13 })
        {
            var s = Snapshot();
            s.Available = Enumerable.Repeat(true, 14).ToArray();
            s.Available[3] = s.Available[4] = s.Available[5] = false;
            s.MagicCapacity.BaseCap = 2000;
            s.Energy = 2; s.Magic = 3; s.ReservedEnergy = 500;
            s.NguAutoAdvance = true; s.EnergyNgu[0].Target = 10001;
            var r = Request(); r.Weights = new double[14]; r.Weights[goal] = 1;
            r.Horizon = 1; r.Swap = 3600; r.Zone = 43;
            var offhand = new PresetItem { Id = 50, SourceSlot = -6, Group = -5 };
            var highTier = new PresetItem { Id = 1000, SourceSlot = 100, Group = -5 };
            var lowerDuplicate = new PresetItem { Id = 1000, SourceSlot = 101, Group = -5 };
            var ordinary = new PresetItem { Id = 1001, SourceSlot = 102, Group = -5 };
            var allCap = new PresetItem { Id = 1002, SourceSlot = 103, Group = -5 };
            var cap = goal == 12 ? ResourceSpec.EnergyCap : ResourceSpec.MagicCap;
            var high = goal == 12 ? ResourceSpec.EnergyCap3 : ResourceSpec.MagicCap3;
            var opposite = goal == 12 ? ResourceSpec.MagicCap : ResourceSpec.EnergyCap;
            highTier.ResourceSpecs[(int)high] = 4000000;
            lowerDuplicate.ResourceSpecs[(int)cap] = 10000;
            ordinary.ResourceSpecs[(int)cap] = 30000;
            allCap.ResourceSpecs[(int)ResourceSpec.AllCap] = 2000000;
            var distractors = Enumerable.Range(0, 48).Select(i => {
                var item = new PresetItem { Id = 2000 + i, SourceSlot = 200 + i, Group = -5 };
                Stat(item, PresetStat.EnergyPower, 1e12 + i); Stat(item, PresetStat.MagicPower, 1e12 + i);
                item.Stats[(int)PresetStat.NguSpeed] = 1e12;
                item.ResourceSpecs[(int)ResourceSpec.NGU] = 1e12f;
                item.ResourceSpecs[(int)opposite] = 1e12f;
                item.Stats[0] = item.Stats[1] = 1e20;
                item.Stats[(int)PresetStat.EnergyBars] = item.Stats[(int)PresetStat.MagicBars] = 1e20;
                // A misleading legacy aggregate must never beat actual raw capacity specs.
                item.Stats[(int)(goal == 12 ? PresetStat.EnergyCap : PresetStat.MagicCap)] = 1e20;
                return item;
            }).ToArray();
            // Capture order deliberately puts the weak hand first.
            s.Slots = new[] { -6, -5 }; s.Factors = new[] { .25, 1d };
            s.Current = new[] { offhand, s.Current[0] };
            s.Items = s.Current.Concat(distractors).Concat(new[] { lowerDuplicate, ordinary, allCap, highTier }).ToArray();
            var plan = LoadoutPresetPlanner.Plan(s, r); var best = plan.Candidates[1];
            Check(best.Feasible && best.Score.HasValue && best.Items.SequenceEqual(new[] { ordinary, highTier }),
                "cap-only chooses strongest raw tiers/duplicate instance and full-strength primary over unrelated massive bonuses: " + goal);
            var projection = goal == 12 ? s.EnergyCapacity : s.MagicCapacity;
            var expected = goal == 12 ? 5750 : 11500;
            Near(best.Metrics[goal].Value, expected, "capacity is native total, not current pool or retained-training-adjusted pool");
            Check(best.EnergyPool == 2 && best.MagicPool == 3 && best.Activities == null,
                "capacity does not refill small pools or request activity training");
            Check(best.Metrics[goal] == best.RawMetrics[goal] && best.SetupChanged && plan.Sequence == null,
                "instant cap has no swap downtime penalty or time-shared sequence");
            Check(plan.Specialists[goal].Items.SequenceEqual(best.Items) && plan.ReferenceProduction[goal] == best.Metrics[goal],
                "cap focus seed survives specialist/mixed search with a frozen native reference");
            var oracle = (from a in s.Items from b in s.Items
                          let items = new[] { a, b }
                          where LoadoutPresetPlanner.Legal(s, items)
                          select projection.Project(LoadoutPresetPlanner.ResourceBonuses(s, items))).Max();
            Near(best.Metrics[goal].Value, oracle, "cap-only dual wield agrees with exhaustive native raw-spec oracle");
            var phase = LoadoutPresetPlanner.ProjectPhase(s.ProgressionCopy(), best, 25);
            Near(phase[goal].Value, expected, "phase cap is not multiplied by seconds");
            var replay = LoadoutPresetPlanner.Replay(s, r, plan).Candidates[1];
            Check(replay.Items.SequenceEqual(best.Items) && replay.Metrics[goal] == best.Metrics[goal] && s.NguAutoAdvance,
                "cap-only replay permits irrelevant finite NGU auto-advance without changing preference");
            r.Horizon = 604800; r.Swap = 0; s.Energy = s.Magic = 0;
            var longer = LoadoutPresetPlanner.Plan(s, r).Candidates[1];
            Check(longer.Items.SequenceEqual(best.Items) && longer.Metrics[goal] == best.Metrics[goal],
                "cap-only winner and total ignore horizon and even empty pools");
            Near(longer.Score.Value, best.Score.Value, "cap-only score has no hidden production/adventure/pool term");
            r.Weights[0] = 1;
            Rejected(() => LoadoutPresetPlanner.Plan(s, r), "auto-advance", "adding production restores the conservative native-advance guard");
            r.Weights[0] = 0;
            s.Available[goal] = false;
            Check(!Evaluate(s, r).Score.HasValue && !Evaluate(s, r).Metrics[goal].HasValue,
                "unavailable cap stays unknown rather than disappearing from weighted score");
            s.Available[goal] = true;
            s.ReservedEnergy = long.MaxValue;
            Check(!Evaluate(s, r).Feasible, "cap-only retains basic-training capacity feasibility");
            s.ReservedEnergy = 0; r.MinimumPower = 1e30;
            Check(!Evaluate(s, r).Feasible, "cap-only retains explicit adventure safety constraints");
            r.MinimumPower = 0;
            // AllCap alone must help both resources even when legacy union stats are absent.
            var all = LoadoutPresetPlanner.Evaluate(s, r, new[] { offhand, allCap }, LoadoutPresetPlanner.NormalizeWeights(r.Weights),
                Enumerable.Repeat(1d, 14).ToArray(), "all", "AllCap");
            Near(all.Metrics[12].Value, 3000, "AllCap projects native energy total");
            Near(all.Metrics[13].Value, 6000, "AllCap projects native magic total");
            if (goal == 12)
            {
                var expired = Stopwatch.StartNew(); System.Threading.Thread.Sleep(1801);
                var budgeted = LoadoutPresetPlanner.Plan(s, r, expired);
                Check(budgeted.Budget && budgeted.Candidates[1].Items.SequenceEqual(best.Items),
                    "cap greedy seed is retained even if unrelated work already consumed the wall-clock search budget");
            }
        }
        var mixedSnapshot = ActivitySnapshot();
        Array.Resize(ref mixedSnapshot.Available, 14); mixedSnapshot.Available[12] = mixedSnapshot.Available[13] = true;
        var mixedRequest = Request(); mixedRequest.Weights = new double[14]; mixedRequest.Weights[9] = mixedRequest.Weights[12] = 1;
        var mixed = Evaluate(mixedSnapshot, mixedRequest);
        Check(mixed.Metrics[9] > 0 && mixed.Activities.AT.Sum() > 0 && mixed.Metrics[12] == 1000,
            "mixed AT/cap keeps production and instant total capacity separate");
        var dual = Snapshot(); dual.Available = new bool[14]; dual.Available[12] = dual.Available[13] = true;
        Stat(dual.Items[1], PresetStat.EnergyCap, 400); Stat(dual.Items[2], PresetStat.MagicCap, 400);
        Stat(dual.Items[3], PresetStat.EnergyCap, 230); Stat(dual.Items[3], PresetStat.MagicCap, 230);
        var dualRequest = Request(); dualRequest.Weights = new double[14]; dualRequest.Weights[12] = dualRequest.Weights[13] = 1;
        var dualPlan = LoadoutPresetPlanner.Plan(dual, dualRequest);
        Check(dualPlan.Candidates[1].Items[0] == dual.Items[3], "dual-cap request chooses the true capacity compromise");
        Near(dualPlan.Candidates[1].Score.Value, .66, "dual-cap score uses only the two normalized total-capacity ratios");
        var soleWeapon = dual.Items[1]; soleWeapon.SourceSlot = -6;
        var empty = new PresetItem { SourceSlot = -5, Group = -5, Name = "Empty" };
        dual.Slots = new[] { -5, -6 }; dual.Factors = new[] { 1d, .25 };
        dual.Current = new[] { empty, soleWeapon }; dual.Items = new[] { soleWeapon };
        dualRequest.Weights[13] = 0;
        var moved = LoadoutPresetPlanner.Plan(dual, dualRequest);
        Check(moved.Candidates[1].Items[0] == soleWeapon && moved.Candidates[1].Items[1].Id == 0 && moved.Candidates[1].Feasible,
            "cap greedy moves sole offhand to empty main without duplicating its ID");
        var movedReplay = LoadoutPresetPlanner.Replay(dual, dualRequest, moved);
        Check(movedReplay.Candidates[1].Metrics[12] == 5000 && movedReplay.Candidates[1].Items[1].Id == 0,
            "cap replay retains virtual empty offhand and full-strength native total");
        Console.WriteLine("Capacity objective checks: " + (assertions - before));
    }
}
