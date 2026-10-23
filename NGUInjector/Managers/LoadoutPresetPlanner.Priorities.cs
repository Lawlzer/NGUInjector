using System;
using System.Linq;

namespace NGUInjector.Managers
{
    internal sealed class PresetLaneFunding
    {
        internal long EnergyNgu, MagicNgu, Blood;
        internal int Ritual = -1;
    }

    internal static partial class LoadoutPresetPlanner
    {
        private static PresetCandidate EvaluatePriorities(PresetSnapshot s, PresetRequest r, PresetItem[] items,
            double[] weights, double[] norms, string key, string label, PresetCandidate pinned, PresetSupport support,
            PresetActivityAllocation acquiredActivities = null)
        {
            if (r.ATWeights == null || r.ATWeights.Length != 5 || r.ATWeights.Any(x => x < 0))
                throw new ArgumentException("ATWeights must contain five nonnegative integers.");
            if (pinned != null)
            {
                if (pinned.PriorityFunding == null) throw new InvalidOperationException("Cached policy has no reviewed lane funding; preview again.");
                return EvaluateActivities(s, r, items, weights, norms, key, label, pinned, support,
                    pinned.Activities ?? new PresetActivityAllocation { Os = s.WandoosOs }, pinned.PriorityFunding);
            }

            var resources = ProjectResources(s, items);
            var bonuses = resources.Bonuses; var ep = resources.EnergyPower; var mp = resources.MagicPower;
            var energy = resources.Energy; var magic = resources.Magic;
            var totals = Totals(s, items); var original = Totals(s, s.Current);
            Func<PresetStat, double> gearRatio = stat => Math.Max(0, 100 + totals[(int)stat]) / Math.Max(1e-100, 100 + original[(int)stat]);
            var nguBonus = NguBonus(s, bonuses, support.Beards);
            var atSpeed = gearRatio(PresetStat.AdvancedTrainingSpeed); var augSpeed = gearRatio(PresetStat.AugmentSpeed);
            var wSpeed = gearRatio(PresetStat.WandoosSpeed)
                * SupportProjection.BeardBonus(s, support.Beards, 4) / SupportProjection.BeardBonus(s, s.CurrentBeards, 4)
                * DiggerBonus(s, support.Diggers, 1) / DiggerBonus(s, s.CurrentDiggers, 1);
            var supportEvaluation = SupportProjection.Evaluate(s, new SupportSelection(support.Beards,
                support.Diggers.Select(d => new SupportDiggerLevel(d.Id, d.Level)).ToArray()), r.DiggerPercent / 100d, r.PlannedSupport);
            var tiers = s.Available[7] && weights[7] > 0 && r.EnergyPriorities.Contains("AUG")
                ? s.Augments.Where(x => x.Augment != null && x.Upgrade != null).ToArray() : new PresetAugment[0];
            if (tiers.Length == 0) tiers = new PresetAugment[] { null };
            if (acquiredActivities != null)
            {
                // Execution may refresh amounts, but only acquisition can choose another tier (including none).
                tiers = acquiredActivities.AugmentId < 0 ? new PresetAugment[] { null }
                    : s.Augments.Where(x => x.Id == acquiredActivities.AugmentId && x.Augment != null && x.Upgrade != null).ToArray();
                if (tiers.Length != 1) throw new InvalidOperationException("Acquired AUG tier is unavailable; preview again.");
            }
            PresetCandidate best = null;
            foreach (var tier in tiers)
            {
                var allocation = new PresetActivityAllocation { Os = acquiredActivities?.Os ?? s.WandoosOs, AugmentId = tier?.Id ?? -1 };
                var funding = new PresetLaneFunding();
                var liquid = s.Gold;
                Func<PresetActivityBar, long, double, double, long> fundBar = (bar, pool, power, speed) => {
                    var amount = Math.Min(pool, ActivityUsefulCap(bar, power, speed));
                    // The same bounded forecast and conservative paid-start accounting used by final evaluation.
                    Func<long, double> cost = value => {
                        var gain = ActivityGain(bar, value, power, r.Horizon, speed);
                        return gain.HasValue ? ActivityCost(bar, gain.Value, value) : double.PositiveInfinity;
                    };
                    var low = cost(amount) <= liquid ? amount : 0L; var high = amount;
                    while (low < high)
                    {
                        var mid = low + (high - low) / 2 + 1;
                        if (cost(mid) <= liquid) low = mid; else high = mid - 1;
                    }
                    if (low > 0) liquid -= cost(low);
                    return low;
                };
                ResourceLanePriority.Resolve(energy, r.EnergyPriorities, (activity, pool) => {
                    if (activity == "NGU" && s.Available[0] && weights[0] > 0)
                        return funding.EnergyNgu = NguShares(s.EnergyNgu, pool, ep, AllocationLookaheadSeconds, nguBonus, DiggerBonus(s, support.Diggers, 4)).Sum();
                    if (activity == "TM" && s.Available[6] && weights[6] > 0)
                        return allocation.TimeMachineEnergy = fundBar(s.TimeMachineEnergy, pool, ep, 1);
                    if (activity == "WAN" && s.Available[8] && weights[8] > 0)
                        return allocation.WandoosEnergy = fundBar(s.WandoosEnergy, pool, 1, wSpeed);
                    if (activity == "AUG" && s.Available[7] && tier != null && weights[7] > 0)
                    {
                        var pair = Share(new[] { (double)ActivityUsefulCap(tier.Augment, ep, augSpeed),
                            (double)ActivityUsefulCap(tier.Upgrade, ep, augSpeed) }, pool, new[] { 1d, 1 });
                        allocation.AugmentEnergy = fundBar(tier.Augment, pair[0], ep, augSpeed);
                        allocation.UpgradeEnergy = fundBar(tier.Upgrade, pool - allocation.AugmentEnergy, ep, augSpeed);
                        return allocation.AugmentEnergy + allocation.UpgradeEnergy;
                    }
                    if (activity == "AT" && s.Available[9] && weights[9] > 0)
                    {
                        var caps = s.AdvancedTraining.Select((bar, i) => s.RequiredATTargets.Length > i
                            && (s.RequiredATTargets[i] <= 0 || bar.Level >= s.RequiredATTargets[i]) ? 0d
                            : (double)ActivityUsefulCap(bar, ep, atSpeed)).ToArray();
                        allocation.AT = Share(caps, pool, r.ATWeights.Select(x => (double)x).ToArray());
                        return allocation.AT.Sum();
                    }
                    return 0;
                });
                ResourceLanePriority.Resolve(magic, r.MagicPriorities, (activity, pool) => {
                    if (activity == "NGU" && s.Available[1] && weights[1] > 0)
                        return funding.MagicNgu = NguShares(s.MagicNgu, pool, mp, AllocationLookaheadSeconds, nguBonus, DiggerBonus(s, support.Diggers, 5)).Sum();
                    if (activity == "TM" && s.Available[6] && weights[6] > 0)
                        return allocation.TimeMachineMagic = fundBar(s.TimeMachineMagic, pool, mp, 1);
                    if (activity == "WAN" && s.Available[8] && weights[8] > 0)
                        return allocation.WandoosMagic = fundBar(s.WandoosMagic, pool, 1, wSpeed);
                    if (activity == "RIT" && s.Available[2] && weights[2] > 0)
                    {
                        // Reuse the native discrete sustainable blood cap, including paid and startup gates.
                        var bloodSnapshot = s.ActivityCopy(); bloodSnapshot.Gold = liquid;
                        var baseline = new PresetCandidate { Feasible = true, MagicPool = pool, MagicPower = mp,
                            MagicNguPower = mp, NguBonus = nguBonus, MagicDiggerBonus = DiggerBonus(s, support.Diggers, 5),
                            BloodBonus = DiggerBonus(s, support.Diggers, 10) / DiggerBonus(s, s.CurrentDiggers, 10),
                            GrossGold = supportEvaluation.GrossGold, Support = new PresetSupport { Drain = supportEvaluation.Drain } };
                        var blood = ComposeResourceDemands(bloodSnapshot, baseline, true, false);
                        funding.Blood = blood.Blood; funding.Ritual = blood.Ritual;
                        var ritual = s.Rituals.FirstOrDefault(x => x.Id == funding.Ritual);
                        if (ritual != null && !ritual.Paid) liquid -= ritual.Cost;
                        return funding.Blood;
                    }
                    return 0;
                });
                var candidate = EvaluateActivities(s, r, items, weights, norms, key, label, null, support, allocation, funding);
                if (best == null || Rank(s, r, candidate, weights, norms) > Rank(s, r, best, weights, norms) + 1e-12) best = candidate;
            }
            return best;
        }

        private static long ActivityUsefulCap(PresetActivityBar bar, double power, double speed)
        {
            if (bar == null || !bar.Unlocked || bar.Target < 0 || bar.Target > 0 && bar.Level >= bar.Target
                || !Finite(bar.Level) || bar.Level < 0 || bar.Level != Math.Floor(bar.Level)
                || !Finite(bar.Coefficient) || bar.Coefficient <= 0 || !(power > 0) || !(speed > 0)
                || bar.LevelExponent != 0 && bar.LevelExponent != 1) return 0;
            var level = Math.Min(bar.Target > 0 ? bar.Target : 9007199254740991d, bar.Level + 50d * AllocationLookaheadSeconds + 2);
            var rate = Math.Pow(power, bar.PowerExponent) * bar.Coefficient * speed * ActivityRateSafetyFactor;
            return rate > 0 && Finite(rate) ? IntegerBudget(Math.Ceiling(Math.Pow(level, bar.LevelExponent) / rate * 1.000002d)) : 0;
        }
    }
}
