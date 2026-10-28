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
        private static PresetRequest ObjectiveRequest(PresetRequest request)
        {
            var setup = request.Copy();
            setup.ResourcePrioritiesReviewed = false;
            setup.EnergyPriorities = setup.MagicPriorities = null;
            setup.ObjectiveSetup = true;
            return setup;
        }

        private static bool ReviewedNguFunding(PresetRequest request)
        {
            return request.ResourcePrioritiesReviewed && ((request.EnergyPriorities?.Contains("NGU") ?? false)
                || (request.MagicPriorities?.Contains("NGU") ?? false));
        }

        private static PresetSnapshot FundingSnapshot(PresetSnapshot snapshot, PresetCandidate setup)
        {
            var funding = snapshot.ActivityCopy();
            funding.AcquiredRouteObservation = true;
            funding.AcquiredRouteZone = setup.Zone; funding.AcquiredRouteFloor = setup.Floor;
            return funding;
        }

        private static PresetCandidate FundSetup(PresetSnapshot snapshot, PresetRequest request, PresetCandidate setup,
            double[] weights, double[] norms)
        {
            var acquired = setup.Activities ?? new PresetActivityAllocation { Os = snapshot.WandoosOs };
            var s = FundingSnapshot(snapshot, setup);
            if (acquired.AugmentId < 0 && request.EnergyPriorities.Contains("AUG")
                && snapshot.Available.Length > 7 && snapshot.Available[7])
            {
                // An objective-free AUG fallback acquires one tier locally, before lane-order funding.
                var local = request.Copy(); local.Weights = new double[Keys.Length]; local.Weights[7] = 1;
                local.GearWeights = new double[5];
                local.EnergyPriorities = new[] { "AUG" }; local.MagicPriorities = new string[0];
                var tierSnapshot = s.ActivityCopy(); tierSnapshot.RequiredATTargets = new long[0];
                PresetCandidate best = null;
                foreach (var tier in snapshot.Augments.Where(x => x.Augment != null && x.Upgrade != null
                    && (x.Augment.Unlocked || x.Upgrade.Unlocked)).OrderBy(x => x.Id))
                {
                    var choice = new PresetActivityAllocation { Os = acquired.Os, AugmentId = tier.Id };
                    var trial = EvaluateSelection(tierSnapshot, local, setup.Items, local.Weights, norms, null, null, null, setup.Support, choice);
                    if (trial.Feasible && trial.Score.HasValue && (best == null || trial.Metrics[7] > best.Metrics[7]))
                    { best = trial; acquired = choice; }
                }
            }
            var funded = EvaluateSelection(s, request, setup.Items, weights, norms, setup.Key, setup.Label, null, setup.Support, acquired);
            funded.SetupScore = setup.Score;
            funded.SetupEligible = setup.Feasible && setup.Score.HasValue;
            funded.Feasible &= funded.SetupEligible.Value;
            funded.Score = CandidateScore(s, request, funded, weights, norms);
            funded.RegularEligibilityZone = setup.RegularEligibilityZone;
            funded.BoostEligibilityZone = setup.BoostEligibilityZone;
            funded.Warnings = setup.Warnings.Concat(funded.Warnings).Distinct().ToArray();
            funded.Evaluations = setup.Evaluations + 1;
            return funded;
        }

        private static PresetCandidate EvaluatePriorities(PresetSnapshot s, PresetRequest r, PresetItem[] items,
            double[] weights, double[] norms, string key, string label, PresetCandidate pinned, PresetSupport support,
            PresetActivityAllocation acquiredActivities = null)
        {
            if (r.ATWeights == null || r.ATWeights.Length != 5 || r.ATWeights.Any(x => x < 0))
                throw new ArgumentException("ATWeights must contain five nonnegative integers.");
            if (pinned != null)
            {
                if (pinned.PriorityFunding == null) throw new InvalidOperationException("Cached policy has no reviewed lane funding; preview again.");
                var replay = EvaluateActivities(s, r, items, weights, norms, key, label, pinned, support,
                    pinned.Activities ?? new PresetActivityAllocation { Os = s.WandoosOs }, pinned.PriorityFunding);
                if ((r.EnergyPriorities.Contains("WAN") || r.MagicPriorities.Contains("WAN")) && replay.Activities.Os != s.WandoosOs)
                {
                    replay.Feasible = false; replay.Score = null;
                    replay.Warnings = replay.Warnings.Concat(new[] { "Acquired Wandoos OS is incompatible with the installed OS; preview again." }).ToArray();
                }
                return replay;
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
            if (acquiredActivities == null) throw new InvalidOperationException("Acquired activity decisions are missing; preview again.");
            // Funding never ranks tiers or setups. Acquisition has already chosen this tier (including none).
            var tier = acquiredActivities.AugmentId < 0 ? null : s.Augments.SingleOrDefault(x => x.Id == acquiredActivities.AugmentId
                && x.Augment != null && x.Upgrade != null);
            if (acquiredActivities.AugmentId >= 0 && tier == null)
                throw new InvalidOperationException("Acquired AUG tier is unavailable; preview again.");
            var allocation = new PresetActivityAllocation { Os = acquiredActivities.Os, AugmentId = tier?.Id ?? -1 };
            var funding = new PresetLaneFunding();
            var liquid = s.Gold;
            Func<double, bool> laterPaidCostFits = null;
            Func<PresetActivityBar, long, double, double, long> fundBar = (bar, pool, power, speed) => {
                var amount = Math.Min(pool, ActivityUsefulCap(bar, power, speed));
                return AffordableActivityBar(bar, amount, power, r.Horizon, speed, ref liquid, laterPaidCostFits) ?? 0;
            };
            ResourceLanePriority.Resolve(energy, r.EnergyPriorities, (activity, pool) => {
                if (activity == "NGU" && s.Available[0])
                    return funding.EnergyNgu = NguShares(s.EnergyNgu, pool, ep, AllocationLookaheadSeconds, nguBonus, DiggerBonus(s, support.Diggers, 4)).Sum();
                if (activity == "TM" && s.Available[6])
                    return allocation.TimeMachineEnergy = fundBar(s.TimeMachineEnergy, pool, ep, 1);
                if (activity == "WAN" && s.Available[8])
                    return allocation.WandoosEnergy = fundBar(s.WandoosEnergy, pool, 1, wSpeed);
                if (activity == "AUG" && s.Available[7] && tier != null)
                {
                    var pair = Share(new[] { (double)ActivityUsefulCap(tier.Augment, ep, augSpeed),
                        (double)ActivityUsefulCap(tier.Upgrade, ep, augSpeed) }, pool, new[] { 1d, 1 });
                    allocation.AugmentEnergy = fundBar(tier.Augment, pair[0], ep, augSpeed);
                    allocation.UpgradeEnergy = fundBar(tier.Upgrade, pool - allocation.AugmentEnergy, ep, augSpeed);
                    return allocation.AugmentEnergy + allocation.UpgradeEnergy;
                }
                if (activity == "AT" && s.Available[9])
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
                if (activity == "NGU" && s.Available[1])
                    return funding.MagicNgu = NguShares(s.MagicNgu, pool, mp, AllocationLookaheadSeconds, nguBonus, DiggerBonus(s, support.Diggers, 5)).Sum();
                if (activity == "TM" && s.Available[6])
                    return allocation.TimeMachineMagic = fundBar(s.TimeMachineMagic, pool, mp, 1);
                if (activity == "WAN" && s.Available[8])
                    return allocation.WandoosMagic = fundBar(s.WandoosMagic, pool, 1, wSpeed);
                if (activity == "RIT" && s.Available[2])
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
                    if (ritual != null && !ritual.Paid)
                    {
                        var energyTmCost = ActivityFundingCost(s.TimeMachineEnergy, allocation.TimeMachineEnergy, ep, r.Horizon, 1);
                        var magicTmCost = ActivityFundingCost(s.TimeMachineMagic, allocation.TimeMachineMagic, mp, r.Horizon, 1);
                        var augmentPairCost = tier == null ? 0 : ActivityFundingCost(tier.Augment, allocation.AugmentEnergy, ep, r.Horizon, augSpeed)
                            + ActivityFundingCost(tier.Upgrade, allocation.UpgradeEnergy, ep, r.Horizon, augSpeed);
                        var paidLimit = s.Gold - ritual.Cost;
                        // Match final cost grouping and strict startup comparison, not rounded residual gold.
                        // Only Magic TM can incur a new paid cost after this Magic-lane ritual.
                        laterPaidCostFits = cost => (energyTmCost + cost) + augmentPairCost < paidLimit;
                        if (!laterPaidCostFits(magicTmCost))
                        {
                            funding.Blood = 0; funding.Ritual = -1; laterPaidCostFits = null;
                            return 0;
                        }
                        liquid -= ritual.Cost;
                    }
                    return funding.Blood;
                }
                return 0;
            });
            return EvaluateActivities(s, r, items, weights, norms, key, label, null, support, allocation, funding);
        }

        private static double ActivityFundingCost(PresetActivityBar bar, long amount, double power, double seconds, double speed)
        {
            var gain = ActivityGain(bar, amount, power, seconds, speed);
            return gain.HasValue ? ActivityCost(bar, gain.Value, amount) : double.PositiveInfinity;
        }

        private static long? AffordableActivityBar(PresetActivityBar bar, long amount, double power, double seconds, double speed,
            ref double liquid, Func<double, bool> costFits = null)
        {
            Func<long, double> cost = value => ActivityFundingCost(bar, value, power, seconds, speed);
            var fullCost = cost(amount);
            var low = fullCost <= liquid && (costFits == null || costFits(fullCost)) ? amount : 0L; var high = amount;
            while (low < high)
            {
                var mid = low + (high - low) / 2 + 1;
                var midCost = cost(mid);
                if (midCost <= liquid && (costFits == null || costFits(midCost))) low = mid; else high = mid - 1;
            }
            if (low == 0 && !Finite(fullCost)) return null;
            if (low > 0) liquid -= cost(low);
            return low;
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
