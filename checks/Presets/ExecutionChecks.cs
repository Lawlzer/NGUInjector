using System;
using System.Diagnostics;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

internal static partial class Program
{
    private static PresetCandidate RefreshExecution(PresetSnapshot s, PresetRequest r, PresetCandidate policy, PresetItem[] items = null)
    {
        return LoadoutPresetPlanner.EvaluateExecution(s, r, items ?? policy.Items,
            LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 14).ToArray(), policy.Key, policy.Label, policy);
    }

    private static PresetPlan ExecutionPlan(PresetCandidate policy)
    {
        return new PresetPlan { Normalization = Enumerable.Repeat(1d, 14).ToArray(), ReferenceKeys = new string[14],
            ReferenceProduction = new double?[14], Candidates = new[] { policy }, Specialists = new[] { policy.Copy() } };
    }

    private static void PresetExecutionChecks()
    {
        var before = assertions;
        foreach (var autoUpdate in new[] { false, true })
        {
            var s = ActivitySnapshot(); var r = PriorityRequest(); r.Weights[7] = r.Weights[8] = 1;
            r.EnergyPriorities = new[] { "WAN", "AUG", "NGU" }; r.MagicPriorities = new[] { "RIT", "NGU" };
            s.WandoosEnergy.Coefficient = 100; s.Rituals[0].Coefficient = .01;
            s.Diggers = new[] { new SupportDigger { Id = 5, Maximum = 2, BaseDrain = 1, Growth = 2, ConsumesGold = true, PerLevel = 10 } };
            s.DiggerSlots = 1; r.PlannedSupport = true; r.DiggerWeights = new double[12]; r.DiggerWeights[5] = 1;

            // Mirror the adapter's frozen ExecutionPreset, including when AutoUpdate disables acquisition.
            var saved = new CustomLoadoutPreset { Id = "frozen", Name = "Frozen", AutoUpdate = autoUpdate, Weights = r.Weights,
                EnergyPriorities = r.EnergyPriorities, MagicPriorities = r.MagicPriorities, ResourcePrioritiesReviewed = true };
            var frozen = CustomLoadoutPreset.Parse(saved.ToJson());
            r = r.Copy(); r.Weights = frozen.Weights; r.EnergyPriorities = frozen.EnergyPriorities; r.MagicPriorities = frozen.MagicPriorities;
            saved.Weights = new double[14]; saved.Weights[12] = 1; saved.EnergyPriorities = new string[0];
            var policy = Evaluate(s, r); var plan = ExecutionPlan(policy);
            Check(policy.Feasible && policy.Activities.AugmentId == 6 && policy.Activities.WandoosEnergy > 0
                && policy.Support.Diggers.Single().Level == 2 && frozen.AutoUpdate == autoUpdate,
                "execution fixture acquires concrete AUG, OS and support decisions with frozen AutoUpdate=" + autoUpdate);

            s.Augments[0].BaseBoost = 1e12; s.Diggers[0].Maximum = 3;
            Check(Evaluate(s, r).Activities.AugmentId == 4, "authorized acquisition would choose newly preferable AUG tier " + autoUpdate);
            s.Energy = 333; s.Magic = 500;
            var refreshed = RefreshExecution(s, r, policy);
            Check(refreshed.Feasible && refreshed.Activities.AugmentId == 6 && refreshed.Activities.Os == policy.Activities.Os,
                "EvaluateExecution preserves acquired AUG and OS instead of selecting the new winner " + autoUpdate);
            Check(refreshed.Activities.AugmentEnergy + refreshed.Activities.UpgradeEnergy < policy.Activities.AugmentEnergy + policy.Activities.UpgradeEnergy
                && refreshed.Energy + refreshed.Activities.EnergyTotal == 333 && refreshed.Blood + refreshed.MagicNgu + refreshed.Activities.MagicTotal == 500,
                "EvaluateExecution recomputes exact live funding rather than freezing acquired amounts " + autoUpdate);
            Check(refreshed.Support.Diggers.Single().Level == 2 && refreshed.Support.Beards.SequenceEqual(policy.Support.Beards)
                && r.EnergyPriorities.SequenceEqual(frozen.EnergyPriorities) && r.Weights[7] > 0,
                "execution keeps acquired support and adapter-frozen intent despite live edits " + autoUpdate);
            var replay = LoadoutPresetPlanner.ReplayExecution(s, r, plan, Stopwatch.StartNew());
            Check(replay.Candidates.Concat(replay.Specialists).All(c => c.Feasible && c.Activities.AugmentId == 6
                && c.Activities.Os == policy.Activities.Os && c.Activities.EnergyTotal == refreshed.Activities.EnergyTotal
                && c.Blood == refreshed.Blood && c.MagicNgu == refreshed.MagicNgu && c.Support.Diggers.Single().Level == 2),
                "ReplayExecution preserves decisions and refreshes every candidate and specialist " + autoUpdate);
            Check(policy.Activities.AugmentId == 6 && policy.Activities.Os == 2 && policy.MagicPool == 1000,
                "execution refresh leaves acquired policy unchanged " + autoUpdate);

            s.WandoosOs = 0;
            Rejected(() => RefreshExecution(s, r, policy), "OS", "EvaluateExecution rejects incompatible acquired Wandoos OS " + autoUpdate);
            Rejected(() => LoadoutPresetPlanner.ReplayExecution(s, r, plan, Stopwatch.StartNew()), "OS",
                "ReplayExecution rejects incompatible acquired Wandoos OS " + autoUpdate);
            var beforeFunding = s.ActivityCopy(); beforeFunding.EnergyPowerProjection = null;
            Rejected(() => RefreshExecution(beforeFunding, r, policy), "OS", "OS compatibility is checked before projection and funding " + autoUpdate);
            s.WandoosOs = 2;
            var completedTier = s.Augments.Single(tier => tier.Id == policy.Activities.AugmentId);
            completedTier.Augment.Target = completedTier.Augment.Level; completedTier.Upgrade.Target = completedTier.Upgrade.Level;
            var completed = RefreshExecution(s, r, policy);
            Check(completed.Feasible && completed.Activities.AugmentId == policy.Activities.AugmentId
                && completed.Activities.AugmentEnergy == 0 && completed.Activities.UpgradeEnergy == 0
                && completed.Energy + completed.Activities.WandoosEnergy == s.Energy,
                "completing acquired AUG caps releases funding without choosing a different tier " + autoUpdate);
            s.Augments = s.Augments.Where(tier => tier.Id != policy.Activities.AugmentId).ToArray();
            Rejected(() => RefreshExecution(s, r, policy), "AUG", "missing acquired AUG cannot trigger replacement acquisition " + autoUpdate);
        }
        ExecutionEmptyTierChecks(); ExecutionDormantOsChecks(); ExecutionBloodFundingChecks();
        ExecutionZeroObjectiveAugmentChecks(); ExecutionRouteChecks();
        ExecutionFrozenSetupEligibilityChecks(); ExecutionFallbackTierEligibilityChecks();
        Console.WriteLine("Preset execution checks: " + (assertions - before));
    }

    private static void ExecutionEmptyTierChecks()
    {
        var s = ActivitySnapshot(); var r = PriorityRequest(); r.Weights[2] = 0;
        r.EnergyPriorities = new[] { "NGU" };
        var policy = Evaluate(s, r);
        Check(policy.Activities.AugmentId == -1, "execution fixture explicitly acquired no AUG tier");
        r.EnergyPriorities = new[] { "AUG", "NGU" };
        Check(Evaluate(s, r).Activities.AugmentId >= 0, "new acquisition can authorize a zero-objective AUG tier by explicit token");
        var refreshed = RefreshExecution(s, r, policy);
        Check(refreshed.Feasible && refreshed.Activities.AugmentId == -1 && refreshed.Activities.AugmentEnergy == 0
            && refreshed.Activities.UpgradeEnergy == 0, "resource refresh cannot activate an unacquired AUG tier");
    }

    private static void ExecutionBloodFundingChecks()
    {
        var s = ResourceDemandSnapshot(); var r = PriorityRequest(); r.Weights[2] = 0;
        s.Rituals = new[] { s.Rituals[0] };
        r.MagicPriorities = new[] { "NGU" };
        var policy = Evaluate(s, r);
        Check(policy.Blood == 0 && policy.MagicNgu == 1000, "execution demand fixture initially funds lower-priority NGU");
        var demand = r.Copy(); demand.MagicPriorities = new[] { "RIT", "NGU" };
        var preempted = RefreshExecution(s, demand, policy);
        Check(preempted.Feasible && preempted.Blood == 1000 && preempted.MagicNgu == 0 && preempted.Metrics[1] == 0
            && preempted.Energy == policy.Energy, "execution refresh funds active blood from the live pool and zeros preempted NGU utility");
        Check(preempted.SetupScore == policy.SetupScore && preempted.Score < policy.Score,
            "temporary funding preemption changes actual production score without changing acquired setup rank");
        var powerGear = s.Items[2]; Stat(powerGear, PresetStat.MagicPower, 1900);
        var prospective = RefreshExecution(s, demand, preempted, new[] { powerGear });
        Check(prospective.Feasible && prospective.Blood == 501 && prospective.MagicNgu == 499 && prospective.Metrics[1] > 0,
            "execution projects candidate power before funding so gear-created overflow restores utility from stale zero funding");
        var applied = s.ActivityCopy(); applied.Current = prospective.Items;
        var observed = RefreshExecution(applied, demand, prospective);
        Check(observed.Blood == prospective.Blood && observed.MagicNgu == prospective.MagicNgu && observed.Metrics[1] == prospective.Metrics[1],
            "execution after gear application agrees with prospective funding rather than oscillating back to the acquired zero");
        s.Rituals[0].Coefficient = .01;
        var overflow = RefreshExecution(s, demand, preempted);
        Check(overflow.Blood == 101 && overflow.MagicNgu == 899 && overflow.Metrics[1] > 0
            && overflow.MagicShares.SequenceEqual(new long[] { 225, 674, 0 }),
            "execution refresh restores real overflow and utility despite an acquired zero-NGU funding observation");
        s.Magic = 400;
        var smaller = LoadoutPresetPlanner.ReplayExecution(s, demand, ExecutionPlan(preempted), Stopwatch.StartNew()).Candidates.Single();
        Check(smaller.Feasible && smaller.Blood == 101 && smaller.MagicNgu == 299 && smaller.MagicShares.Sum() + smaller.Blood == 400,
            "ReplayExecution uses the current pool, not either stale acquired or last-refreshed amounts");
        var released = RefreshExecution(s, r, preempted);
        Check(released.Blood == 0 && released.MagicNgu == 400 && released.Metrics[1] > 0,
            "execution demand release recomputes the remaining priorities rather than restoring a saved allocation");
        Check(r.Weights[2] == 0 && r.MagicPriorities.SequenceEqual(new[] { "NGU" })
            && demand.MagicPriorities.SequenceEqual(new[] { "RIT", "NGU" }) && demand.Weights.SequenceEqual(r.Weights),
            "temporary demand uses a copied request without changing objectives or the baseline reviewed chain");
    }

    private static void ExecutionDormantOsChecks()
    {
        var s = ActivitySnapshot(); var r = PriorityRequest();
        r.EnergyPriorities = new[] { "NGU", "WAN" }; r.MagicPriorities = new[] { "NGU" };
        var policy = Evaluate(s, r);
        Check(policy.Activities.WandoosEnergy == 0 && policy.Activities.WandoosMagic == 0 && policy.Activities.Os == 2,
            "execution captures Wandoos OS even while a higher priority temporarily suppresses its funding");
        s.WandoosOs = 0;
        Rejected(() => RefreshExecution(s, r, policy), "OS", "dormant configured Wandoos cannot adopt a newly observed OS during refresh");
        var pinned = LoadoutPresetPlanner.Evaluate(s, r, policy.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights),
            Enumerable.Repeat(1d, 14).ToArray(), policy.Key, policy.Label, policy);
        Check(!pinned.Feasible && pinned.Score == null && pinned.SetupScore == policy.SetupScore,
            "exact reviewed replay also rejects incompatible dormant zero-objective Wandoos OS");
        r.EnergyPriorities = new[] { "NGU" };
        var unrelated = RefreshExecution(s, r, policy);
        Check(unrelated.Feasible && unrelated.Energy == policy.Energy && unrelated.Activities.Os == policy.Activities.Os,
            "unmanaged Wandoos OS changes do not block unrelated funding or rewrite the acquired OS");
    }

    private static void ExecutionZeroObjectiveAugmentChecks()
    {
        var s = ActivitySnapshot(); s.Available = Enumerable.Repeat(true, 14).ToArray(); s.PpPerKill = new double[0];
        var r = PriorityRequest(); r.Weights = ActivityRequest(12).Weights;
        r.EnergyPriorities = new[] { "NGU", "AUG" }; r.MagicPriorities = new string[0];
        var policy = Evaluate(s, r);
        Check(policy.Feasible && policy.Activities.AugmentId == 6 && policy.Activities.AugmentEnergy == 0
            && policy.Activities.UpgradeEnergy == 0 && policy.SetupScore == 1000,
            "zero-objective AUG acquires its local best tier even while NGU preempts every unit");
        var reversed = r.Copy(); reversed.EnergyPriorities = new[] { "AUG", "NGU" };
        var active = Evaluate(s, reversed);
        Check(active.Activities.AugmentId == policy.Activities.AugmentId && active.Metrics[7] > 0 && active.SetupScore == policy.SetupScore,
            "fallback tier acquisition is independent of priority position and actual AUG funding");
        s.Augments = s.Augments.Reverse().ToArray();
        Check(Evaluate(s, reversed).Activities.AugmentId == 6, "local AUG acquisition does not depend on captured tier enumeration");
        s.Augments.Single(x => x.Id == 6).BaseBoost = 1;
        Check(Evaluate(s, reversed).Activities.AugmentId == 4, "equal local AUG production breaks ties deterministically by native tier ID");
        s.Augments.Single(x => x.Id == 6).BaseBoost = 2;
        s.Augments.Single(x => x.Id == 4).BaseBoost = 1e12;
        Check(Evaluate(s, reversed).Activities.AugmentId == 4, "fresh zero-objective acquisition can choose a genuinely better tier");
        s.EnergyNgu[0].Target = s.EnergyNgu[0].Level + 1; s.EnergyNgu[0].Coefficient = 100;
        var refreshed = RefreshExecution(s, r, policy);
        Check(refreshed.Feasible && refreshed.Activities.AugmentId == 6 && refreshed.Metrics[7] > 0 && refreshed.Energy == 1
            && refreshed.Energy + refreshed.Activities.EnergyTotal == 1000 && refreshed.Score == 1000
            && refreshed.SetupScore == policy.SetupScore && policy.Activities.AugmentEnergy == 0,
            "fresh residual activates only the acquired zero-objective AUG tier and preserves setup rank and original policy");
        var plan = ExecutionPlan(policy); plan.ReferenceKeys[12] = "setup-cap"; plan.ReferenceProduction[12] = 1000;
        var replay = LoadoutPresetPlanner.ReplayExecution(s, r, plan, Stopwatch.StartNew());
        SameReferences(replay, plan, "zero-objective tier funding refresh");
        Check(replay.Candidates.Concat(replay.Specialists).All(c => c.SetupScore == policy.SetupScore && c.Activities.AugmentId == 6)
            && replay.Sequence == null, "execution replay preserves setup rank and acquired fallback tier without a proportional sequence");
        var tier = s.Augments.Single(x => x.Id == 6); tier.Augment.Target = tier.Augment.Level; tier.Upgrade.Target = tier.Upgrade.Level;
        var completed = RefreshExecution(s, r, policy);
        Check(completed.Activities.AugmentId == 6 && completed.Activities.AugmentEnergy == 0 && completed.Activities.UpgradeEnergy == 0,
            "completed zero-objective acquired tier stays selected rather than replacing it with the new best tier");
    }

    private static void ExecutionRouteChecks()
    {
        var s = ActivitySnapshot(); s.Available = Enumerable.Repeat(true, 14).ToArray();
        s.PpPerKill = new[] { 0d, 200, 240, 1000 }; s.ItopodHp = new[] { 0d, 500, 900, 1300 };
        s.ItopodDefense = new double[4];
        var r = PriorityRequest(); r.Weights = ActivityRequest(3, 12).Weights;
        r.EnergyPriorities = new[] { "NGU" }; r.MagicPriorities = new string[0];
        var policy = Evaluate(s, r);
        Check(policy.Feasible && policy.Zone == 1000 && policy.Floor == 3 && policy.Hits == 2,
            "reviewed execution fixture acquires a concrete short-multihit ITOPOD route");
        s.PpPerKill[1] = 1e9;
        Check(Evaluate(s, r).Floor == 1, "fresh authorized setup search would replace the acquired route");
        var refreshed = RefreshExecution(s, r, policy);
        SameSetup(refreshed, policy, "reviewed execution route refresh");
        Check(refreshed.Feasible && refreshed.SetupScore == policy.SetupScore && refreshed.Metrics[3] == policy.Metrics[3],
            "funding refresh does not search replacement floors or rescore setup rank");
        var observed = s.ActivityCopy(); observed.ObservedItopodFloor = 3; observed.ObservedItopodMaximum = 3;
        observed.PpPerKill = new[] { 1000d }; observed.ItopodHp = new[] { 1300d }; observed.ItopodDefense = new double[1];
        var bounded = RefreshExecution(observed, r, policy);
        Check(bounded.Feasible && bounded.Floor == 3 && bounded.Metrics[3] == policy.Metrics[3],
            "execution funding consumes one acquired floor observation without requiring floor-indexed replacement data");
        observed.ItopodHp = new[] { 1e12 };
        var unsafeRoute = RefreshExecution(observed, r, policy);
        Check(!unsafeRoute.Feasible && unsafeRoute.Score == null && unsafeRoute.SetupScore == policy.SetupScore,
            "lost acquired-route feasibility blocks refresh rather than substituting another route");
    }

    private static void ExecutionFrozenSetupEligibilityChecks()
    {
        var s = ScarceGoldTmSnapshot(); var r = ScarceGoldTmRequest(); r.MinimumPower = 2000;
        var policy = Evaluate(s, r);
        Check(!policy.Feasible && policy.SetupScore == null && policy.Score == null,
            "frozen setup fixture records original hard power ineligibility rather than funded production utility");
        var fresh = s.ActivityCopy(); fresh.BasePower = 2000;
        Check(Evaluate(fresh, r).Feasible && Evaluate(fresh, r).SetupScore > 0,
            "new acquisition really could pass the formerly failed hard power constraint");
        var plan = ExecutionPlan(policy);
        var exact = LoadoutPresetPlanner.Replay(fresh, r, plan);
        var execution = LoadoutPresetPlanner.ReplayExecution(fresh, r, plan, Stopwatch.StartNew());
        Check(exact.Candidates.Concat(exact.Specialists).All(c => !c.Feasible && c.SetupScore == null && c.Score == null),
            "exact Replay cannot revive a setup that had no frozen hard eligibility witness");
        Check(execution.Candidates.Concat(execution.Specialists).All(c => !c.Feasible && c.SetupScore == null && c.Score == null),
            "execution replay also cannot turn newly observed hard eligibility into an acquired setup");

        s = ScarceGoldTmSnapshot(); r = ScarceGoldTmRequest(); policy = Evaluate(s, r);
        var eligible = LoadoutPresetPlanner.Replay(s, r, ExecutionPlan(policy));
        Check(eligible.Candidates.Concat(eligible.Specialists).All(c => c.Feasible && c.SetupScore == policy.SetupScore && c.Score == policy.Score),
            "exact Replay preserves both frozen affordable setup utility and actual funded production");

        s = ActivitySnapshot(); r = PriorityRequest(); r.Weights = ActivityRequest(0, 9).Weights;
        r.EnergyPriorities = new[] { "NGU", "AT" }; r.MagicPriorities = new string[0];
        s.RequiredATTargets = new long[] { 11, 11 };
        foreach (var bar in s.AdvancedTraining) { bar.Coefficient = 100; bar.LevelExponent = 0; }
        policy = Evaluate(s, r);
        Check(!policy.Feasible && policy.SetupScore > 0 && policy.Score == null,
            "funding-only failure retains its distinct valid frozen setup witness");
        s.EnergyNgu[0].Coefficient = 100; s.EnergyNgu[0].Target = s.EnergyNgu[0].Level + 1;
        var recovered = RefreshExecution(s, r, policy);
        Check(recovered.Feasible && recovered.SetupScore == policy.SetupScore && recovered.Activities.AT.Take(2).All(x => x > 0),
            "funding refresh may recover a valid acquired setup when real residuals return, without reacquiring it");
    }

    private static void ExecutionFallbackTierEligibilityChecks()
    {
        var s = ActivitySnapshot(); s.Available = s.Available.Concat(new[] { true, true }).ToArray(); s.Gold = 10;
        var r = PriorityRequest(); r.Weights = ActivityRequest(12).Weights;
        r.EnergyPriorities = new[] { "NGU", "AUG" }; r.MagicPriorities = new string[0];
        s.Augments[0].BaseBoost = 1e12;
        s.Augments[0].Augment.CostCoefficient = s.Augments[0].Upgrade.CostCoefficient = 1000;
        Check(Evaluate(s, r).Activities.AugmentId == 6,
            "dormant fallback acquires the affordable productive tier rather than an unaffordable higher-potential tier");
        s.Augments[1].Augment.CostCoefficient = s.Augments[1].Upgrade.CostCoefficient = 1000;
        s.Augments = s.Augments.Reverse().ToArray();
        Check(Evaluate(s, r).Activities.AugmentId == 4,
            "all-unaffordable zero-production tier ties remain deterministic by native ID");
        var completed = s.Augments.Single(x => x.Id == 4);
        completed.Augment.Target = completed.Augment.Level; completed.Upgrade.Target = completed.Upgrade.Level;
        Check(Evaluate(s, r).Activities.AugmentId == 4,
            "completed and unaffordable zero-production tier ties use the same stable native-ID order");
        completed.Augment.Target = completed.Upgrade.Target = 0;
        foreach (var tier in s.Augments) tier.Augment.CostCoefficient = tier.Upgrade.CostCoefficient = 0;
        var probeRequest = ActivityRequest(7); probeRequest.MinimumPower = 2000;
        var probe = Evaluate(s, probeRequest);
        Check(!probe.Feasible && probe.Metrics[7] > 0,
            "ineligible tier fixture exposes positive provisional AUG production despite failed hard setup eligibility");
        r.MinimumPower = 2000;
        var ineligible = Evaluate(s, r);
        Check(!ineligible.Feasible && ineligible.SetupScore == null && ineligible.Activities.AugmentId == -1,
            "tier acquisition cannot promote any probe that fails the frozen setup's hard eligibility");

        s = ActivitySnapshot(); s.Available = s.Available.Concat(new[] { true, true }).ToArray();
        s.RequiredATTargets = new long[] { 11, 11 };
        foreach (var bar in s.AdvancedTraining) { bar.Coefficient = 100; bar.LevelExponent = 0; }
        s.EnergyNgu[0].Coefficient = 100; s.EnergyNgu[0].Target = s.EnergyNgu[0].Level + 1;
        r = PriorityRequest(); r.Weights = ActivityRequest(9, 12).Weights;
        r.EnergyPriorities = new[] { "AT", "NGU", "AUG" }; r.MagicPriorities = new string[0];
        var required = Evaluate(s, r);
        Check(required.Feasible && required.Activities.AugmentId == 6 && required.Activities.AT.Take(2).All(x => x > 0),
            "tier-local feasibility excludes unrelated provisional AT funding while final strict funding still enforces mandatory AT");
    }
}
