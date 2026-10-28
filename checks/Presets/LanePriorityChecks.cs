using System;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    private static PresetRequest PriorityRequest()
    {
        var r = ActivityRequest(0, 1, 2);
        r.ResourcePrioritiesReviewed = true;
        r.EnergyPriorities = new[] { "NGU", "TM", "AUG", "WAN", "AT" };
        r.MagicPriorities = new[] { "RIT", "NGU", "TM", "WAN" };
        r.BloodPercent = 0;
        return r;
    }

    static partial void LanePriorityChecks()
    {
        var before = assertions;
        LaneResolverChecks(); LanePreemptionChecks(); LaneActivityCapChecks(); LaneUtilityChecks(); LaneReplayChecks();
        LaneRitualStartupMarginChecks();
        PurePlannerChecks();
        PresetExecutionChecks();
        Console.WriteLine("Lane priority numeric checks: " + (assertions - before));
    }

    private static void LaneResolverChecks()
    {
        foreach (var budget in new[] { 0L, 1, 7, 1000, long.MaxValue })
        {
            var amounts = new long[3];
            var idle = ResourceLanePriority.Resolve(budget, new[] { "first", "disabled", "last" }, (token, remaining) => {
                var i = token == "first" ? 0 : token == "disabled" ? 1 : 2;
                return amounts[i] = i == 0 ? Math.Min(7, remaining) : i == 1 ? 0 : remaining;
            });
            Check(amounts[0] == Math.Min(budget, 7) && amounts[1] == 0 && amounts[2] == budget - amounts[0]
                && checked(amounts.Sum() + idle) == budget, "strict resolver exact cap spill and disabled skip " + budget);
        }
        Check(ResourceLanePriority.Resolve(long.MaxValue, new string[0], (token, remaining) => remaining) == long.MaxValue,
            "explicit empty lane leaves the whole exact pool idle");
        Rejected(() => ResourceLanePriority.Resolve(1, new[] { "bad" }, (token, remaining) => 2), "budget", "resolver rejects invalid overfunding callback");
    }

    private static void LanePreemptionChecks()
    {
        var s = ResourceDemandSnapshot(); var r = PriorityRequest();
        s.Rituals = new[] { s.Rituals[0] };
        var preempted = Evaluate(s, r);
        Check(preempted.Feasible && preempted.Blood == 1000 && preempted.MagicNgu == 0 && preempted.Metrics[1] == 0
            && preempted.MagicShares.SequenceEqual(new long[3]), "higher blood demand consumes entire magic and zeros lower NGU utility despite positive saved weight");
        Check(preempted.Energy == 1000 && preempted.Metrics[0] > 0, "magic preemption leaves the independent energy lane funded");
        s.Rituals[0].Coefficient = .01;
        var spill = Evaluate(s, r);
        Check(spill.Blood == 101 && spill.MagicNgu == 899 && spill.MagicShares.SequenceEqual(new long[] { 225, 674, 0 })
            && spill.Metrics[1] > 0 && checked(spill.Blood + spill.MagicNgu + spill.Activities.MagicTotal) == 1000,
            "useful blood cap releases exact overflow with global NGU ratios and zero exclusions");
        Near(spill.Metrics[0].Value, preempted.Metrics[0].Value, "energy production is invariant under magic cap changes");
        s.Rituals[0].Unlocked = false;
        var locked = Evaluate(s, r);
        Check(locked.Feasible && locked.Blood == 0 && locked.MagicNgu == 1000, "locked higher priority releases the whole magic lane");
        s.Rituals[0].Unlocked = true; s.Gold = 0;
        Check(Evaluate(s, r).MagicNgu == 1000, "unpaid unaffordable ritual cannot steal lower-priority funding");
        s.Rituals[0].Paid = true;
        Check(Evaluate(s, r).Blood == 101, "already-paid ritual runs without a second startup charge");
        s.Gold = 1000; s.Rituals[0].Paid = false;
        r.MagicPriorities = new[] { "NGU" };
        var released = Evaluate(s, r);
        Check(released.Blood == 0 && released.MagicNgu == 1000 && released.MagicShares.SequenceEqual(new long[] { 250, 750, 0 }),
            "removing temporary higher demand recomputes current baseline rather than restoring stale amounts");
        r.MagicPriorities = new[] { "RIT", "NGU" }; r.Weights[2] = 0;
        Check(Evaluate(s, r).Blood == 101 && Evaluate(s, r).MagicNgu == 899,
            "explicit reviewed ritual token authorizes funding independently of its zero objective weight");
        r.Weights[2] = 1; r.ResourcePrioritiesReviewed = false;
        var unreviewed = Evaluate(s, r);
        Check(unreviewed.Blood == 0 && unreviewed.MagicNgu == 1000, "unreviewed proposal retains legacy fixed magic fraction");
        r.ResourcePrioritiesReviewed = true; r.EnergyPriorities = new string[0]; r.MagicPriorities = new string[0];
        var empty = Evaluate(s, r);
        Check(empty.Feasible && empty.Energy == 0 && empty.MagicNgu == 0 && empty.Blood == 0 && empty.Score == 0,
            "reviewed empty means controlled-empty, never an implicit NGU fallback");
        r = PriorityRequest(); s.MagicNgu[0].Weight = s.MagicNgu[1].Weight = 0;
        var excluded = Evaluate(s, r);
        Check(excluded.Blood == 101 && excluded.MagicNgu == 0, "all-zero within-NGU exclusions leave surplus idle");
        s.MagicNgu[0].Weight = 1; s.MagicNgu[0].Level = 0; s.MagicNgu[0].Target = 1; s.MagicNgu[0].Coefficient = 1;
        Check(Evaluate(s, r).MagicNgu == 2, "finite NGU cap receives only useful exact demand");
        s.MagicNgu[0].Level = 1;
        Check(Evaluate(s, r).MagicNgu == 0, "completed finite NGU target receives no funding");
        s = ResourceDemandSnapshot(); r = PriorityRequest();
        s.Rituals = new[] { s.Rituals[1] };
        s.Energy = s.Magic = long.MaxValue; s.EnergyCapacity.BaseCap = s.MagicCapacity.BaseCap = long.MaxValue;
        foreach (var row in s.EnergyNgu.Concat(s.MagicNgu)) row.Coefficient = 1e-20;
        var exact = Evaluate(s, r);
        Check(exact.Energy == long.MaxValue && exact.Blood == 101 && exact.MagicNgu == long.MaxValue - 101
            && checked(exact.Blood + exact.MagicNgu) == long.MaxValue
            && exact.MagicShares.SequenceEqual(new long[] { 2305843009213693927, 6917529027641081779, 0 }),
            "reviewed candidate funding conserves every integer in independent Int64-sized pools");
    }

    private static void LaneActivityCapChecks()
    {
        var s = ActivitySnapshot(); var r = PriorityRequest(); r.Weights = Enumerable.Repeat(1d, 14).ToArray();
        s.Available = Enumerable.Repeat(true, 14).ToArray();
        s.Zones = new[] { new PresetZone { Id = 0, IdlePower = 1, IdleToughness = 1, OneHitPower = 1,
            NormalRatio = 1, BoostValues = new[] { 1d }, Chances = new[] { 1d }, Caps = new[] { 1d } } };
        s.PpPerKill = Enumerable.Repeat(1d, 100).ToArray();
        s.TimeMachineEnergy.Coefficient = s.TimeMachineMagic.Coefficient = 100;
        s.TimeMachineEnergy.LevelExponent = s.TimeMachineMagic.LevelExponent = 0;
        s.WandoosEnergy.Coefficient = s.WandoosMagic.Coefficient = 100;
        foreach (var bar in s.AdvancedTraining) { bar.Coefficient = 100; bar.LevelExponent = 0; }
        foreach (var tier in s.Augments) foreach (var bar in new[] { tier.Augment, tier.Upgrade })
        { bar.Coefficient = 100; bar.LevelExponent = 0; }
        s.Rituals[0].Coefficient = .01;
        r.EnergyPriorities = new[] { "TM", "AUG", "WAN", "AT", "NGU" };
        r.MagicPriorities = new[] { "TM", "WAN", "RIT", "NGU" };
        var c = Evaluate(s, r);
        Check(c.Feasible && c.Score.HasValue && c.Metrics.Length == 14 && c.Metrics.All(x => x.HasValue),
            "all fourteen production, adventure and capacity objectives coexist with strict lanes");
        Check(c.Activities.TimeMachineEnergy == 1 && c.Activities.TimeMachineMagic == 1 && c.Activities.WandoosEnergy == 1
            && c.Activities.WandoosMagic == 1 && c.Activities.AugmentEnergy == 1 && c.Activities.UpgradeEnergy == 1
            && c.Activities.AT.SequenceEqual(new long[] { 1, 1, 0, 0, 0 }), "productive activity caps spill in strict order using installed OS and AT exclusions");
        Check(c.Energy + c.Activities.EnergyTotal == 1000 && c.MagicNgu + c.Blood + c.Activities.MagicTotal == 1000
            && c.Activities.Os == s.WandoosOs && c.Metrics[12] == 1000 && c.Metrics[13] == 1000, "full-objective exact accounting and instant capacities remain independent");
        s.TimeMachineEnergy.Target = s.TimeMachineEnergy.Level;
        var completed = Evaluate(s, r);
        Check(completed.Activities.TimeMachineEnergy == 0 && completed.Energy == c.Energy + 1, "completed activity releases its exact cap to lower priorities");
        s.RequiredATTargets = new long[] { 11, 11 }; r.EnergyPriorities = new[] { "NGU", "AT" };
        Check(!Evaluate(s, r).Feasible, "mandatory unfinished AT cannot be silently bypassed by a higher uncapped NGU");
        r.EnergyPriorities = new[] { "AT", "NGU" };
        Check(Evaluate(s, r).Feasible && Evaluate(s, r).Activities.AT.Take(2).All(x => x > 0), "mandatory AT is funded when its hard gate can be satisfied");
        s.Energy = 1;
        Check(!Evaluate(s, r).Feasible, "scarce pool cannot manufacture mandatory funding on two AT rows");
        s.AdvancedTraining = new PresetActivityBar[0];
        Check(!Evaluate(s, r).Feasible, "missing mandatory native AT rows block safely instead of bypassing or indexing absent captures");
        s = ActivitySnapshot(); r = PriorityRequest(); r.Weights[6] = 1; r.EnergyPriorities = new[] { "TM", "NGU" };
        s.TimeMachineEnergy.CostCoefficient = 1e9; s.Gold = 1;
        var unaffordable = Evaluate(s, r);
        Check(unaffordable.Feasible && unaffordable.Activities.TimeMachineEnergy == 0 && unaffordable.Energy > 0,
            "unaffordable activity yields real energy overflow without inventing future income");
        s = ActivitySnapshot(); r = PriorityRequest(); r.Weights[6] = 1; r.EnergyPriorities = new[] { "TM", "NGU" };
        s.TimeMachineEnergy.Coefficient = 100; s.TimeMachineEnergy.LevelExponent = 0;
        s.TimeMachineEnergy.Target = 11; s.TimeMachineEnergy.CostCoefficient = 1;
        s.Rituals[0].Coefficient = .01; s.Gold = 51;
        var exactStartup = Evaluate(s, r);
        Check(exactStartup.ActivityGoldSpent == 50 && exactStartup.Blood == 0 && exactStartup.MagicNgu == 1000,
            "liquid activity spending cannot double-fund unpaid blood at its strict exact startup boundary");
        s.Gold = 52;
        Check(Evaluate(s, r).Blood == 101 && Evaluate(s, r).Feasible,
            "one real surplus gold above unpaid ritual startup makes its useful cap eligible");
        s.Gold = 50; s.Rituals[0].Paid = true;
        Check(Evaluate(s, r).Blood == 101 && Evaluate(s, r).Feasible,
            "paid blood requires no extra liquid gold after an earlier lane spends the available balance");
    }

    private static void LaneUtilityChecks()
    {
        var s = ResourceDemandSnapshot(); var r = PriorityRequest(); r.Weights[0] = 0; r.Weights[1] = 1000000;
        r.EnergyPriorities = new string[0]; s.Rituals = new[] { s.Rituals[0] };
        var gear = new PresetItem { Id = 90, SourceSlot = 90, Group = -5, Name = "NGU-only" };
        gear.ResourceSpecs[(int)ResourceSpec.NGU] = 1e6f;
        var weights = LoadoutPresetPlanner.NormalizeWeights(r.Weights); var norms = Enumerable.Repeat(1d, 14).ToArray();
        var baseline = Evaluate(s, r);
        var nguGear = LoadoutPresetPlanner.Evaluate(s, r, new[] { gear }, weights, norms, "ngu", "NGU-only");
        Check(nguGear.MagicNgu == 0 && nguGear.Metrics[1] == 0 && nguGear.Score == baseline.Score,
            "unfunded Magic NGU gives NGU-specific gear zero utility despite enormous objective importance");
        var digger = new SupportDigger { Id = 5, Maximum = 1, Level = 1, PerLevel = 100 };
        s.Diggers = s.CurrentDiggers = new[] { digger }; s.DiggerSlots = 1;
        Check(Evaluate(s, r).Score == baseline.Score && Evaluate(s, r).Metrics[1] == 0,
            "unfunded Magic NGU gives its current support multiplier zero utility");
        s.NguFactors = Enumerable.Repeat(1f, 6).ToArray(); s.BeardSlots = 1; s.UnlockedBeards = s.CurrentBeards = new[] { 3 };
        s.BeardTemporary[0] = 100;
        Check(Evaluate(s, r).Metrics[1] == 0 && Evaluate(s, r).Score == baseline.Score,
            "unfunded Magic NGU gives its beard multiplier zero utility when Energy NGU is excluded");
        s.CurrentDiggers = new SupportDigger[0];
        s.Diggers = new[] { new SupportDigger { Id = 5, Maximum = 1, BaseDrain = 10, Growth = 2, ConsumesGold = true, PerLevel = 100 },
            new SupportDigger { Id = 10, Maximum = 1, BaseDrain = 10, Growth = 2, ConsumesGold = true, PerLevel = 10 } };
        r.PlannedSupport = true;
        var supportedBlood = Evaluate(s, r);
        Check(supportedBlood.MagicNgu == 0 && supportedBlood.Support.Diggers.Single().Id == 5
            && supportedBlood.SetupScore > baseline.SetupScore,
            "planned support follows the Magic NGU setup objective even when reviewed funding preempts it with blood");
        r.PlannedSupport = false;
        s.Diggers = new[] { digger };
        s.CurrentDiggers = new SupportDigger[0]; s.CurrentBeards = new int[0];
        var creatingOverflow = s.Items[2];
        Stat(creatingOverflow, PresetStat.MagicPower, 1900);
        var prospective = LoadoutPresetPlanner.Evaluate(s, r, new[] { creatingOverflow }, weights, norms, "prospective", "Prospective");
        Check(prospective.Blood < 1000 && prospective.MagicNgu > 0 && prospective.Metrics[1] > 0 && prospective.Score > baseline.Score,
            "candidate power is projected before funding so a setup can create genuine overflow from a zero-funded baseline");
        s.Rituals[0].Coefficient = .01;
        baseline = Evaluate(s, r);
        nguGear = LoadoutPresetPlanner.Evaluate(s, r, new[] { gear }, weights, norms, "ngu", "NGU-only");
        Check(nguGear.MagicNgu > 0 && nguGear.Metrics[1] > baseline.Metrics[1] && nguGear.Score > baseline.Score,
            "real overflow restores NGU-specific gear utility");
        r.PlannedSupport = true; r.DiggerWeights = new double[12]; r.DiggerWeights[5] = 1;
        Check(Evaluate(s, r).Support.Diggers.Any(x => x.Id == 5) && Evaluate(s, r).Metrics[1] > baseline.Metrics[1],
            "prospectively funded Magic NGU restores support utility");
        var first = LoadoutPresetPlanner.Plan(s, r).Candidates[1];
        var second = LoadoutPresetPlanner.Plan(s, r).Candidates[1];
        Check(first.Items.Select(x => x.Id).SequenceEqual(second.Items.Select(x => x.Id)) && first.Blood == second.Blood
            && first.MagicNgu == second.MagicNgu && first.Support.Diggers.Select(x => x.Id).SequenceEqual(second.Support.Diggers.Select(x => x.Id)),
            "prospective selection and allocation converge deterministically without incumbent-dependent utility");
        var applied = s.ActivityCopy(); applied.Current = first.Items; applied.CurrentBeards = first.Support.Beards;
        applied.CurrentDiggers = first.Support.Diggers;
        var afterApply = LoadoutPresetPlanner.Plan(applied, r).Candidates[1];
        Check(afterApply.Items.Select(x => x.Id).SequenceEqual(first.Items.Select(x => x.Id))
            && afterApply.Blood == first.Blood && afterApply.MagicNgu == first.MagicNgu
            && afterApply.Support.Diggers.Select(x => x.Id + ":" + x.Level).SequenceEqual(first.Support.Diggers.Select(x => x.Id + ":" + x.Level)),
            "observing the selected gear and support after apply does not reverse the prospective lane decision");
    }

    private static PresetSnapshot RitualMarginSnapshot(double ritualCost, double tmCost)
    {
        var s = ActivitySnapshot();
        s.Rituals[0].Coefficient = .01; s.Rituals[0].Cost = ritualCost;
        foreach (var bar in new[] { s.TimeMachineEnergy, s.TimeMachineMagic })
        { bar.Coefficient = 100; bar.LevelExponent = 0; bar.Target = 11; bar.CostCoefficient = tmCost / 50; }
        return s;
    }

    private static void LaneRitualStartupMarginChecks()
    {
        var r = PriorityRequest(); r.Weights = ActivityRequest(2).Weights; r.BloodPercent = -1;
        r.EnergyPriorities = new string[0]; r.MagicPriorities = new[] { "RIT", "TM" };
        var s = RitualMarginSnapshot(1, 50); s.Gold = 51;
        var boundary = Evaluate(s, r);
        Check(boundary.Feasible && boundary.SetupEligible == true && boundary.SetupScore == 500 && boundary.Score == 500
            && boundary.Blood == 101 && boundary.Activities.TimeMachineMagic == 0 && boundary.ActivityGoldSpent == 0,
            "unpaid higher-priority RIT keeps its strict startup margin instead of failing when optional TM would spend the remaining fifty gold");
        var ritualOnly = r.Copy(); ritualOnly.MagicPriorities = new[] { "RIT" };
        var only = Evaluate(s, ritualOnly);
        Check(only.Feasible && only.Blood == boundary.Blood && only.SetupScore == boundary.SetupScore && only.Score == boundary.Score,
            "adding unaffordable optional TM cannot invalidate or reduce the earlier ritual's funded production");
        var plan = LoadoutPresetPlanner.Plan(s, r); var onlyPlan = LoadoutPresetPlanner.Plan(s, ritualOnly);
        SameReferences(plan, onlyPlan, "ritual margin optional fallback");
        SameSetup(plan.Candidates[1], onlyPlan.Candidates[1], "ritual margin optional fallback");
        Check(plan.Candidates[1].Feasible && plan.Candidates[1].SetupScore == onlyPlan.Candidates[1].SetupScore
            && plan.Candidates[1].Blood == 101 && plan.Candidates[1].Activities.TimeMachineMagic == 0,
            "strict startup protection leaves objective-only Plan selection and rank independent of the optional TM token");
        s.Gold = 52;
        var surplus = RefreshExecution(s, r, boundary);
        Check(surplus.Feasible && surplus.Blood == 101 && surplus.Activities.TimeMachineMagic == 1 && surplus.ActivityGoldSpent == 50
            && surplus.SetupScore == boundary.SetupScore, "one surplus gold funds optional TM without reacquiring the ritual setup");
        s.Gold = 51;
        var released = RefreshExecution(s, r, surplus);
        Check(released.Feasible && released.Blood == 101 && released.Activities.TimeMachineMagic == 0 && released.SetupScore == boundary.SetupScore,
            "funding refresh removes lower TM at the exact unpaid startup boundary while retaining RIT");
        var pinned = LoadoutPresetPlanner.Evaluate(s, r, surplus.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights),
            Enumerable.Repeat(1d, 14).ToArray(), surplus.Key, surplus.Label, surplus);
        Check(!pinned.Feasible && pinned.SetupScore == surplus.SetupScore,
            "exact replay still rejects previously acquired TM spending that loses the unpaid ritual margin");

        foreach (var pair in new[] { new[] { 1d, 50 }, new[] { .125, .5 }, new[] { .1, .3 } })
        {
            s = RitualMarginSnapshot(pair[0], pair[1]);
            var tmCost = 50 * s.TimeMachineMagic.CostCoefficient;
            var exact = tmCost + s.Rituals[0].Cost;
            foreach (var gold in new[] { Math.BitDecrement(exact), exact, Math.BitIncrement(exact) })
            {
                s.Gold = gold;
                var c = Evaluate(s, r);
                var fits = tmCost < gold - s.Rituals[0].Cost;
                Check(c.Feasible && c.Blood == 101 && c.Activities.TimeMachineMagic == (fits ? 1 : 0)
                    && c.ActivityGoldSpent == (fits ? tmCost : 0) && c.SetupScore == boundary.SetupScore,
                    "unpaid ritual margin uses the exact double bound, including fractional costs and adjacent representable gold: "
                    + pair[0] + "/" + pair[1] + "/" + gold.ToString("R"));
            }
            s.Gold = tmCost; s.Rituals[0].Paid = true;
            var paid = Evaluate(s, r);
            Check(paid.Feasible && paid.Blood == 101 && paid.Activities.TimeMachineMagic == 1 && paid.ActivityGoldSpent == tmCost,
                "paid ritual reserves no liquid gold or strict startup margin: " + pair[0] + "/" + pair[1]);
        }

        s = RitualMarginSnapshot(1, 1); s.Gold = 66;
        s.TimeMachineMagic.Coefficient = .0003; s.TimeMachineMagic.LevelExponent = 1;
        s.TimeMachineMagic.Target = 12; s.TimeMachineMagic.CostCoefficient = 1;
        var reduced = Evaluate(s, r);
        Check(reduced.Feasible && reduced.Blood == 101 && reduced.Activities.TimeMachineMagic == 1
            && reduced.ActivityGoldSpent == 50 && reduced.Metrics[6] == .001,
            "lower TM funding reduces to a cheaper productive amount rather than exhausting the unpaid ritual margin: "
            + reduced.Feasible + "/" + reduced.Blood + "/" + reduced.Activities.TimeMachineMagic + "/" + reduced.ActivityGoldSpent + "/" + reduced.Metrics[6]);

        s = RitualMarginSnapshot(.125, Math.BitDecrement(.25)); s.Gold = 1.125;
        s.TimeMachineEnergy.CostCoefficient = .015;
        var magicCost = 50 * s.TimeMachineMagic.CostCoefficient;
        Check(magicCost < .25 && .75 + magicCost == 1,
            "rounding fixture has lower TM cost below the residual but aggregate activity spending rounds to the strict bound");
        var withEnergy = r.Copy(); withEnergy.EnergyPriorities = new[] { "TM" };
        var rounded = Evaluate(s, withEnergy);
        Check(rounded.Feasible && rounded.Blood == 101 && rounded.Activities.TimeMachineEnergy == 1
            && rounded.Activities.TimeMachineMagic == 0 && rounded.ActivityGoldSpent == .75,
            "unpaid startup protection compares aggregate activity spending instead of merely decrementing residual gold");
        var tmFirst = withEnergy.Copy(); tmFirst.MagicPriorities = new[] { "TM", "RIT" };
        var earlierTm = Evaluate(s, tmFirst);
        Check(earlierTm.Feasible && earlierTm.Blood == 0 && earlierTm.Activities.TimeMachineEnergy == 1
            && earlierTm.Activities.TimeMachineMagic == 1 && earlierTm.ActivityGoldSpent == 1,
            "opposite priority keeps earlier TM and skips unpaid RIT when rounded aggregate spending exhausts its margin");
    }

    private static void LaneReplayChecks()
    {
        var s = ActivitySnapshot(); var r = PriorityRequest(); r.Weights[6] = r.Weights[7] = 1;
        r.EnergyPriorities = new[] { "AUG", "NGU", "TM" }; r.MagicPriorities = new[] { "RIT", "NGU", "TM" };
        s.Rituals[0].Coefficient = .01;
        s.Diggers = new[] { new SupportDigger { Id = 5, Maximum = 2, BaseDrain = 1, Growth = 2, ConsumesGold = true, PerLevel = 10 } };
        s.DiggerSlots = 1; r.PlannedSupport = true; r.DiggerWeights = new double[12]; r.DiggerWeights[5] = 1;
        var policy = Evaluate(s, r);
        Check(policy.Support.Diggers.Single().Level == 2, "reviewed replay fixture acquires an actual funded support level");
        s.Rituals = s.Rituals.Concat(new[] { new PresetRitual { Id = 9, Unlocked = true, Paid = true, Coefficient = 1, Blood = 1e9 } }).ToArray();
        s.Diggers[0].Maximum = 3;
        s.Augments[0].BaseBoost = 1e12;
        var replay = LoadoutPresetPlanner.Evaluate(s, r, policy.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 14).ToArray(), "pinned", "Pinned", policy);
        Check(replay.Ritual == policy.Ritual && replay.Blood == policy.Blood && replay.MagicNgu == policy.MagicNgu
            && replay.Activities.AugmentId == policy.Activities.AugmentId && replay.Activities.AugmentEnergy == policy.Activities.AugmentEnergy
            && replay.Activities.UpgradeEnergy == policy.Activities.UpgradeEnergy, "reviewed policy replay pins exact ritual, funding and AUG tier rather than reoptimizing");
        Check(replay.Support.Diggers.Single().Level == 2 && replay.Support.Diggers.Single().Id == 5,
            "reviewed replay retains acquired support instead of upgrading to a newly available level");
        s.Available[2] = false;
        Check(!LoadoutPresetPlanner.Evaluate(s, r, policy.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 14).ToArray(), "pinned", "Pinned", policy).Feasible,
            "pinned reviewed ritual cannot bypass a lost availability gate");
        s.Available[2] = true;
        s.Available[1] = false;
        Check(!LoadoutPresetPlanner.Evaluate(s, r, policy.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 14).ToArray(), "pinned", "Pinned", policy).Feasible,
            "pinned reviewed NGU cannot silently drop its exact funded lane after availability loss");
        s.Available[1] = true;
        s.Magic = 1;
        Check(!LoadoutPresetPlanner.Evaluate(s, r, policy.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 14).ToArray(), "pinned", "Pinned", policy).Feasible,
            "pinned priority policy rejects lost pools rather than silently resizing");
    }
}
