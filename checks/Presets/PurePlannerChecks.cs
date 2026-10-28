using System;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    private static PresetRequest ObjectiveOnlyRequest(PresetRequest request)
    {
        var copy = request.Copy(); copy.ResourcePrioritiesReviewed = false;
        copy.EnergyPriorities = copy.MagicPriorities = null;
        return copy;
    }

    private static void SameSetup(PresetCandidate actual, PresetCandidate expected, string name)
    {
        Check(actual.Key == expected.Key && actual.Items.Select(x => x.Id + ":" + x.SourceSlot + ":" + x.Level)
            .SequenceEqual(expected.Items.Select(x => x.Id + ":" + x.SourceSlot + ":" + x.Level)), name + " exact gear and candidate key");
        Check(actual.Support.Beards.SequenceEqual(expected.Support.Beards)
            && actual.Support.Diggers.Select(x => x.Id + ":" + x.Level).SequenceEqual(expected.Support.Diggers.Select(x => x.Id + ":" + x.Level))
            && actual.Support.DiggerWeights.SequenceEqual(expected.Support.DiggerWeights), name + " support IDs, levels and raw weights");
        Check(actual.Zone == expected.Zone && actual.Floor == expected.Floor
            && actual.RegularEligibilityZone == expected.RegularEligibilityZone && actual.BoostEligibilityZone == expected.BoostEligibilityZone,
            name + " route and eligibility witnesses");
    }

    private static void SameReferences(PresetPlan actual, PresetPlan expected, string name)
    {
        Check(actual.Normalization.SequenceEqual(expected.Normalization) && actual.ReferenceKeys.SequenceEqual(expected.ReferenceKeys)
            && actual.ReferenceProduction.SequenceEqual(expected.ReferenceProduction), name + " frozen setup-derived references");
    }

    private static void PurePlannerChecks()
    {
        var before = assertions;
        PureSetupSearchChecks(); PureReferenceRepairChecks(); PureSetupFailureChecks();
        ZeroObjectiveFundingChecks(); ResidualActivityChecks(); ReviewedControlChecks(); ReviewedCapacityAdvanceChecks();
        ScarceGoldSetupChecks(); ScarceGoldSupportChecks(); ScarceGoldHardConstraintChecks();
        Console.WriteLine("Pure planner separation checks: " + (assertions - before));
    }

    private static void PureSetupSearchChecks()
    {
        var s = ActivitySnapshot(); s.Items = Snapshot().Items;
        s.Available = Enumerable.Repeat(true, 14).ToArray(); s.PpPerKill = new double[0];
        s.Zones = new[] { new PresetZone { Id = 0, IdlePower = 1, IdleToughness = 1, OneHitPower = 1,
            NormalRatio = 1, BoostValues = new[] { 1d }, Chances = new[] { 1d }, Caps = new[] { 1d } } };
        s.DiggerSlots = 1;
        s.Diggers = new[] { 1, 5, 10 }.Select(id => new SupportDigger { Id = id, Maximum = 2, PerLevel = 10 }).ToArray();
        s.BeardSlots = 1; s.UnlockedBeards = new[] { 3, 4 };
        s.NguFactors = Enumerable.Repeat(1f, 6).ToArray(); s.BeardTemporary[0] = 2;
        var r = PriorityRequest(); r.Weights = ActivityRequest(1, 4, 10).Weights; r.Weights[1] = 9;
        r.PlannedSupport = true; r.BeardPriority = new[] { 3 };
        var savedWeights = r.Weights.ToArray(); var savedEnergy = r.EnergyPriorities.ToArray(); var savedMagic = r.MagicPriorities.ToArray();
        var objective = ObjectiveOnlyRequest(r); var setup = LoadoutPresetPlanner.Plan(s, objective);
        Check(!setup.Budget && setup.Candidates[1].Items[0].Id == 42 && setup.Candidates[1].Support.Diggers.Single().Id == 5,
            "pure setup fixture has a concrete Magic gear and support winner");
        var orders = new[] {
            new[] { new[] { "NGU", "TM", "AUG", "WAN", "AT" }, new[] { "NGU", "RIT", "TM", "WAN" } },
            new[] { new[] { "AT", "WAN", "AUG", "TM", "NGU" }, new[] { "RIT", "WAN", "TM", "NGU" } },
            new[] { new string[0], new string[0] } };
        foreach (var order in orders)
        {
            var reviewed = r.Copy(); reviewed.EnergyPriorities = order[0]; reviewed.MagicPriorities = order[1];
            var plan = LoadoutPresetPlanner.Plan(s, reviewed);
            Check(!plan.Budget && plan.Candidates[1].Key == "balanced" && plan.Sequence == null,
                "reviewed plan retains internal balanced key and offers no proportional sequence");
            SameReferences(plan, setup, "changed zero-weight fallback chains");
            var expected = setup.Candidates.Concat(setup.Specialists).ToArray();
            var actual = plan.Candidates.Concat(plan.Specialists).ToArray();
            for (var i = 0; i < expected.Length; i++)
            {
                SameSetup(actual[i], expected[i], "reviewed role " + expected[i].Key);
                Check(actual[i].SetupScore == expected[i].Score, "every role retains objective-only setup score " + expected[i].Key);
            }
            var directSetup = Evaluate(s, objective); var direct = Evaluate(s, reviewed);
            SameSetup(direct, directSetup, "direct numeric support selection");
            Check(direct.SetupScore == directSetup.Score, "direct numeric setup score is objective-only");
            Check(order[1].Length == 0 ? direct.MagicNgu == 0 && direct.Blood == 0
                : order[1][0] == "RIT" ? direct.Blood > 0 && direct.MagicNgu == 0 : direct.MagicNgu > 0 && direct.Blood == 0,
                "direct numeric metrics and amounts use actual strict funding after support is fixed");
            var replay = LoadoutPresetPlanner.Replay(s, reviewed, plan);
            SameReferences(replay, setup, "reviewed exact replay");
            Check(replay.Sequence == null && replay.Candidates.Concat(replay.Specialists).Select(c => c.SetupScore)
                .SequenceEqual(actual.Select(c => c.SetupScore)), "exact replay preserves all setup ranks without a stale sequence");
        }
        Check(r.ResourcePrioritiesReviewed && r.Weights.SequenceEqual(savedWeights) && r.EnergyPriorities.SequenceEqual(savedEnergy)
            && r.MagicPriorities.SequenceEqual(savedMagic) && s.Current[0].Id == 40 && s.CurrentDiggers.Length == 0,
            "objective copies, setup search and funding do not mutate caller intent or captured setup");
    }

    private static void PureReferenceRepairChecks()
    {
        var s = ActivitySnapshot(); var r = PriorityRequest(); r.Weights = ActivityRequest(0, 9).Weights;
        s.RequiredATTargets = new long[] { 11, 11 };
        foreach (var bar in s.AdvancedTraining) { bar.Coefficient = 100; bar.LevelExponent = 0; }
        var setup = LoadoutPresetPlanner.Plan(s, ObjectiveOnlyRequest(r));
        Check(setup.ReferenceKeys[0].StartsWith("reference-seed-") && setup.ReferenceProduction[0] > 0,
            "pure reference fixture requires original-objective repair, not a feasible NGU specialist");
        foreach (var energy in new[] { new[] { "AT", "NGU" }, new[] { "NGU", "AT" } })
        {
            r.EnergyPriorities = energy; r.MagicPriorities = new string[0];
            var plan = LoadoutPresetPlanner.Plan(s, r);
            SameReferences(plan, setup, "reviewed mandatory-AT reference repair");
            SameSetup(plan.Candidates[1], setup.Candidates[1], "reference-repaired mixed setup");
            Check(plan.Candidates[1].SetupScore == setup.Candidates[1].Score,
                "actual AT funding failure cannot redefine the repaired setup score");
            Check(plan.Candidates[1].Feasible == (energy[0] == "AT"), "mandatory AT still requires actual residual funding");
        }
        r.EnergyPriorities = new[] { "AT", "NGU" }; r.ATWeights = new[] { 1, 0, 0, 0, 0 };
        var excluded = Evaluate(s, r);
        Check(!excluded.Feasible && excluded.Activities.AT[1] == 0 && excluded.Score == null,
            "reviewed AT token cannot override an excluded unfinished required stat ratio");
    }

    private static void PureSetupFailureChecks()
    {
        var s = ActivitySnapshot(); s.Available = Enumerable.Repeat(true, 14).ToArray(); s.PpPerKill = new double[0];
        var best = new PresetItem { Id = 201, SourceSlot = 201, Group = -5 }; best.Stats[0] = 1000;
        var rescue = new PresetItem { Id = 202, SourceSlot = 202, Group = -5 }; rescue.ResourceSpecs[(int)ResourceSpec.NGU] = 1e12f;
        s.Items = s.Current.Concat(new[] { best, rescue }).ToArray(); s.RequiredATTargets = new long[] { 11, 11 };
        foreach (var bar in s.AdvancedTraining) { bar.Coefficient = 100; bar.LevelExponent = 0; }
        var r = PriorityRequest(); r.Weights = ActivityRequest(9, 10).Weights; r.Weights[10] = 100000;
        r.EnergyPriorities = new[] { "NGU", "AT" }; r.MagicPriorities = new string[0];
        var setup = LoadoutPresetPlanner.Plan(s, ObjectiveOnlyRequest(r)); var plan = LoadoutPresetPlanner.Plan(s, r);
        Check(setup.Candidates[1].Items[0].Id == best.Id && setup.Candidates[1].Feasible,
            "funding failure fixture has a feasible objective-best setup");
        SameSetup(plan.Candidates[1], setup.Candidates[1], "funding-infeasible setup winner");
        Check(!plan.Candidates[1].Feasible && plan.Candidates[1].Score == null && plan.Candidates[1].SetupScore == setup.Candidates[1].Score,
            "failed funding blocks objective-best setup without replacing it or nulling its setup rank");
        var alternative = LoadoutPresetPlanner.Evaluate(s, r, new[] { rescue }, LoadoutPresetPlanner.NormalizeWeights(r.Weights),
            plan.Normalization, "rescue", "Rescue");
        Check(alternative.Feasible && alternative.Activities.AT.Take(2).All(x => x > 0)
            && alternative.SetupScore < plan.Candidates[1].SetupScore,
            "a lower-setup-rank gear alternative really could fund AT but is not substituted");
    }

    private static void ZeroObjectiveFundingChecks()
    {
        var s = ActivitySnapshot(); s.Available = Enumerable.Repeat(true, 14).ToArray(); s.PpPerKill = new double[0];
        foreach (var row in s.EnergyNgu.Concat(s.MagicNgu)) { row.Coefficient = 100; row.Target = row.Level + 1; }
        var excluded = new PresetNgu { Coefficient = 100, HardCap = 1000000000, Weight = 0 };
        s.EnergyNgu = s.EnergyNgu.Concat(new[] { excluded }).ToArray(); s.MagicNgu = s.MagicNgu.Concat(new[] { excluded }).ToArray();
        foreach (var bar in new[] { s.TimeMachineEnergy, s.TimeMachineMagic, s.WandoosEnergy, s.WandoosMagic }
            .Concat(s.AdvancedTraining).Concat(s.Augments.SelectMany(x => new[] { x.Augment, x.Upgrade })))
        { bar.Coefficient = 100; bar.LevelExponent = 0; }
        s.Rituals[0].Coefficient = .01;
        var r = PriorityRequest(); r.Weights = ActivityRequest(12, 13).Weights;
        var c = Evaluate(s, r); var w = LoadoutPresetPlanner.NormalizeWeights(r.Weights); var norms = Enumerable.Repeat(1d, 14).ToArray();
        Check(c.Feasible && new[] { 0, 1, 2, 6, 7, 8, 9 }.All(g => c.Metrics[g] > 0),
            "every supported resource activity produces real metrics with zero objective importance");
        Check(c.Energy == 1 && c.MagicNgu == 1 && c.Blood == 101 && c.Activities.TimeMachineEnergy == 1
            && c.Activities.TimeMachineMagic == 1 && c.Activities.WandoosEnergy == 1 && c.Activities.WandoosMagic == 1
            && c.Activities.AugmentEnergy == 1 && c.Activities.UpgradeEnergy == 1
            && c.Activities.AT.SequenceEqual(new long[] { 1, 1, 0, 0, 0 }), "zero-weight activities receive only exact useful caps");
        Check(c.EnergyShares.SequenceEqual(new long[] { 1, 0 }) && c.MagicShares.SequenceEqual(new long[] { 1, 0 }),
            "explicit zero-objective NGU intent never overrides Misc NGU zero-ratio exclusions");
        var withoutFallbackUtility = c.Metrics.ToArray();
        for (var i = 0; i < 10; i++) withoutFallbackUtility[i] = 0;
        Check(c.Score == 1000 && c.Score == LoadoutPresetPlanner.Score(withoutFallbackUtility, w, norms, true)
            && c.SetupScore == c.Score, "positive zero-weight funded metrics add exactly zero objective contribution");
        var legacy = Evaluate(s, ObjectiveOnlyRequest(r));
        Check(legacy.PriorityFunding == null && legacy.SetupScore == null && legacy.Activities == null,
            "unreviewed capacity evaluation keeps the existing generic numeric path");
    }

    private static long ActivityFunding(PresetCandidate c, string token, bool energy)
    {
        if (token == "NGU") return energy ? c.Energy : c.MagicNgu;
        if (token == "RIT") return c.Blood;
        if (token == "TM") return energy ? c.Activities.TimeMachineEnergy : c.Activities.TimeMachineMagic;
        if (token == "WAN") return energy ? c.Activities.WandoosEnergy : c.Activities.WandoosMagic;
        if (token == "AUG") return c.Activities.AugmentEnergy + c.Activities.UpgradeEnergy;
        return c.Activities.AT.Sum();
    }

    private static void ResidualActivityChecks()
    {
        foreach (var energy in new[] { true, false })
        foreach (var token in energy ? new[] { "NGU", "TM", "AUG", "WAN", "AT" } : new[] { "NGU", "TM", "WAN", "RIT" })
        {
            var s = ActivitySnapshot(); s.Available = Enumerable.Repeat(true, 14).ToArray(); s.PpPerKill = new double[0]; s.Energy = s.Magic = 3;
            s.WandoosEnergy.Coefficient = s.WandoosMagic.Coefficient = .001;
            var first = token == "TM" ? "WAN" : "TM";
            var bar = first == "TM" ? energy ? s.TimeMachineEnergy : s.TimeMachineMagic : energy ? s.WandoosEnergy : s.WandoosMagic;
            bar.Coefficient = 100; bar.LevelExponent = 0;
            var r = PriorityRequest(); r.Weights = ActivityRequest(12).Weights;
            r.EnergyPriorities = energy ? new[] { first, token } : new string[0];
            r.MagicPriorities = energy ? new string[0] : new[] { first, token };
            var c = Evaluate(s, r); var name = (energy ? "Energy " : "Magic ") + token;
            Check(c.Feasible && ActivityFunding(c, first, energy) == 1 && ActivityFunding(c, token, energy) == 2,
                name + " zero-objective fallback receives only the residual after the earlier useful cap");
            Check((energy ? c.Energy + c.Activities.EnergyTotal : c.MagicNgu + c.Blood + c.Activities.MagicTotal) == 3,
                name + " exact residual conservation");
            s.Energy = s.Magic = 1;
            var preempted = Evaluate(s, r);
            Check(ActivityFunding(preempted, first, energy) == 1 && ActivityFunding(preempted, token, energy) == 0,
                name + " receives no funding while the earlier activity needs the whole pool");
            s.Energy = s.Magic = 3;
            var goal = token == "NGU" ? energy ? 0 : 1 : token == "RIT" ? 2 : token == "TM" ? 6 : token == "AUG" ? 7 : token == "WAN" ? 8 : 9;
            s.Available[goal] = false;
            var unavailable = Evaluate(s, r);
            Check(unavailable.Feasible && ActivityFunding(unavailable, first, energy) == 1 && ActivityFunding(unavailable, token, energy) == 0,
                name + " reviewed zero-objective token cannot bypass native availability");
        }
    }

    private static void ReviewedControlChecks()
    {
        var s = ActivitySnapshot(); s.Available = Enumerable.Repeat(true, 14).ToArray(); s.PpPerKill = new double[0];
        var r = PriorityRequest(); r.Weights = ActivityRequest(10).Weights;
        r.EnergyPriorities = r.MagicPriorities = new string[0];
        var empty = Evaluate(s, r);
        Check(!empty.ControlsEnergy && !empty.ControlsMagic && empty.Energy == 0 && empty.MagicNgu == 0 && empty.Blood == 0,
            "no-objective reviewed-empty lanes remain uncontrolled and receive no implicit fallback");
        r.EnergyPriorities = new[] { "AUG" }; r.MagicPriorities = new[] { "RIT" };
        s.Available[7] = false; s.Rituals[0].Unlocked = false;
        var dormant = Evaluate(s, r);
        Check(dormant.ControlsEnergy && dormant.ControlsMagic && dormant.Activities.EnergyTotal == 0 && dormant.Blood == 0,
            "explicit zero-objective intents control their lanes even while native activity gates are unavailable");
        r.Weights = ActivityRequest(0, 1).Weights; r.EnergyPriorities = r.MagicPriorities = new string[0];
        var owned = Evaluate(s, r);
        Check(owned.ControlsEnergy && owned.ControlsMagic && owned.Energy == 0 && owned.MagicNgu == 0 && owned.Score == 0 && owned.SetupScore > 0,
            "objective-owned reviewed-empty lanes stay controlled-empty with actual zero production");
        r.Weights = ActivityRequest(1).Weights;
        var oneLane = Evaluate(s, r);
        Check(!oneLane.ControlsEnergy && oneLane.ControlsMagic, "empty-lane ownership remains independent between Energy and Magic");
    }

    private static void ReviewedCapacityAdvanceChecks()
    {
        var s = ActivitySnapshot(); s.Available = Enumerable.Repeat(true, 14).ToArray(); s.PpPerKill = new double[0];
        var r = PriorityRequest(); r.Weights = ActivityRequest(12).Weights;
        r.EnergyPriorities = new[] { "NGU" }; r.MagicPriorities = new string[0];
        var plan = LoadoutPresetPlanner.Plan(s, r);
        s.NguAutoAdvance = true; s.EnergyNgu[0].Target = s.EnergyNgu[0].Level + 1;
        Rejected(() => LoadoutPresetPlanner.Plan(s, r), "auto-advance", "reviewed cap-only Plan guards explicit zero-objective NGU funding");
        Rejected(() => Evaluate(s, r), "auto-advance", "reviewed cap-only direct evaluation guards NGU funding");
        Rejected(() => LoadoutPresetPlanner.Replay(s, r, plan), "auto-advance", "reviewed cap-only exact replay guards NGU funding");
        Rejected(() => RefreshExecution(s, r, plan.Candidates[1]), "auto-advance", "reviewed cap-only funding refresh guards NGU funding");
        r.EnergyPriorities = new string[0];
        Check(LoadoutPresetPlanner.Plan(s, r).Candidates[1].Feasible,
            "cap-only setup with no NGU intent still bypasses irrelevant native NGU auto-advance forecasting");
    }

    private static PresetSnapshot ScarceGoldTmSnapshot()
    {
        var s = ActivitySnapshot(); s.Magic = 0; s.Gold = 10;
        s.TimeMachineEnergy = ActivityBar(.00001, 0);
        s.TimeMachineEnergy.LevelExponent = 0; s.TimeMachineEnergy.CostCoefficient = 1;
        return s;
    }

    private static PresetRequest ScarceGoldTmRequest()
    {
        var r = PriorityRequest(); r.Weights = ActivityRequest(6).Weights;
        r.EnergyPriorities = new[] { "TM", "NGU" }; r.MagicPriorities = new string[0];
        return r;
    }

    private static void ScarceGoldSetupChecks()
    {
        var s = ScarceGoldTmSnapshot(); var r = ScarceGoldTmRequest();
        var legacyRequest = r.Copy(); legacyRequest.ResourcePrioritiesReviewed = false;
        legacyRequest.EnergyPriorities = legacyRequest.MagicPriorities = null;
        var legacy = Evaluate(s, legacyRequest);
        Check(!legacy.Feasible && legacy.Score == null && legacy.ActivityGoldSpent > s.Gold && legacy.Metrics[6] > 0,
            "scarce-gold TM regression reproduces the unaffordable full proportional provisional allocation");
        var c = Evaluate(s, r);
        Check(c.Feasible && c.SetupScore > 0 && c.Score > 0 && c.Activities.TimeMachineEnergy == 4
            && c.ActivityGoldSpent == 10 && c.Metrics[6] == .001,
            "reviewed TM retains usable setup utility and funds four affordable units producing one real level with ten gold");
        var plan = LoadoutPresetPlanner.Plan(s, r);
        Check(plan.Candidates.All(x => x.Feasible && x.SetupScore > 0 && x.Score > 0 && x.ActivityGoldSpent == 10)
            && plan.ReferenceProduction[6] == .001 && plan.Normalization[6] == .001,
            "scarce-gold Plan freezes a measured affordable TM reference and usable setup scores");
        var preempted = r.Copy(); preempted.EnergyPriorities = new[] { "NGU", "TM" };
        var alternate = LoadoutPresetPlanner.Plan(s, preempted);
        SameReferences(alternate, plan, "scarce-gold fallback reorder");
        SameSetup(alternate.Candidates[1], plan.Candidates[1], "scarce-gold fallback reorder");
        Check(alternate.Candidates[1].SetupScore == plan.Candidates[1].SetupScore && alternate.Candidates[1].Score == 0
            && alternate.Candidates[1].Metrics[6] == 0, "scarce-gold setup rank never falls back to preempted funded production score");
        Check(s.Gold == 10 && s.Magic == 0 && r.EnergyPriorities.SequenceEqual(new[] { "TM", "NGU" }),
            "affordable setup scoring leaves snapshot gold, pools and explicit intent unchanged");
        var shared = ScarceGoldTmSnapshot(); shared.Magic = 1000;
        shared.TimeMachineMagic = ActivityBar(.00001, 0); shared.TimeMachineMagic.LevelExponent = 0;
        shared.TimeMachineMagic.CostCoefficient = 1;
        var both = r.Copy(); both.MagicPriorities = new[] { "TM" };
        var sharedCandidate = Evaluate(shared, both);
        Check(sharedCandidate.Feasible && sharedCandidate.SetupScore > 0 && sharedCandidate.Metrics[6] == .001
            && sharedCandidate.Activities.TimeMachineEnergy == 4 && sharedCandidate.Activities.TimeMachineMagic == 0
            && sharedCandidate.ActivityGoldSpent == 10,
            "objective-only and strict TM both share one ten-gold budget across Energy and Magic rather than spending it twice");
    }

    private static void ScarceGoldSupportChecks()
    {
        var s = ScarceGoldTmSnapshot(); s.Magic = 1000; s.Gold = 12; s.TimeMachineMagic.Unlocked = false;
        s.DiggerSlots = 1;
        s.Diggers = new[] { 5, 10 }.Select(id => new SupportDigger { Id = id, Maximum = 1, PerLevel = 10,
            BaseDrain = 10, Growth = 2, ConsumesGold = true }).ToArray();
        var r = ScarceGoldTmRequest(); r.PlannedSupport = true; r.Weights[1] = 9;
        r.MagicPriorities = new[] { "NGU", "RIT" };
        var baseline = Evaluate(s, r);
        var demand = r.Copy(); demand.MagicPriorities = new[] { "RIT", "TM", "NGU" };
        var blood = Evaluate(s, demand);
        Check(baseline.Feasible && blood.Feasible && baseline.Support.Diggers.Single().Id == 5
            && blood.Support.Diggers.Single().Id == 5 && baseline.SetupScore > 0 && baseline.SetupScore == blood.SetupScore,
            "direct shared-gold evaluation chooses objective-only Magic support before either strict funding chain");
        Check(baseline.Activities.TimeMachineEnergy == 4 && blood.Activities.TimeMachineEnergy == 4
            && baseline.ActivityGoldSpent == 10 && blood.ActivityGoldSpent == 10 && blood.Blood > 0 && blood.MagicNgu == 0
            && baseline.Blood == 0 && baseline.MagicNgu == 1000 && baseline.Score > blood.Score,
            "strict TM and zero-objective ritual share twelve gold without feeding their funded scores back into support selection");
        SameSetup(blood, baseline, "shared-gold direct support invariance");
        var exactlySpent = s.ActivityCopy(); exactlySpent.Gold = 10;
        var unavailableStartup = Evaluate(exactlySpent, demand);
        Check(unavailableStartup.Feasible && unavailableStartup.Blood == 0 && unavailableStartup.MagicNgu == 1000
            && unavailableStartup.SetupScore > 0 && unavailableStartup.Support.Diggers.Single().Id == 5,
            "spending all liquid gold on affordable TM releases unpaid ritual demand without invalidating setup utility");
    }

    private static void ScarceGoldHardConstraintChecks()
    {
        foreach (var constraint in new[] { "power", "toughness", "basic", "gear", "support", "route", "unavailable", "forecast" })
        {
            var s = ScarceGoldTmSnapshot(); var r = ScarceGoldTmRequest(); var items = s.Current;
            if (constraint == "power") r.MinimumPower = 2000;
            if (constraint == "toughness") r.MinimumToughness = 2000;
            if (constraint == "basic") s.ReservedEnergy = 1001;
            if (constraint == "gear") items = new[] { new PresetItem { Id = 900, SourceSlot = 900, Group = -1 } };
            if (constraint == "support")
            {
                s.DiggerSlots = 1;
                s.Diggers = s.CurrentDiggers = new[] { new SupportDigger { Id = 5, Level = 1, Maximum = 1,
                    BaseDrain = 2000, Growth = 2, ConsumesGold = true } };
            }
            if (constraint == "route")
            {
                r.Weights[4] = 1; r.Zone = 0; s.Available[4] = true;
                s.Zones = new[] { new PresetZone { Id = 0, IdlePower = 1e9, IdleToughness = 1e9, OneHitPower = 1e9 } };
            }
            if (constraint == "unavailable") s.Available[6] = false;
            if (constraint == "forecast") s.TimeMachineEnergy.LevelExponent = 2;
            var c = LoadoutPresetPlanner.Evaluate(s, r, items, LoadoutPresetPlanner.NormalizeWeights(r.Weights),
                Enumerable.Repeat(1d, 14).ToArray(), "hard", "Hard");
            Check(!c.Feasible && c.SetupScore == null && c.Score == null,
                "affordable setup scoring cannot waive hard eligibility or invent missing objective utility: " + constraint);
        }
    }
}
