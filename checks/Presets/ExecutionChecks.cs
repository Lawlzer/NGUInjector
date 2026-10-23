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
        Console.WriteLine("Preset execution checks: " + (assertions - before));
    }

    private static void ExecutionEmptyTierChecks()
    {
        var s = ActivitySnapshot(); var r = PriorityRequest(); r.Weights[2] = 0;
        r.EnergyPriorities = new[] { "AUG", "NGU" };
        var policy = Evaluate(s, r);
        Check(policy.Activities.AugmentId == -1, "execution fixture explicitly acquired no AUG tier");
        r.Weights[7] = 1;
        Check(Evaluate(s, r).Activities.AugmentId >= 0, "new acquisition can authorize an AUG tier");
        var refreshed = RefreshExecution(s, r, policy);
        Check(refreshed.Feasible && refreshed.Activities.AugmentId == -1 && refreshed.Activities.AugmentEnergy == 0
            && refreshed.Activities.UpgradeEnergy == 0, "resource refresh cannot activate an unacquired AUG tier");
    }

    private static void ExecutionBloodFundingChecks()
    {
        var s = ResourceDemandSnapshot(); var r = PriorityRequest(); r.Weights[2] = 0;
        s.Rituals = new[] { s.Rituals[0] };
        var policy = Evaluate(s, r);
        Check(policy.Blood == 0 && policy.MagicNgu == 1000, "execution demand fixture initially funds lower-priority NGU");
        r.Weights[2] = 1;
        var preempted = RefreshExecution(s, r, policy);
        Check(preempted.Feasible && preempted.Blood == 1000 && preempted.MagicNgu == 0 && preempted.Metrics[1] == 0
            && preempted.Energy == policy.Energy, "execution refresh funds active blood from the live pool and zeros preempted NGU utility");
        var powerGear = s.Items[2]; Stat(powerGear, PresetStat.MagicPower, 1900);
        var prospective = RefreshExecution(s, r, preempted, new[] { powerGear });
        Check(prospective.Feasible && prospective.Blood == 501 && prospective.MagicNgu == 499 && prospective.Metrics[1] > 0,
            "execution projects candidate power before funding so gear-created overflow restores utility from stale zero funding");
        var applied = s.ActivityCopy(); applied.Current = prospective.Items;
        var observed = RefreshExecution(applied, r, prospective);
        Check(observed.Blood == prospective.Blood && observed.MagicNgu == prospective.MagicNgu && observed.Metrics[1] == prospective.Metrics[1],
            "execution after gear application agrees with prospective funding rather than oscillating back to the acquired zero");
        s.Rituals[0].Coefficient = .01;
        var overflow = RefreshExecution(s, r, preempted);
        Check(overflow.Blood == 101 && overflow.MagicNgu == 899 && overflow.Metrics[1] > 0
            && overflow.MagicShares.SequenceEqual(new long[] { 225, 674, 0 }),
            "execution refresh restores real overflow and utility despite an acquired zero-NGU funding observation");
        s.Magic = 400;
        var smaller = LoadoutPresetPlanner.ReplayExecution(s, r, ExecutionPlan(preempted), Stopwatch.StartNew()).Candidates.Single();
        Check(smaller.Feasible && smaller.Blood == 101 && smaller.MagicNgu == 299 && smaller.MagicShares.Sum() + smaller.Blood == 400,
            "ReplayExecution uses the current pool, not either stale acquired or last-refreshed amounts");
        r.Weights[2] = 0;
        var released = RefreshExecution(s, r, preempted);
        Check(released.Blood == 0 && released.MagicNgu == 400 && released.Metrics[1] > 0,
            "execution demand release recomputes the remaining priorities rather than restoring a saved allocation");
    }

    private static void ExecutionDormantOsChecks()
    {
        var s = ActivitySnapshot(); var r = PriorityRequest(); r.Weights[8] = 1;
        r.EnergyPriorities = new[] { "NGU", "WAN" }; r.MagicPriorities = new[] { "NGU" };
        var policy = Evaluate(s, r);
        Check(policy.Activities.WandoosEnergy == 0 && policy.Activities.WandoosMagic == 0 && policy.Activities.Os == 2,
            "execution captures Wandoos OS even while a higher priority temporarily suppresses its funding");
        s.WandoosOs = 0;
        Rejected(() => RefreshExecution(s, r, policy), "OS", "dormant configured Wandoos cannot adopt a newly observed OS during refresh");
        r.Weights[8] = 0;
        var unrelated = RefreshExecution(s, r, policy);
        Check(unrelated.Feasible && unrelated.Energy == policy.Energy && unrelated.Activities.Os == policy.Activities.Os,
            "unmanaged Wandoos OS changes do not block unrelated funding or rewrite the acquired OS");
    }
}
