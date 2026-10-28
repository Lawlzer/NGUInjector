using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NGUInjector.CustomPlans;

namespace NGUInjector.Managers
{
    // Numeric-only snapshot. No game references, callbacks, or mutations belong in this engine.
    internal enum PresetStat { Power, Toughness, EnergyCap, EnergyPower, EnergyBars, MagicCap, MagicPower, MagicBars,
        Res3Cap, Res3Power, NguSpeed, Gold, Drop, BeardSpeed, WandoosSpeed, AugmentSpeed, AdvancedTrainingSpeed,
        MoveCooldown, WishSpeed, HackSpeed, Respawn }

    internal sealed class PresetItem
    {
        internal int Id, SourceSlot, Group, Level;
        internal string Name;
        internal double[] Stats = new double[21];
        internal float[] ResourceSpecs = new float[14];
        internal float[] LootSpecs = new float[2];
    }
    internal sealed class PresetNgu
    {
        internal double Level, Target, Coefficient;
        internal double HardCap;
        internal float SpeedDivider, TickDivider = 1;
        internal double[] TickFactors = new double[0];
        internal int Weight;
    }
    internal sealed class PresetRitual
    {
        internal int Id;
        internal double Coefficient, Blood, Cost;
        internal bool Unlocked, Paid;
    }
    internal sealed class PresetZone
    {
        internal int Id;
        internal double IdlePower, IdleToughness, OneHitPower, NormalRatio;
        internal double[] BoostValues = new double[0], Chances = new double[0], Caps = new double[0];
    }
    internal sealed class PresetActivityBar
    {
        // Per-tick progress = allocation * coefficient * power^PowerExponent / (level+1)^LevelExponent.
        internal double Level, Target, Progress, Coefficient, PowerExponent = 1, LevelExponent = 1;
        internal bool Unlocked, Paid;
        internal double CostCoefficient, CostExponent = 1;
    }
    internal sealed class PresetAugment
    {
        internal int Id;
        internal double BaseBoost, TierExponent = 1;
        internal PresetActivityBar Augment, Upgrade;
    }
    internal sealed class PresetActivityAllocation
    {
        internal long TimeMachineEnergy, TimeMachineMagic, WandoosEnergy, WandoosMagic;
        internal int Os, AugmentId = -1;
        internal long AugmentEnergy, UpgradeEnergy;
        internal long[] AT = new long[5];
        internal long EnergyTotal => checked(TimeMachineEnergy + WandoosEnergy + AugmentEnergy + UpgradeEnergy + AT.Sum());
        internal long MagicTotal => checked(TimeMachineMagic + WandoosMagic);
    }
    internal sealed class PresetSnapshot : SupportContext
    {
        internal PresetItem[] Items, Current;
        internal int[] Slots;
        internal double[] Factors;
        internal long Energy, Magic;
        internal double EnergyCap, MagicCap, EnergyPower, MagicPower, Power, Toughness;
        internal double BasePower, BaseToughness, Drop, DropGearBase = 100, Respawn, AttackSeconds, IdleModifier;
        internal double CubePower, CubeToughness;
        internal double[] PowerFactors = Enumerable.Repeat(1d, 14).ToArray(), ToughnessFactors = Enumerable.Repeat(1d, 14).ToArray();
        internal float[] NguFactors = new float[0];
        internal ResourceProjection EnergyCapacity, MagicCapacity, EnergyPowerProjection, MagicPowerProjection;
        internal bool BeastMode, NguAutoAdvance;
        internal bool LazyItopodPurchased, LazyItopodEnabled;
        internal bool LazyItopodActive => LazyItopodPurchased && LazyItopodEnabled;
        internal float[] DropFactors = new float[0];
        internal float CubeLoot;
        internal double Gold;
        internal long ReservedEnergy;
        internal bool ObservedCurrentPools;
        internal double[] PpPerKill;
        internal double[] ItopodBoostPerKill;
        internal double[] ItopodHp, ItopodDefense;
        // Execution observations contain one acquired floor, not a floor-indexed forecast.
        internal int ObservedItopodFloor = -1, ObservedItopodMaximum;
        internal bool AcquiredRouteObservation;
        internal bool FarmingEconomicsCaptured;
        internal int AcquiredRouteZone = -1, AcquiredRouteFloor = -1;
        internal PresetNgu[] EnergyNgu = new PresetNgu[0], MagicNgu = new PresetNgu[0];
        internal PresetRitual[] Rituals = new PresetRitual[0];
        internal PresetZone[] Zones = new PresetZone[0];
        internal bool[] Available = new bool[LoadoutPresetPlanner.Keys.Length];
        internal string[] Reasons = new string[LoadoutPresetPlanner.Keys.Length];
        internal PresetActivityBar TimeMachineEnergy, TimeMachineMagic, WandoosEnergy, WandoosMagic;
        internal PresetActivityBar[] AdvancedTraining = new PresetActivityBar[0];
        internal double ATGoalWeight;
        internal int[] ATWeights = new int[0];
        internal long[] RequiredATTargets = new long[0];
        internal PresetAugment[] Augments = new PresetAugment[0];
        internal int WandoosOs;
        internal double AugmentGlobalFactor = 1;
        internal PresetSnapshot ActivityCopy() { return (PresetSnapshot)MemberwiseClone(); }
        internal PresetSnapshot ProgressionCopy()
        {
            var copy = (PresetSnapshot)MemberwiseClone();
            copy.EnergyNgu = CloneNgu(EnergyNgu); copy.MagicNgu = CloneNgu(MagicNgu);
            return copy;
        }
        private static PresetNgu[] CloneNgu(PresetNgu[] targets)
        { return targets.Select(x => new PresetNgu { Level = x.Level, Target = x.Target, HardCap = x.HardCap, Coefficient = x.Coefficient,
            SpeedDivider = x.SpeedDivider, TickDivider = x.TickDivider, TickFactors = x.TickFactors, Weight = x.Weight }).ToArray(); }
    }
    internal sealed class PresetRequest
    {
        internal double[] Weights;
        internal string[] EnergyPriorities, MagicPriorities;
        internal bool ResourcePrioritiesReviewed;
        // Additive manual gear utility: Push, adventure power, adventure toughness, drop, gold.
        internal double[] GearWeights = new double[5];
        internal int GearFightMode; // 0: sqrt(P*T)*move, 1: P*T*move, 2: (P+T)*move.
        internal int Horizon = 3600, Swap = 5, BloodPercent = -1, Zone = -1, DiggerPercent = 75;
        internal bool PlannedSupport;
        internal bool SetupOnly;
        // Only the copied reviewed setup request clips provisional spending; generic requests are unchanged.
        internal bool ObjectiveSetup;
        internal double MinimumPower, MinimumToughness;
        internal int[] BeardPriority = new int[0];
        internal int[] BeardRotation;
        internal double[] DiggerWeights = new double[0];
        internal int[] ATWeights = { 1, 1, 0, 0, 0 };
        internal PresetRequest Copy() { return (PresetRequest)MemberwiseClone(); }
    }
    internal sealed class PresetSupport
    {
        internal int[] Beards;
        internal SupportDigger[] Diggers;
        internal double[] DiggerWeights = new double[0];
        internal double Budget, Drain, Remaining;
        internal string[] Notes;
    }
    internal sealed class PresetCandidate
    {
        internal string Key, Label;
        internal PresetItem[] Items;
        internal double?[] Metrics = new double?[LoadoutPresetPlanner.Keys.Length];
        internal PresetActivityAllocation Activities;
        internal PresetLaneFunding PriorityFunding;
        internal double ActivityGoldSpent;
        internal double? Score;
        // Objective-only setup rank; Score and Metrics always describe actual funded production.
        internal double? SetupScore;
        // Imported retained decisions have no search eligibility/rank to replay.
        internal bool? SetupEligible;
        internal int BloodPercent, Ritual = -1, Zone = -1, Floor = -1, Evaluations = 1;
        // Acquired eligibility witnesses let execution check weighted zero-yield
        // farming goals without enumerating replacement routes.
        internal int RegularEligibilityZone = -1, BoostEligibilityZone = -1;
        internal long Energy, MagicNgu, Blood;
        internal double Hits;
        internal long[] EnergyShares, MagicShares;
        // Empty objective-owned lanes stay controlled; explicit reviewed tokens also acquire control.
        internal bool ControlsEnergy, ControlsMagic;
        internal long EnergyPool, MagicPool;
        internal double EnergyNguPower, MagicNguPower, MagicPower, BloodBonus;
        internal double NguBonus, EnergyDiggerBonus, MagicDiggerBonus, GrossGold;
        // Retain evaluated instant totals even when the corresponding public objectives are unavailable.
        internal double GearPower, GearToughness, GearDrop;
        internal double?[] RawMetrics;
        internal double ConstraintFitness;
        internal bool Feasible;
        internal bool SetupChanged;
        internal PresetSupport Support;
        internal string[] Warnings = new string[0];
        internal PresetCandidate Copy()
        {
            var copy = (PresetCandidate)MemberwiseClone();
            // Application observations must not redefine the acquired support choice.
            if (Support != null) copy.Support = new PresetSupport {
                Beards = Support.Beards?.ToArray(),
                Diggers = Support.Diggers?.Select(x => new SupportDigger { Id = x.Id, Name = x.Name,
                    Maximum = x.Maximum, Level = x.Level, BaseDrain = x.BaseDrain, Growth = x.Growth,
                    StartBonus = x.StartBonus, PerLevel = x.PerLevel, ConsumesGold = x.ConsumesGold }).ToArray(),
                DiggerWeights = Support.DiggerWeights?.ToArray(), Notes = Support.Notes?.ToArray(),
                Budget = Support.Budget, Drain = Support.Drain, Remaining = Support.Remaining };
            return copy;
        }
    }
    internal sealed class PresetSequence
    {
        internal string First, Second;
        internal double FirstSeconds, SecondSeconds;
        internal int SwapSeconds;
        internal double?[] Metrics;
        internal double? Score, Improvement;
    }
    internal sealed class PresetPlan
    {
        internal PresetCandidate[] Candidates, Specialists;
        internal PresetSequence Sequence;
        internal double[] Normalization;
        internal string[] ReferenceKeys;
        internal double?[] ReferenceProduction;
        internal int Evaluated;
        internal long Milliseconds;
        internal bool Budget;
    }

    internal static partial class LoadoutPresetPlanner
    {
        internal static readonly string[] Keys = PresetDefinitions.ObjectiveKeys;
        internal static readonly string[] Labels = PresetDefinitions.ObjectiveLabels;
        internal static readonly string[] Units = { "levels/s", "levels/s", "blood/s", "progress/s", "boost points/s", "index/s", "levels/s", "bonus/s", "levels/s", "weighted levels/s", "power", "toughness", "energy", "magic" };
        internal const int ProductionGoalCount = 10;
        private const int EvaluationBudget = 6000;
        internal const int AllocationLookaheadSeconds = 5;

        internal static double[] NormalizeWeights(double[] weights)
        {
            if (weights == null || weights.Length != 6 && weights.Length != ProductionGoalCount && weights.Length != 12 && weights.Length != Keys.Length || weights.Any(x => !Finite(x) || x < 0) || !weights.Any(x => x > 0))
                throw new ArgumentException("Six, ten, twelve, or fourteen finite nonnegative weights with at least one positive goal are required.");
            var max = weights.Max();
            var sum = weights.Sum(x => x / max);
            return Pad(weights.Select(x => x / max / sum).ToArray(), 0d);
        }

        private static T[] Pad<T>(T[] values, T missing)
        { return Enumerable.Range(0, Keys.Length).Select(i => values != null && i < values.Length ? values[i] : missing).ToArray(); }

        internal static bool CapacityOnly(double[] weights)
        { return weights != null && weights.Length == Keys.Length && weights.Skip(12).Any(x => x > 0) && weights.Take(12).All(x => x == 0); }

        internal static void RequireSupportedNguAdvance(PresetSnapshot s)
        {
            // A target may finish during the horizon and native code can transfer into excluded rows.
            // Until those transfers are modeled, do not claim any finite weighted target is safe.
            if (s.NguAutoAdvance && s.EnergyNgu.Concat(s.MagicNgu).Any(x => x.Weight > 0 && x.Target > 0 && x.Level < Math.Min(x.Target, x.HardCap)))
                throw new InvalidOperationException("Preset NGU forecasts do not support native auto-advance with unfinished finite weighted targets. Disable NGU auto-advance or use unlimited (zero) targets, then preview again. No preference was changed.");
        }

        internal static PresetPlan Plan(PresetSnapshot s, PresetRequest r, Stopwatch clock = null)
        {
            var weights = NormalizeWeights(r.Weights);
            if (!CapacityOnly(weights) || ReviewedNguFunding(r)) RequireSupportedNguAdvance(s);
            var reviewed = r.ResourcePrioritiesReviewed ? r : null;
            if (reviewed != null)
            {
                CustomLoadoutPreset.ValidateResourcePriorities(r.EnergyPriorities, r.MagicPriorities, true);
                // All setup search, including specialist/reference repairs, uses objectives alone.
                r = ObjectiveRequest(r);
            }
            if (r.Horizon < 1 || r.Horizon > 604800 || r.Swap < 0 || r.Swap > 3600 || r.BloodPercent < -1 || r.BloodPercent > 100)
                throw new ArgumentException("Invalid horizon, swap duration, or magic split.");
            if (s.Current.Length != s.Slots.Length || s.Factors.Length != s.Slots.Length || !Legal(s, s.Current))
                throw new ArgumentException("Current equipment snapshot is not legal.");
            clock = clock ?? Stopwatch.StartNew();
            var result = new PresetPlan { Normalization = Enumerable.Repeat(1d, Keys.Length).ToArray() };
            var norms = result.Normalization;
            var referenceRequest = r.Copy(); referenceRequest.GearWeights = new double[5];
            var current = Evaluate(s, referenceRequest, s.Current, weights, norms, "current", "Current gear", null, clock);
            result.Evaluated += current.Evaluations;
            for (var g = 0; g < Keys.Length; g++) norms[g] = current.Metrics[g] ?? 0;
            var specialists = new PresetCandidate[Keys.Length];
            // Fixed per-start quota prevents an early specialist from exhausting the whole pass.
            // Requested capacity lanes must get their greedy seed before unrelated production searches.
            foreach (var g in Enumerable.Range(0, Keys.Length).OrderBy(g => g >= 12 && weights[g] > 0 ? 0 : 1))
            {
                if (r.SetupOnly && weights[g] <= 0) { specialists[g] = current.Copy(); continue; }
                var focus = new double[Keys.Length]; focus[g] = 1;
                var focusRequest = referenceRequest.Copy();
                if (g == 1) focusRequest.BloodPercent = 0;
                if (g == 2) focusRequest.BloodPercent = 100;
                var fallback = Expired(result, clock) && !(g >= 12 && weights[g] > 0);
                var unavailable = g >= 6 && !Pad(s.Available, false)[g];
                var c = fallback ? current.Copy() : unavailable
                    ? Evaluate(s, focusRequest, s.Current, focus, norms, null, null)
                    : Search(s, focusRequest, s.Current, focus, norms, result, clock, g >= 6 ? 160 : 550);
                if (!fallback && unavailable) result.Evaluated += c.Evaluations;
                c.Key = "focus-" + Keys[g]; c.Label = Labels[g] + (fallback ? " budget fallback" : " full focus");
                if (fallback) c.Warnings = c.Warnings.Concat(new[] { "Search budget exhausted; this reference retains the initial current-gear allocation policy." }).ToArray();
                if (g == 3 && r.Zone >= 0 && r.Weights[4] + r.Weights[5] > 0) c.Label = "ITOPOD reference (selected-zone constrained)";
                if (!fallback && (g == 1 || g == 2) && r.BloodPercent >= 0 && r.BloodPercent != focusRequest.BloodPercent)
                    c.Warnings = c.Warnings.Concat(new[] { "Full-focus reference overrides the preset's fixed magic/blood split." }).ToArray();
                specialists[g] = c;
            }
            // Keep positive feasible specialist references. Missing references get a bounded
            // original-policy seed pass, never an infeasible focus output or a mixed winner.
            result.ReferenceKeys = new string[Keys.Length]; result.ReferenceProduction = new double?[Keys.Length];
            for (var g = 0; g < Keys.Length; g++)
            {
                var reference = specialists.Where(c => c.Feasible && c.Metrics[g].HasValue).OrderByDescending(c => c.Metrics[g].Value).FirstOrDefault();
                result.ReferenceKeys[g] = reference?.Key; result.ReferenceProduction[g] = reference?.Metrics[g];
                norms[g] = result.ReferenceProduction[g] ?? 0;
            }
            var missingReferences = Enumerable.Range(0, Keys.Length).Where(g => norms[g] <= 0).ToArray();
            if (missingReferences.Any(g => weights[g] > 0))
            {
                var allowed = new HashSet<string>(SupportAlternatives(s, r, weights).Select(support => new SupportSelection(support.Beards,
                    support.Diggers.Select(d => new SupportDiggerLevel(d.Id, d.Level)).ToArray()).StableKey));
                var references = new List<PresetCandidate>();
                foreach (var seed in new[] { current }.Concat(specialists))
                {
                    var supports = new[] { current.Support, seed.Support }.Where(support => allowed.Contains(new SupportSelection(support.Beards,
                        support.Diggers.Select(d => new SupportDiggerLevel(d.Id, d.Level)).ToArray()).StableKey))
                        .GroupBy(support => new SupportSelection(support.Beards, support.Diggers.Select(d => new SupportDiggerLevel(d.Id, d.Level)).ToArray()).StableKey)
                        .Select(group => group.First()).ToArray();
                    for (var i = 0; i < supports.Length; i++)
                    {
                        // At most 2 * (Keys.Length + 1) score-only evaluations, even
                        // after expiration. All use the same provisional denominators.
                        references.Add(EvaluateSelection(s, referenceRequest, seed.Items, weights, norms,
                            "reference-seed-" + seed.Key + "-" + i, null, null, supports[i]));
                        result.Evaluated++;
                    }
                }
                foreach (var g in missingReferences)
                {
                    var reference = references.Where(c => c.Feasible && c.Metrics[g].HasValue && Finite(c.Metrics[g].Value) && c.Metrics[g].Value > 0)
                        .OrderByDescending(c => c.Metrics[g].Value).FirstOrDefault();
                    if (reference == null) continue;
                    result.ReferenceKeys[g] = reference.Key; result.ReferenceProduction[g] = reference.Metrics[g];
                    norms[g] = reference.Metrics[g].Value;
                }
            }
            // Freeze BEFORE mixed optimization; replay/Apply never rebuild these references.
            var mixed = Expired(result, clock) ? current.Copy() : Search(s, r, s.Current, weights, norms, result, clock, 900);
            foreach (var seed in specialists.Where((c, g) => !r.SetupOnly || weights[g] > 0))
            {
                if (Expired(result, clock)) break;
                var trial = Search(s, r, seed.Items, weights, norms, result, clock, 400);
                if (Rank(s, r, trial, weights, norms) > Rank(s, r, mixed, weights, norms)) mixed = trial;
            }
            var currentLabel = r.PlannedSupport ? "Current gear / planned support" : "Current gear / current support";
            current = Expired(result, clock)
                ? EvaluateSelection(s, r, s.Current, weights, norms, "current", currentLabel, null, current.Support)
                : Evaluate(s, r, s.Current, weights, norms, "current", currentLabel, null, clock);
            result.Evaluated += current.Evaluations;
            if (Rank(s, r, current, weights, norms) > Rank(s, r, mixed, weights, norms))
                mixed = current.Copy();
            mixed.Score = CandidateScore(s, r, mixed, weights, norms);
            // Always retain found gear seeds, even after search expires. At most Keys.Length * 3 score-only trials;
            // regenerate the original support eligibility so focus-only allocations cannot leak into balanced.
            Func<PresetSupport, string> supportKey = support => new SupportSelection(support.Beards,
                support.Diggers.Select(d => new SupportDiggerLevel(d.Id, d.Level)).ToArray()).StableKey;
            var allowedSupport = new HashSet<string>(SupportAlternatives(s, r, weights).Select(supportKey));
            foreach (var seed in specialists.Where((c, g) => !r.SetupOnly || weights[g] > 0))
            foreach (var support in new[] { current.Support, mixed.Support, seed.Support }.Where(x => allowedSupport.Contains(supportKey(x)))
                .GroupBy(supportKey).Select(g => g.First()))
            {
                var trial = EvaluateSelection(s, r, seed.Items, weights, norms, null, null, null, support);
                result.Evaluated++;
                if (trial.Score.HasValue && (!mixed.Score.HasValue || trial.Score.Value > mixed.Score.Value)) mixed = trial;
            }
            mixed.Key = "balanced"; mixed.Label = "Weighted balanced";
            foreach (var c in specialists) c.Score = CandidateScore(s, r, c, weights, norms);
            result.Specialists = specialists;
            result.Candidates = new[] { current, mixed };
            result.Sequence = reviewed != null || r.SetupOnly ? null : Sequence(s, r, result.Candidates.Concat(result.Specialists).ToArray(), weights, norms, clock, result);
            if (reviewed != null)
            {
                result.Candidates = Array.ConvertAll(result.Candidates, c => FundSetup(s, reviewed, c, weights, norms));
                result.Specialists = Array.ConvertAll(result.Specialists, c => FundSetup(s, reviewed, c, weights, norms));
                result.Evaluated += result.Candidates.Length + result.Specialists.Length;
            }
            result.Milliseconds = clock.ElapsedMilliseconds;
            return result;
        }

        private static bool Expired(PresetPlan p, Stopwatch clock)
        {
            if (p.Evaluated >= EvaluationBudget || clock.ElapsedMilliseconds >= 1800) p.Budget = true;
            return p.Budget;
        }

        internal static PresetPlan Replay(PresetSnapshot s, PresetRequest request, PresetPlan cached, Stopwatch clock = null)
        {
            clock = clock ?? Stopwatch.StartNew();
            var weights = NormalizeWeights(request.Weights);
            if (!CapacityOnly(weights) || ReviewedNguFunding(request)) RequireSupportedNguAdvance(s);
            var emptySlots = s.Current.Select((x, i) => x.Id == 0 ? x
                : new PresetItem { SourceSlot = s.Slots[i], Group = Group(s.Slots[i]), Name = "Empty" }).ToArray();
            Func<PresetCandidate, PresetCandidate> replay = policy => {
                if (clock.ElapsedMilliseconds >= 3000) throw new InvalidOperationException("Preset replay exceeded its operation time budget; preview again.");
                if (policy.Items.Length != s.Slots.Length) throw new InvalidOperationException("Preview gear slot count changed; preview again.");
                var items = policy.Items.Select((x, i) => {
                    var actual = x.Id == 0 ? emptySlots[i] : s.Items.SingleOrDefault(y => y.SourceSlot == x.SourceSlot);
                    if (actual == null || actual.Id != x.Id || actual.Level != x.Level || actual.Group != x.Group || actual.Group != Group(s.Slots[i]) || !actual.Stats.SequenceEqual(x.Stats)
                        || !actual.ResourceSpecs.SequenceEqual(x.ResourceSpecs) || !actual.LootSpecs.SequenceEqual(x.LootSpecs))
                        throw new InvalidOperationException("Exact preview gear is missing or changed; preview again.");
                    return actual;
                }).ToArray();
                var candidate = Evaluate(s, request, items, weights, cached.Normalization, policy.Key, policy.Label, policy);
                candidate.Warnings = candidate.Warnings.Concat(policy.Warnings).Distinct().ToArray();
                return candidate;
            };
            var plan = new PresetPlan { Normalization = Pad(cached.Normalization, 1d), ReferenceKeys = Pad(cached.ReferenceKeys, (string)null),
                ReferenceProduction = Pad(cached.ReferenceProduction, (double?)null), Candidates = cached.Candidates.Select(replay).ToArray(),
                Specialists = cached.Specialists.Select(replay).ToArray(), Evaluated = cached.Candidates.Length + cached.Specialists.Length };
            plan.Sequence = Sequence(s, request, plan.Candidates.Concat(plan.Specialists).ToArray(), weights, plan.Normalization, clock, plan);
            return plan;
        }

        private static PresetCandidate Search(PresetSnapshot s, PresetRequest r, PresetItem[] seed, double[] weights,
            double[] norms, PresetPlan plan, Stopwatch clock, int quota)
        {
            var limit = plan.Evaluated + quota;
            var supports = SupportAlternatives(s, r, weights);
            var finalists = new List<PresetCandidate>();
            Func<PresetItem[], bool, PresetCandidate> evaluate = (items, full) => {
                PresetCandidate winner = null;
                var choices = full || finalists.Count == 0 ? supports : finalists.Select(c => c.Support).ToArray();
                if (full) finalists.Clear();
                foreach (var support in choices)
                {
                    if (winner != null && (plan.Evaluated >= limit || Expired(plan, clock))) break;
                    var trial = EvaluateSelection(s, r, items, weights, norms, null, null, null, support); plan.Evaluated++;
                    if (winner == null || Rank(s, r, trial, weights, norms) > Rank(s, r, winner, weights, norms) + 1e-12) winner = trial;
                    if (full) finalists.Add(trial);
                }
                if (full) finalists = finalists.OrderByDescending(c => Rank(s, r, c, weights, norms))
                    .GroupBy(c => new SupportSelection(c.Support.Beards, c.Support.Diggers.Select(d => new SupportDiggerLevel(d.Id, d.Level)).ToArray()).StableKey)
                    .Select(g => g.First()).Take(3).ToList();
                return winner;
            };
            var gear = seed.ToArray();
            var best = evaluate(gear, true);
            var original = Totals(s, s.Current);
            // A raw union-stat greedy seed crosses tick/kill-threshold plateaus that single swaps cannot.
            var greedy = s.Current.ToArray(); var used = new HashSet<int>();
            var capacityOnly = CapacityOnly(weights);
            // Strongest cap item/instance goes in the full-strength hand, not the first captured slot.
            var slotOrder = Enumerable.Range(0, greedy.Length).OrderByDescending(i => capacityOnly ? s.Slots[i] == -5 ? 1 : s.Factors[i] : 0);
            foreach (var slot in slotOrder)
            {
                if (!capacityOnly && Expired(plan, clock)) break;
                var pick = s.Items.Where(x => x.Group == Group(s.Slots[slot]) && !used.Contains(x.Id))
                    .OrderByDescending(x => SeedValue(x, weights, s, r, original)).ThenBy(x => x.Id).ThenBy(x => x.SourceSlot).FirstOrDefault();
                if (pick != null) greedy[slot] = pick;
                else if (capacityOnly) greedy[slot] = new PresetItem { SourceSlot = s.Slots[slot], Group = Group(s.Slots[slot]), Name = "Empty" };
                if (greedy[slot].Id != 0) used.Add(greedy[slot].Id);
            }
            if (Legal(s, greedy) && (capacityOnly || !Expired(plan, clock)))
            {
                var seedFinalists = finalists.ToList();
                var trial = evaluate(greedy, true);
                if (Rank(s, r, trial, weights, norms) > Rank(s, r, best, weights, norms)) { best = trial; gear = greedy; }
                else finalists = seedFinalists;
            }
            // Bounded deterministic coordinate ascent, followed by pair-order swaps for offhand/accessories.
            for (var pass = 0; pass < 3 && plan.Evaluated < limit && !Expired(plan, clock); pass++)
            {
                var improved = false;
                for (var slot = 0; slot < gear.Length && plan.Evaluated < limit && !Expired(plan, clock); slot++)
                {
                    // A bounded union-stat shortlist, not a dominance proof. Include combat constraints in every seed.
                    var options = s.Items.Where(x => x.Group == Group(s.Slots[slot]))
                        .OrderByDescending(x => SeedValue(x, weights, s, r, original)).ThenBy(x => x.Id).ThenBy(x => x.SourceSlot)
                        .Take(Math.Max(4, quota / Math.Max(1, 2 * gear.Length)));
                    foreach (var item in options)
                    {
                        if (plan.Evaluated >= limit || Expired(plan, clock)) break;
                        if (gear.Where((x, i) => i != slot).Any(x => x.Id != 0 && x.Id == item.Id)) continue;
                        var previous = gear[slot]; gear[slot] = item;
                        var trial = evaluate(gear, false);
                        if (Rank(s, r, trial, weights, norms) > Rank(s, r, best, weights, norms) + 1e-12)
                        { best = trial; improved = true; }
                        else gear[slot] = previous;
                    }
                }
                for (var a = 0; a < gear.Length; a++)
                for (var b = a + 1; b < gear.Length && plan.Evaluated < limit && !Expired(plan, clock); b++)
                {
                    if (Group(s.Slots[a]) != Group(s.Slots[b])) continue;
                    var previous = gear[a]; gear[a] = gear[b]; gear[b] = previous;
                    var trial = evaluate(gear, false);
                    if (Rank(s, r, trial, weights, norms) > Rank(s, r, best, weights, norms) + 1e-12) { best = trial; improved = true; }
                    else { gear[b] = gear[a]; gear[a] = previous; }
                }
                // Reopen the bounded support portfolio after gear/route thresholds move, not a Cartesian gear product.
                if (r.PlannedSupport && plan.Evaluated < limit && !Expired(plan, clock))
                {
                    var trial = evaluate(gear, true);
                    if (Rank(s, r, trial, weights, norms) > Rank(s, r, best, weights, norms) + 1e-12) { best = trial; improved = true; }
                }
                if (!improved) break;
            }
            return best;
        }

        private static double SeedValue(PresetItem item, double[] w, PresetSnapshot s, PresetRequest r, double[] current)
        {
            var v = item.Stats;
            Func<PresetStat, double> relative = stat => v[(int)stat] / Math.Max(1, 100 + current[(int)stat]);
            var capacity = 0d;
            if (w[12] + w[13] > 0)
            {
                // Use raw native resource tiers, including AllCap, rather than the legacy union stat.
                var bonuses = ResourceProjection.NormalizeRaw(item.ResourceSpecs);
                capacity = w[12] * (bonuses[(int)ResourceSpec.EnergyCap] + (double)bonuses[(int)ResourceSpec.EnergyCap3] + bonuses[(int)ResourceSpec.AllCap])
                    + w[13] * (bonuses[(int)ResourceSpec.MagicCap] + (double)bonuses[(int)ResourceSpec.MagicCap3] + bonuses[(int)ResourceSpec.AllCap]);
            }
            var value = capacity
                + w[0] * (relative(PresetStat.EnergyCap) + relative(PresetStat.EnergyPower) + relative(PresetStat.NguSpeed))
                + w[1] * (relative(PresetStat.MagicCap) + relative(PresetStat.MagicPower) + relative(PresetStat.NguSpeed))
                + w[2] * (relative(PresetStat.MagicCap) + relative(PresetStat.MagicPower))
                + (w[3] + w[4] + w[5]) * (v[0] / Math.Max(1, s.BasePower + current[0]) + relative(PresetStat.Respawn))
                + (w[4] + w[5]) * relative(PresetStat.Drop)
                + (w[6] + w[7]) * relative(PresetStat.EnergyPower)
                + w[6] * relative(PresetStat.MagicPower)
                + w[7] * relative(PresetStat.AugmentSpeed)
                + w[8] * relative(PresetStat.WandoosSpeed)
                + w[9] * (relative(PresetStat.AdvancedTrainingSpeed) + .5 * relative(PresetStat.EnergyPower))
                + w[10] * v[0] / Math.Max(1, s.BasePower + current[0])
                + w[11] * v[1] / Math.Max(1, s.BaseToughness + current[1])
                + (r.MinimumToughness > 0 || w[4] + w[5] > 0 ? v[1] / Math.Max(1, s.BaseToughness + current[1]) : 0);
            var blend = GearBlend(r);
            if (blend == null) return value;
            var power = v[0] / Math.Max(1, s.BasePower + current[0]);
            var toughness = v[1] / Math.Max(1, s.BaseToughness + current[1]);
            return blend[0] * value + blend[1] * (power + toughness + relative(PresetStat.MoveCooldown))
                + blend[2] * power + blend[3] * toughness + blend[4] * relative(PresetStat.Drop) + blend[5] * relative(PresetStat.Gold);
        }

        internal static int Group(int slot) { return slot == -6 ? -5 : slot >= 10000 ? 10000 : slot; }
        internal static bool Legal(PresetSnapshot s, PresetItem[] items)
        {
            return items.Length == s.Slots.Length && items.Select((x, i) => x != null && x.Group == Group(s.Slots[i])).All(x => x)
                && items.Where(x => x.Id != 0).Select(x => x.Id).Distinct().Count() == items.Count(x => x.Id != 0)
                && items.Where(x => x.Id != 0).Select(x => x.SourceSlot).Distinct().Count() == items.Count(x => x.Id != 0);
        }

        internal static double[] Totals(PresetSnapshot s, PresetItem[] items)
        {
            var totals = new double[21];
            // InventoryController sums weapon first, then armor, offhand, and accessories as floats.
            var weapon = Array.IndexOf(s.Slots, -5);
            if (weapon >= 0) { totals[0] = (float)items[weapon].Stats[0]; totals[1] = (float)items[weapon].Stats[1]; }
            for (var i = 0; i < items.Length; i++)
            {
                if (i != weapon)
                    for (var stat = 0; stat < 2; stat++) totals[stat] = (float)((float)totals[stat] + (float)((float)items[i].Stats[stat] * (float)s.Factors[i]));
                for (var stat = 2; stat < totals.Length; stat++) totals[stat] += items[i].Stats[stat] * s.Factors[i];
            }
            return totals;
        }

        internal static float[] ResourceBonuses(PresetSnapshot s, PresetItem[] items)
        {
            return ResourceProjection.EquipmentBonuses(s.Slots, (i, stat) => items[i].ResourceSpecs[stat],
                i => (float)s.Factors[i], nativePrefix: false);
        }

        internal static double NguBonus(PresetSnapshot s, float[] bonuses, int[] beards = null)
        {
            var value = (float)(1f + bonuses[(int)ResourceSpec.NGU] + bonuses[(int)ResourceSpec.NGU2]);
            for (var i = 0; i < s.NguFactors.Length; i++) value = (float)(value * (i == 5 && beards != null ? BeardBonus(s, beards, 0) : s.NguFactors[i]));
            return value;
        }

        internal static float BeardBonus(PresetSnapshot s, int[] beards, int index)
        {
            return (float)SupportProjection.BeardBonus(s, beards, new[] { 3, 6, 5, 1 }[index]);
        }

        internal static double GrossGold(PresetSnapshot s, int[] beards)
        {
            return SupportProjection.GrossGold(s, beards);
        }

        internal static double Drop(PresetSnapshot s, PresetItem[] items, int[] beards, double digger)
        {
            if (s.BeardEffective != null && items.SequenceEqual(s.Current) && beards.Contains(1) == s.CurrentBeards.Contains(1)
                && digger == DiggerBonus(s, s.CurrentDiggers, 0)) return s.Drop;
            if (s.DropFactors.Length == 0)
                return s.Drop * (s.DropGearBase + Totals(s, items)[(int)PresetStat.Drop]) / Math.Max(1e-100, s.DropGearBase + Totals(s, s.Current)[(int)PresetStat.Drop])
                    * BeardBonus(s, beards, 3) / Math.Max(1f, BeardBonus(s, s.CurrentBeards, 3)) * digger / DiggerBonus(s, s.CurrentDiggers, 0);
            var raw = new float[2];
            var weapon = Array.IndexOf(s.Slots, -5);
            if (weapon >= 0) Array.Copy(items[weapon].LootSpecs, raw, raw.Length);
            for (var i = 0; i < items.Length; i++) if (i != weapon)
                for (var j = 0; j < raw.Length; j++) raw[j] = (float)(raw[j] + (float)(items[i].LootSpecs[j] * (float)s.Factors[i]));
            var gear = (float)(1f + raw[0] / 1000f + raw[1] / 1000000f + s.CubeLoot);
            var value = 1f;
            for (var i = 0; i < s.DropFactors.Length; i++)
                value = (float)(value * (i == 2 ? gear : i == 6 ? BeardBonus(s, beards, 3) : i == 7 ? (float)digger : s.DropFactors[i]));
            return value;
        }

        internal static double AdventureStat(double basis, double cube, double gear, double[] factors, double digger, double beard = double.NaN)
        {
            // Keep the native F-stack prefix through the cube comparison and first five multipliers.
            // Character then stores its float local after each subsequent multiplier.
            var threshold = (float)basis + (double)(float)gear;
            var effectiveCube = (float)cube > threshold ? (float)(threshold + (float)Math.Pow((float)((float)cube - threshold), .5)) : (float)cube;
            var value = (float)basis + (double)(float)gear + effectiveCube;
            for (var i = 0; i < factors.Length; i++)
            {
                value *= i == 4 ? digger : i == 3 && !double.IsNaN(beard) ? beard : factors[i];
                if (i >= 4) value = (float)value;
            }
            return double.IsNaN(value) ? 0 : (float)Math.Min(1e36f, Math.Max(1f, value));
        }

        internal static double AdventureRegen(double baseToughness, double baseRegen, double cubeToughness,
            double gearToughness, double[] toughnessFactors)
        {
            // Cube softcap uses toughness, never base regen. Keep the native F-stack prefix
            // through both 0.03f products and the first five multipliers, then round stores.
            var threshold = (float)baseToughness + (double)(float)gearToughness;
            var cube = (float)cubeToughness > threshold
                ? (float)(threshold + (float)Math.Pow((float)((float)cubeToughness - threshold), .5)) : (float)cubeToughness;
            var value = (float)baseRegen + (float)gearToughness * (double).03f + cube * (double).03f;
            for (var i = 0; i < toughnessFactors.Length; i++)
            {
                value *= toughnessFactors[i];
                if (i >= 4) value = (float)value;
            }
            return double.IsNaN(value) ? 0 : (float)Math.Min(1e36f, Math.Max(.1f, value));
        }

        internal static bool AdventureRegenParity(double projected, double actual)
        {
            if (!Finite(projected) || !Finite(actual) || projected <= 0 || actual <= 0) return false;
            if (projected == actual) return true;
            // Same fourteen-factor policy as AdventureParity, with regen's own clamp boundary.
            if (projected <= .1f || actual <= .1f || projected >= 1e36f || actual >= 1e36f) return false;
            var p = new FloatBits { Value = (float)projected }; var a = new FloatBits { Value = (float)actual };
            return Math.Abs((long)p.Bits - a.Bits) <= 7;
        }

        internal static bool AdventureParity(double projected, double actual)
        {
            if (!Finite(projected) || !Finite(actual) || projected <= 0 || actual <= 0) return false;
            if (projected == actual) return true;
            // Native getter/local F precision depends on JIT call boundaries. Seven binary32
            // ULPs is a bounded acceptance policy for this fourteen-multiplier path, not a
            // universal error bound. Never absorb clamps or reuse it for other getters.
            if (projected <= 1 || actual <= 1 || projected >= 1e36f || actual >= 1e36f) return false;
            var p = new FloatBits { Value = (float)projected }; var a = new FloatBits { Value = (float)actual };
            return Math.Abs((long)p.Bits - a.Bits) <= 7;
        }

        internal static PresetCandidate Evaluate(PresetSnapshot s, PresetRequest r, PresetItem[] items, double[] weights,
            double[] normalization, string key, string label, PresetCandidate policy = null, Stopwatch clock = null)
        {
            weights = Pad(weights, 0d); normalization = Pad(normalization, 1d);
            if (policy != null)
            {
                var replay = EvaluateSelection(s, r, items, weights, normalization, key, label, policy, policy.Support);
                replay.SetupScore = policy.SetupScore;
                replay.SetupEligible = policy.SetupEligible;
                if (r.ResourcePrioritiesReviewed)
                {
                    if (policy.SetupEligible == false)
                    {
                        replay.Feasible = false; replay.Score = null;
                        replay.Warnings = replay.Warnings.Concat(new[] { "Acquired setup has no eligible objective-only score; preview again." }).ToArray();
                    }
                    replay.RegularEligibilityZone = policy.RegularEligibilityZone;
                    replay.BoostEligibilityZone = policy.BoostEligibilityZone;
                }
                return replay;
            }
            if (r.ResourcePrioritiesReviewed)
            {
                CustomLoadoutPreset.ValidateResourcePriorities(r.EnergyPriorities, r.MagicPriorities, true);
                var setup = Evaluate(s, ObjectiveRequest(r), items, weights, normalization, key, label, null, clock);
                return FundSetup(s, r, setup, weights, normalization);
            }
            PresetCandidate best = null;
            var choices = SupportAlternatives(s, r, weights);
            var evaluated = 0;
            foreach (var support in choices)
            {
                if (best != null && clock != null && clock.ElapsedMilliseconds >= 1800) break;
                var trial = EvaluateSelection(s, r, items, weights, normalization, key, label, null, support);
                evaluated++;
                if (best == null || Rank(s, r, trial, weights, normalization) > Rank(s, r, best, weights, normalization) + 1e-12) best = trial;
            }
            best.Evaluations = evaluated;
            return best;
        }

        private static PresetCandidate EvaluateSelection(PresetSnapshot s, PresetRequest r, PresetItem[] items, double[] weights,
            double[] normalization, string key, string label, PresetCandidate policy, PresetSupport choice,
            PresetActivityAllocation acquiredActivities = null)
        {
            weights = Pad(weights, 0d); normalization = Pad(normalization, 1d);
            s = s.ActivityCopy(); s.Available = Pad(s.Available, false); s.Reasons = Pad(s.Reasons, (string)null);
            CustomLoadoutPreset.ValidateResourcePriorities(r.EnergyPriorities, r.MagicPriorities, r.ResourcePrioritiesReviewed);
            if (ReviewedNguFunding(r)) RequireSupportedNguAdvance(s);
            s.Available[6] &= s.TimeMachineEnergy != null && s.TimeMachineMagic != null
                && (s.TimeMachineEnergy.Unlocked || s.TimeMachineMagic.Unlocked);
            s.Available[7] &= s.Augments.Any(x => x.Augment != null && x.Upgrade != null && (x.Augment.Unlocked || x.Upgrade.Unlocked));
            s.Available[8] &= s.WandoosEnergy != null && s.WandoosMagic != null && (s.WandoosEnergy.Unlocked || s.WandoosMagic.Unlocked);
            s.Available[9] &= s.AdvancedTraining.Length == 5;
            if (r.ResourcePrioritiesReviewed)
                return EvaluatePriorities(s, r, items, weights, normalization, key, label, policy, choice, acquiredActivities);
            if (weights[6] + weights[7] + weights[8] + weights[9] > 0 || policy?.Activities != null)
                return EvaluateActivities(s, r, items, weights, normalization, key, label, policy, choice);
            var candidate = EvaluateCore(s, r, items, weights, normalization, key, label, policy, choice);
            for (var g = 6; g < ProductionGoalCount; g++) if (s.Available[g]) candidate.Metrics[g] = candidate.RawMetrics[g] = 0;
            if (MissingRequiredATFunding(s, null))
            {
                candidate.Feasible = false; candidate.Score = null;
                candidate.Warnings = candidate.Warnings.Concat(new[] { "Mandatory stage AT targets require positive allocations on every unfinished stat lane." }).ToArray();
            }
            return candidate;
        }

        private static bool MissingRequiredATFunding(PresetSnapshot snapshot, long[] allocations)
        {
            return snapshot.RequiredATTargets.Select((target, i) => target > 0
                && (i >= snapshot.AdvancedTraining.Length || snapshot.AdvancedTraining[i].Level < target)
                && (allocations == null || i >= allocations.Length || allocations[i] <= 0)).Any(x => x);
        }

        private static PresetCandidate EvaluateCore(PresetSnapshot s, PresetRequest r, PresetItem[] items, double[] weights,
            double[] normalization, string key, string label, PresetCandidate policy, PresetSupport choice, PresetLaneFunding funding = null)
        {
            var t = Totals(s, items); var original = Totals(s, s.Current);
            var resources = ProjectResources(s, items);
            var bonuses = resources.Bonuses; var ep = resources.EnergyPower; var mp = resources.MagicPower;
            var totalEnergyCap = resources.EnergyCap; var totalMagicCap = resources.MagicCap;
            var energyCap = resources.EnergyCapBudget;
            var energy = resources.Energy; var magic = resources.Magic;
            var support = new PresetSupport { Beards = choice.Beards.ToArray(), Diggers = choice.Diggers.ToArray(),
                DiggerWeights = choice.DiggerWeights.ToArray(), Notes = choice.Notes.ToArray() };
            var beards = support.Beards;
            var supportEvaluation = SupportProjection.Evaluate(s, new SupportSelection(beards,
                support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()), r.DiggerPercent / 100d, r.PlannedSupport);
            var grossGold = supportEvaluation.GrossGold;
            support.Budget = supportEvaluation.Budget; support.Drain = supportEvaluation.Drain;
            var changed = !items.SequenceEqual(s.Current) || !support.Beards.OrderBy(x => x).SequenceEqual(s.CurrentBeards.OrderBy(x => x))
                || !support.Diggers.OrderBy(x => x.Id).Select(x => x.Id + ":" + x.Level).SequenceEqual(s.CurrentDiggers.OrderBy(x => x.Id).Select(x => x.Id + ":" + x.Level));
            var duration = Math.Max(0, r.Horizon - (changed ? r.Swap : 0));
            Func<int, double> supportRatio = id => DiggerBonus(s, support.Diggers, id) / DiggerBonus(s, s.CurrentDiggers, id);
            var adventureDigger = DiggerBonus(s, support.Diggers, 3);
            var sameAdventureDigger = (support.Diggers.FirstOrDefault(d => d.Id == 3)?.Level ?? 0) == (s.CurrentDiggers.FirstOrDefault(d => d.Id == 3)?.Level ?? 0);
            var adventureBeard = beards.Contains(5) == s.CurrentBeards.Contains(5) ? double.NaN : BeardBonus(s, beards, 2);
            var power = AdventureStat(s.BasePower, s.CubePower, t[0], s.PowerFactors, sameAdventureDigger ? s.PowerFactors[4] : adventureDigger, adventureBeard);
            var toughness = AdventureStat(s.BaseToughness, s.CubeToughness, t[1], s.ToughnessFactors, sameAdventureDigger ? s.ToughnessFactors[4] : adventureDigger, adventureBeard);
            var drop = Drop(s, items, beards, DiggerBonus(s, support.Diggers, 0));
            var respawn = s.Respawn * Math.Max(.2, 1 - t[(int)PresetStat.Respawn] / 100) / Math.Max(.2, 1 - original[(int)PresetStat.Respawn] / 100);
            var nguBonus = NguBonus(s, bonuses, beards);
            var energyDigger = DiggerBonus(s, support.Diggers, 4); var magicDigger = DiggerBonus(s, support.Diggers, 5);
            var metrics = new double?[Keys.Length];
            if (s.Available[0]) metrics[0] = NguMetric(s.EnergyNgu, funding?.EnergyNgu ?? energy, ep, duration, nguBonus, energyDigger);
            var requestedSplit = policy == null ? r.BloodPercent : policy.BloodPercent;
            var bestSplit = requestedSplit >= 0 ? requestedSplit : 0;
            var bestValue = double.NegativeInfinity; var bestBlood = 0d; var ritual = -1; var bloodDrain = 0d;
            // Coarse deterministic shared-magic optimization; both E and M remain eligible in every phase.
            for (var percent = requestedSplit >= 0 ? requestedSplit : 0; funding == null && percent <= (requestedSplit >= 0 ? requestedSplit : 100); percent += 10)
            {
                var m = s.Available[1] ? NguMetric(s.MagicNgu, ResourcePercent(magic, 100 - percent), mp, duration, nguBonus, magicDigger) : null;
                var blood = 0d; var selected = -1; var drain = 0d;
                if (s.Available[2]) foreach (var ritualRow in s.Rituals.Where(x => x.Unlocked && (x.Paid || s.Gold >= x.Cost) && (policy == null || policy.Ritual == x.Id)))
                {
                    var fills = BloodFills(ritualRow.Coefficient * mp * ResourcePercent(magic, percent));
                    var cost = fills * ritualRow.Cost;
                    if (cost + support.Drain >= grossGold || cost < 0) continue;
                    var value = fills * ritualRow.Blood * supportRatio(10);
                    if (value <= blood) continue;
                    blood = value; selected = ritualRow.Id; drain = cost;
                }
                var utility = weights[1] * ScoringRatio(m ?? 0, normalization[1]) + weights[2] * ScoringRatio(blood, normalization[2]);
                if (utility <= bestValue) continue;
                bestValue = utility; bestSplit = percent; bestBlood = blood; ritual = selected; bloodDrain = drain; metrics[1] = m;
            }
            if (funding != null)
            {
                for (var g = 0; g < 3; g++) if (!s.Available[g]) metrics[g] = 0;
                if (s.Available[1]) metrics[1] = NguMetric(s.MagicNgu, funding.MagicNgu, mp, duration, nguBonus, magicDigger);
                var row = s.Available[2] ? s.Rituals.FirstOrDefault(x => x.Id == funding.Ritual && x.Unlocked && (x.Paid || s.Gold > x.Cost)) : null;
                if (funding.Blood > 0 && row != null)
                {
                    ritual = row.Id;
                    var fills = BloodFills(row.Coefficient * mp * funding.Blood);
                    bestBlood = fills * row.Blood * supportRatio(10); bloodDrain = fills * row.Cost;
                }
                bestSplit = magic > 0 ? (int)((decimal)funding.Blood * 100 / magic) : 0;
            }
            if (s.Available[2]) metrics[2] = bestBlood;
            support.Remaining = grossGold - support.Drain - bloodDrain;
            var selectedZone = r.Zone >= 0 && r.Weights[4] + r.Weights[5] > 0;
            // Funding refresh deliberately evaluates with no pinned allocation policy.
            // That must not unpin the independently acquired route or enumerate floors.
            var routeBound = policy != null || s.AcquiredRouteObservation;
            var routeZone = policy != null ? policy.Zone : s.AcquiredRouteZone;
            var routeFloor = policy != null ? policy.Floor : s.AcquiredRouteFloor;
            var zone = -1; var floor = -1; var hits = 0d; var pp = 0d; var boost = 0d; var zoneScore = 0d;
            var routeScore = double.NegativeInfinity;
            if (s.Available[3] && !s.BeastMode && !selectedZone && (!routeBound || routeZone == 1000))
            {
                var maximum = s.ObservedItopodFloor >= 0 ? s.ObservedItopodMaximum : s.PpPerKill.Length - 1;
                var nativeEnemies = s.ItopodHp != null && s.ItopodDefense != null && s.ItopodHp.Length == s.PpPerKill.Length && s.ItopodDefense.Length == s.PpPerKill.Length;
                var heuristicFloor = ItopodFloor(power, s.IdleModifier, maximum);
                // Without enemy data, compare only the existing one-hit range. Never invent multihit multipliers.
                var last = nativeEnemies && !s.LazyItopodActive ? maximum : heuristicFloor;
                var first = routeBound ? routeFloor : s.LazyItopodActive ? heuristicFloor : 1;
                for (var f = first; f >= 1 && f <= (routeBound ? routeFloor : last) && f <= last; f++)
                {
                    if (s.ObservedItopodFloor >= 0 && f != s.ObservedItopodFloor) continue;
                    var row = s.ObservedItopodFloor >= 0 ? 0 : f;
                    if (s.LazyItopodActive && f != heuristicFloor) continue;
                    var h = 1d;
                    if (nativeEnemies)
                    {
                        var damage = Math.Floor((float)((float)(Math.Max(0f, (float)power - (float)s.ItopodDefense[row] / 2f) * .8f) * (float)s.IdleModifier));
                        h = damage > 0 ? Math.Max(1, Math.Ceiling(s.ItopodHp[row] / damage)) : double.PositiveInfinity;
                        // Short idle fights only. Regeneration, paralysis, deaths and player health are not simulated.
                        if (h > 4 || !Finite(h)) { if (!routeBound) break; else continue; }
                    }
                    var seconds = Math.Max(.02, respawn + s.AttackSeconds * h);
                    var progress = s.PpPerKill[row] * supportRatio(8) / seconds;
                    var boosts = s.ItopodBoostPerKill != null && row < s.ItopodBoostPerKill.Length ? s.ItopodBoostPerKill[row] / seconds : 0;
                    var value = weights[3] * ScoringRatio(progress, normalization[3]) + weights[4] * ScoringRatio(boosts, normalization[4]);
                    if (value <= routeScore) continue;
                    routeScore = value; zone = 1000; floor = f; hits = h; pp = progress; boost = boosts;
                }
            }
            foreach (var z in s.Zones)
            {
                if (s.BeastMode || selectedZone && z.Id != r.Zone || routeBound && z.Id != routeZone) continue;
                if (power < z.IdlePower || toughness < z.IdleToughness || z.OneHitPower <= 0) continue;
                var h = Math.Max(1, Math.Ceiling(z.OneHitPower / Math.Max(1e-100, power * s.IdleModifier / 1.2)));
                var kills = 1 / Math.Max(.02, respawn + s.AttackSeconds * h);
                var loot = z.Id >= 20 ? Math.Pow(Math.Max(0, drop), 1d / 3) : drop;
                var index = r.Zone < 0 || r.Zone == z.Id ? loot * kills : 0;
                var value = 0d;
                for (var i = 0; i < z.BoostValues.Length; i++) value += z.BoostValues[i] * Math.Min(z.Caps[i], z.Chances[i] * loot);
                var boostRate = value * z.NormalRatio * kills;
                var score = weights[4] * ScoringRatio(boostRate, normalization[4]) + weights[5] * ScoringRatio(index, normalization[5]);
                if (score <= routeScore) continue;
                routeScore = score; boost = boostRate; zoneScore = index; zone = z.Id; floor = -1; hits = h; pp = 0;
            }
            if (s.Available[3]) metrics[3] = pp;
            if (s.Available[4]) metrics[4] = boost;
            if (s.Available[5]) metrics[5] = zoneScore;
            if (s.Available[10]) metrics[10] = power;
            if (s.Available[11]) metrics[11] = toughness;
            if (s.Available[12]) metrics[12] = totalEnergyCap;
            if (s.Available[13]) metrics[13] = totalMagicCap;
            var feasible = Legal(s, items) && energyCap >= s.ReservedEnergy && power >= r.MinimumPower && toughness >= r.MinimumToughness
                && (weights[3] + weights[4] + weights[5] <= 0 || zone >= 0) && (!selectedZone || zone == r.Zone) && support.Remaining >= 0
                && (policy == null || policy.Ritual < 0 || ritual == policy.Ritual)
                && (!routeBound || routeZone != 1000 || zone == 1000 && floor == routeFloor)
                && supportEvaluation.Feasible;
            if (funding != null) feasible &= funding.EnergyNgu <= energy && funding.MagicNgu <= magic
                && funding.Blood <= magic - funding.MagicNgu && (funding.Blood == 0 || ritual == funding.Ritual)
                && (funding.EnergyNgu == 0 || s.Available[0]) && (funding.MagicNgu == 0 || s.Available[1])
                && (funding.Blood == 0 || bloodDrain + support.Drain < grossGold);
            var constraintFitness = Math.Min(1, power / Math.Max(1e-100, r.MinimumPower)) * Math.Min(1, toughness / Math.Max(1e-100, r.MinimumToughness));
            if (energyCap < s.ReservedEnergy) constraintFitness *= energyCap / (double)s.ReservedEnergy;
            if (weights[3] + weights[4] + weights[5] > 0 && zone < 0)
                constraintFitness *= s.Zones.Select(z => Math.Min(1, power / Math.Max(1e-100, z.IdlePower))
                    * Math.Min(1, toughness / Math.Max(1e-100, z.IdleToughness))).DefaultIfEmpty(0).Max();
            if (support.Remaining < 0) constraintFitness /= 1 + Math.Abs(support.Remaining) / Math.Max(1, grossGold);
            var energyShares = NguShares(s.EnergyNgu, s.Available[0] ? funding?.EnergyNgu ?? energy : 0, ep, AllocationLookaheadSeconds, nguBonus, energyDigger);
            var magicShares = NguShares(s.MagicNgu, s.Available[1] ? funding?.MagicNgu ?? ResourcePercent(magic, 100 - bestSplit) : 0, mp, AllocationLookaheadSeconds, nguBonus, magicDigger);
            var c = new PresetCandidate { Key = key, Label = label, Items = items.ToArray(), Metrics = metrics, ConstraintFitness = constraintFitness,
                ControlsEnergy = weights[0] + weights[6] + weights[7] + weights[8] + weights[9] > 0
                    || r.ResourcePrioritiesReviewed && r.EnergyPriorities.Length > 0,
                ControlsMagic = weights[1] + weights[2] + weights[6] + weights[8] > 0
                    || r.ResourcePrioritiesReviewed && r.MagicPriorities.Length > 0,
                BloodPercent = bestSplit, Ritual = ritual, Energy = energyShares.Sum(), MagicNgu = magicShares.Sum(), EnergyShares = energyShares, MagicShares = magicShares,
                Blood = ritual >= 0 ? funding?.Blood ?? ResourcePercent(magic, bestSplit) : 0, Zone = zone, Floor = floor, Hits = hits, Support = support, Feasible = feasible,
                EnergyPool = energy, MagicPool = magic, EnergyNguPower = ep, MagicNguPower = mp, NguBonus = nguBonus,
                EnergyDiggerBonus = energyDigger, MagicDiggerBonus = magicDigger, MagicPower = mp, BloodBonus = supportRatio(10), GrossGold = grossGold, RawMetrics = metrics.ToArray(),
                GearPower = power, GearToughness = toughness, GearDrop = drop };
            c.SetupChanged = changed;
            c.RegularEligibilityZone = s.Zones.Any(z => z.Id == zone) ? zone : s.Zones.Select(z => z.Id).DefaultIfEmpty(-1).First();
            c.BoostEligibilityZone = s.Zones.Any(z => z.Id == zone && z.BoostValues.Length > 0) ? zone
                : s.Zones.Where(z => z.BoostValues.Length > 0).Select(z => z.Id).DefaultIfEmpty(-1).First();
            c.PriorityFunding = funding;
            var uptime = Math.Max(0, r.Horizon - (changed ? r.Swap : 0)) / (double)r.Horizon;
            // Instant equipment stats do not accumulate or lose value to production downtime.
            for (var g = 0; g < ProductionGoalCount; g++) if (c.Metrics[g].HasValue) c.Metrics[g] *= uptime;
            c.Score = CandidateScore(s, r, c, weights, normalization);
            if (!feasible) c.Warnings = new[] { "Power/toughness, zone safety, legality, or sustainable gold constraint failed." };
            if (energyCap < s.ReservedEnergy) c.Warnings = c.Warnings.Concat(new[] { "Candidate energy capacity is below retained basic training (" + energyCap + " < " + s.ReservedEnergy + "); basic training cannot be recalled by preset apply." }).ToArray();
            if (s.BeastMode) c.Warnings = c.Warnings.Concat(new[] { "Farming unavailable while Beast Mode is on: triple incoming damage is not covered by idle safety recommendations. Turn it off and preview again." }).ToArray();
            if (s.LazyItopodActive && policy != null && policy.Zone == 1000 && floor != policy.Floor)
                c.Warnings = c.Warnings.Concat(new[] { "Lazy ITOPOD shifter is purchased and enabled and would replace the exact cached floor after a kill. Preview again under the current shifter preference; no preference was changed." }).ToArray();
            if (s.LazyItopodActive && zone < 0 && weights[3] > 0)
                c.Warnings = c.Warnings.Concat(new[] { "No supported stationary ITOPOD route at the native Lazy ITOPOD shifter floor. The shifter preference was left unchanged." }).ToArray();
            if (s.Available[0] && !metrics[0].HasValue || s.Available[1] && !metrics[1].HasValue)
                c.Warnings = c.Warnings.Concat(new[] { "NGU tick integration could not certify its error bound within the fixed work budget; affected production is unavailable." }).ToArray();
            if (selectedZone && zone != r.Zone) c.Warnings = c.Warnings.Concat(new[] { "Selected regular zone is locked, unavailable or below safety recommendations; no alternative route substituted." }).ToArray();
            return c;
        }

        internal static double BloodFills(double progressPerTick)
        {
            return !Finite(progressPerTick) || progressPerTick <= 1e-9 ? 0 : 50 / Math.Ceiling(1 / Math.Min(1, progressPerTick));
        }

        internal static PresetCandidate ReserveIronPillMagic(PresetSnapshot snapshot, PresetCandidate candidate)
        { return ComposeResourceDemands(snapshot, candidate, true, false); }

        internal static PresetCandidate ComposeResourceDemands(PresetSnapshot snapshot, PresetCandidate baseline, bool ironPill, bool bloodGoals)
        {
            // Both demands use one reservation; Iron Pill takes precedence without stacking blood funding.
            // The caller supplies a fresh baseline on every pass, including demand release.
            if (!ironPill && !bloodGoals || !baseline.Feasible || snapshot.Available.Length <= 2 || !snapshot.Available[2]) return baseline;
            // Keep dedicated TM/Wandoos allocations and every energy lane. Only the shared magic pool is borrowed.
            var pool = Math.Max(0, baseline.MagicPool - (baseline.Activities?.MagicTotal ?? 0));
            var gold = baseline.GrossGold - baseline.Support.Drain;
            if (pool <= 0 || !(gold > 0)) return baseline;
            var selected = -1; var amount = 0L; var production = 0d;
            foreach (var ritual in snapshot.Rituals)
            {
                var coefficient = ritual.Coefficient * baseline.MagicPower;
                if (!ritual.Unlocked || !ritual.Paid && Math.Max(0d, snapshot.Gold - baseline.ActivityGoldSpent) <= ritual.Cost
                    || !Finite(coefficient) || coefficient <= 0 || !Finite(ritual.Cost) || ritual.Cost < 0
                    || !Finite(ritual.Blood) || ritual.Blood <= 0) continue;
                var limit = Math.Min(pool, IntegerBudget(Math.Ceiling(1.000002d / coefficient)));
                // Discrete native fills are monotone. Find a sustainable partial allocation, not just all-or-nothing funding.
                var low = 0L; var high = limit;
                while (low < high)
                {
                    var mid = low + (high - low) / 2 + 1;
                    if (BloodFills(coefficient * mid) * ritual.Cost < gold) low = mid;
                    else high = mid - 1;
                }
                if (low < limit && low > 0)
                {
                    var fills = BloodFills(coefficient * low);
                    var sustainable = low; var first = 1L;
                    while (first < low)
                    {
                        var mid = first + (low - first) / 2;
                        if (BloodFills(coefficient * mid) >= fills) low = mid;
                        else first = mid + 1;
                    }
                    low = Math.Min(sustainable, IntegerBudget(Math.Ceiling(first * 1.000002d)));
                }
                var blood = BloodFills(coefficient * low) * ritual.Blood * baseline.BloodBonus;
                if (!Finite(blood) || blood <= 0 || blood < production
                    || blood == production && (low > amount || low == amount && ritual.Id < selected)) continue;
                selected = ritual.Id; amount = low; production = blood;
            }
            if (selected < 0) return baseline;
            var overlay = baseline.Copy();
            overlay.ControlsMagic = true;
            overlay.Ritual = selected; overlay.Blood = amount;
            overlay.MagicShares = NguShares(snapshot.MagicNgu, snapshot.Available[1] ? pool - amount : 0,
                baseline.MagicNguPower, AllocationLookaheadSeconds, baseline.NguBonus, baseline.MagicDiggerBonus);
            overlay.MagicNgu = overlay.MagicShares.Sum();
            return overlay;
        }

        private struct ProjectedResources
        {
            internal float[] Bonuses;
            internal double EnergyPower, MagicPower, EnergyCap, MagicCap;
            internal long EnergyCapBudget, Energy, Magic;
        }

        private static ProjectedResources ProjectResources(PresetSnapshot s, PresetItem[] items)
        {
            var bonuses = ResourceBonuses(s, items);
            var result = new ProjectedResources { Bonuses = bonuses,
                EnergyPower = s.EnergyPowerProjection.Project(bonuses), MagicPower = s.MagicPowerProjection.Project(bonuses),
                EnergyCap = s.EnergyCapacity.Project(bonuses), MagicCap = s.MagicCapacity.Project(bonuses) };
            result.EnergyCapBudget = IntegerBudget(result.EnergyCap);
            // Subtract retained basic training as integers before clipping the actual reclaimable pools.
            result.Energy = CurrentPoolBudget(s, items, true, Math.Max(0, result.EnergyCapBudget - s.ReservedEnergy));
            result.Magic = CurrentPoolBudget(s, items, false, IntegerBudget(result.MagicCap));
            return result;
        }

        private static long CurrentPoolBudget(PresetSnapshot snapshot, PresetItem[] items, bool energy, long projectedBudget)
        {
            var observed = energy ? snapshot.Energy : snapshot.Magic;
            // Over-cap native pools remain real resources when replaying unchanged
            // equipment. This never increases a prospective swap's capacity allowance.
            var unchanged = snapshot.ObservedCurrentPools && items.Length == snapshot.Current.Length
                && !items.Where((item, i) => !ReferenceEquals(item, snapshot.Current[i])
                    && !(item.Id == 0 && snapshot.Current[i].Id == 0 && item.SourceSlot == snapshot.Current[i].SourceSlot)).Any();
            return unchanged ? observed : Math.Min(observed, projectedBudget);
        }

        private static PresetCandidate EvaluateActivities(PresetSnapshot s, PresetRequest r, PresetItem[] items, double[] w,
            double[] norms, string key, string label, PresetCandidate pinned, PresetSupport support,
            PresetActivityAllocation priorityActivities = null, PresetLaneFunding funding = null)
        {
            if (r.ATWeights == null || r.ATWeights.Length != 5 || r.ATWeights.Any(x => x < 0))
                throw new ArgumentException("ATWeights must contain five nonnegative integers.");
            var totals = Totals(s, items); var original = Totals(s, s.Current);
            var resources = ProjectResources(s, items);
            var ep = resources.EnergyPower; var mp = resources.MagicPower;
            var energy = resources.Energy; var magic = resources.Magic;
            Func<PresetStat, double> gearRatio = stat => Math.Max(0, 100 + totals[(int)stat]) / Math.Max(1e-100, 100 + original[(int)stat]);
            var atSpeed = gearRatio(PresetStat.AdvancedTrainingSpeed);
            var augSpeed = gearRatio(PresetStat.AugmentSpeed);
            var wSpeed = gearRatio(PresetStat.WandoosSpeed)
                * SupportProjection.BeardBonus(s, support.Beards, 4) / SupportProjection.BeardBonus(s, s.CurrentBeards, 4)
                * DiggerBonus(s, support.Diggers, 1) / DiggerBonus(s, s.CurrentDiggers, 1);
            var eWeights = new[] { w[0], w[6], w[7], w[8], w[9] };
            var mWeights = new[] { w[1] + w[2], w[6], w[8] };
            var eCaps = new[] { s.Available[0], s.Available[6] && s.TimeMachineEnergy.Unlocked, s.Available[7],
                s.Available[8] && s.WandoosEnergy.Unlocked, s.Available[9] && s.AdvancedTraining.Where((x, i) => r.ATWeights[i] > 0).Any(x => x.Unlocked) };
            var mCaps = new[] { s.Available[1] || s.Available[2], s.Available[6] && s.TimeMachineMagic.Unlocked, s.Available[8] && s.WandoosMagic.Unlocked };
            Func<double[], bool[], long, long[]> distribute = (weights, available, pool) => {
                var max = weights.Max();
                return Share(available.Select((a, i) => a && weights[i] > 0 ? (double)long.MaxValue : 0).ToArray(), pool,
                    weights.Select(x => max > 0 && x > 0 ? Math.Max(1, Math.Round(x / max * 10000)) : 0).ToArray());
            };
            PresetCandidate best = null;
            Func<long[], long[], PresetActivityAllocation, PresetCandidate> trial = (e, m, fixedAllocation) => {
                var allocation = fixedAllocation ?? new PresetActivityAllocation {
                    Os = s.WandoosOs, TimeMachineEnergy = e[1], TimeMachineMagic = m[1], WandoosEnergy = e[3], WandoosMagic = m[2] };
                if (fixedAllocation == null && s.AdvancedTraining.Length == 5)
                    allocation.AT = Share(s.AdvancedTraining.Select(x => x.Unlocked ? (double)long.MaxValue : 0).ToArray(), e[4], r.ATWeights.Select(x => (double)x).ToArray());
                var tiers = fixedAllocation != null ? s.Augments.Where(x => x.Id == fixedAllocation.AugmentId).ToArray()
                    : e[2] > 0 ? s.Augments.Where(x => x.Augment.Unlocked || x.Upgrade.Unlocked).ToArray() : new PresetAugment[0];
                if (fixedAllocation == null ? e[2] == 0 : fixedAllocation.AugmentId < 0) tiers = new PresetAugment[] { null };
                PresetCandidate winner = null;
                foreach (var tier in tiers)
                {
                    // Exact AUG pair indices and amounts are stored, never reselected during replay.
                    var a = new PresetActivityAllocation { Os = allocation.Os, TimeMachineEnergy = allocation.TimeMachineEnergy,
                        TimeMachineMagic = allocation.TimeMachineMagic, WandoosEnergy = allocation.WandoosEnergy,
                        WandoosMagic = allocation.WandoosMagic, AT = allocation.AT.ToArray(), AugmentId = tier?.Id ?? -1 };
                    for (var i = 0; i < s.RequiredATTargets.Length && i < a.AT.Length; i++)
                        if (s.RequiredATTargets[i] <= 0 || i < s.AdvancedTraining.Length && s.AdvancedTraining[i].Level >= s.RequiredATTargets[i]) a.AT[i] = 0;
                    var pair = fixedAllocation != null ? new[] { fixedAllocation.AugmentEnergy, fixedAllocation.UpgradeEnergy }
                        : tier == null ? new long[2] : Share(new[] { tier.Augment.Unlocked ? (double)long.MaxValue : 0,
                            tier.Upgrade.Unlocked ? (double)long.MaxValue : 0 }, e[2], new[] { 1d, 1 });
                    a.AugmentEnergy = pair[0]; a.UpgradeEnergy = pair[1];
                    var liquid = s.Gold;
                    if (r.ObjectiveSetup && fixedAllocation == null)
                    {
                        // Provisional amounts share real liquid gold, independent of reviewed lane order.
                        // An uncertified forecast stays uncertified, not a fabricated known-zero objective.
                        a.TimeMachineEnergy = AffordableActivityBar(s.TimeMachineEnergy, a.TimeMachineEnergy, ep, r.Horizon, 1, ref liquid) ?? a.TimeMachineEnergy;
                        a.TimeMachineMagic = AffordableActivityBar(s.TimeMachineMagic, a.TimeMachineMagic, mp, r.Horizon, 1, ref liquid) ?? a.TimeMachineMagic;
                        if (tier != null)
                        {
                            a.AugmentEnergy = AffordableActivityBar(tier.Augment, a.AugmentEnergy, ep, r.Horizon, augSpeed, ref liquid) ?? a.AugmentEnergy;
                            a.UpgradeEnergy = AffordableActivityBar(tier.Upgrade, a.UpgradeEnergy, ep, r.Horizon, augSpeed, ref liquid) ?? a.UpgradeEnergy;
                        }
                    }
                    var reduced = s.ActivityCopy(); reduced.Energy = Math.Max(0, energy - a.EnergyTotal); reduced.Magic = Math.Max(0, magic - a.MagicTotal);
                    if (r.ObjectiveSetup && fixedAllocation == null) reduced.Gold = liquid;
                    var c = EvaluateCore(reduced, r, items, w, norms, key, label, pinned, support, funding);
                    c.Activities = a; c.EnergyPool = energy; c.MagicPool = magic;
                    var duration = Math.Max(0, r.Horizon - (c.SetupChanged ? r.Swap : 0));
                    var valid = a.EnergyTotal <= energy && a.MagicTotal <= magic && a.AT.Length == 5
                        && (a.WandoosEnergy + a.WandoosMagic == 0 || a.Os == s.WandoosOs);
                    valid &= (a.TimeMachineEnergy + a.TimeMachineMagic == 0 || s.Available[6])
                        && (a.AugmentEnergy + a.UpgradeEnergy == 0 || s.Available[7])
                        && (a.WandoosEnergy + a.WandoosMagic == 0 || s.Available[8])
                        && (a.AT.Sum() == 0 || s.Available[9]);
                    var missingRequiredAT = MissingRequiredATFunding(s, a.AT);
                    valid &= !missingRequiredAT;
                    var spent = 0d;
                    for (var g = 6; g < ProductionGoalCount; g++)
                    {
                        double? gain = null;
                        if (funding != null && !s.Available[g]) gain = 0;
                        if (s.Available[g])
                        {
                            if (g == 6)
                            {
                                var eg = ActivityGain(s.TimeMachineEnergy, a.TimeMachineEnergy, ep, duration, 1);
                                var mg = ActivityGain(s.TimeMachineMagic, a.TimeMachineMagic, mp, duration, 1);
                                gain = eg + mg;
                                if (gain.HasValue) spent += ActivityCost(s.TimeMachineEnergy, eg.Value, a.TimeMachineEnergy)
                                    + ActivityCost(s.TimeMachineMagic, mg.Value, a.TimeMachineMagic);
                            }
                            if (g == 8) gain = ActivityGain(s.WandoosEnergy, a.WandoosEnergy, 1, duration, wSpeed)
                                + ActivityGain(s.WandoosMagic, a.WandoosMagic, 1, duration, wSpeed);
                            if (g == 9 && s.AdvancedTraining.Length == 5)
                            {
                                var sum = r.ATWeights.Sum(x => (double)x); gain = 0;
                                for (var i = 0; i < 5; i++) if (r.ATWeights[i] > 0)
                                    gain += ActivityGain(s.AdvancedTraining[i], a.AT[i], ep, duration, atSpeed) * (r.ATWeights[i] / sum);
                                var stageComplete = s.RequiredATTargets.Length == 2
                                    && s.RequiredATTargets.Select((target, i) => target <= 0 || s.AdvancedTraining[i].Level >= target).All(x => x);
                                if (!stageComplete && (sum <= 0 || !s.AdvancedTraining.Where((x, i) => r.ATWeights[i] > 0).Any(x => x.Unlocked)) && w[g] > 0) gain = null;
                            }
                            if (g == 7)
                            {
                                gain = 0;
                                if (tier != null)
                                {
                                    var ag = ActivityGain(tier.Augment, a.AugmentEnergy, ep, duration, augSpeed);
                                    var ug = ActivityGain(tier.Upgrade, a.UpgradeEnergy, ep, duration, augSpeed);
                                    if (!ag.HasValue || !ug.HasValue) gain = null;
                                    else
                                    {
                                        spent += ActivityCost(tier.Augment, ag.Value, a.AugmentEnergy) + ActivityCost(tier.Upgrade, ug.Value, a.UpgradeEnergy);
                                        Func<double, double, double> bonus = (al, ul) => tier.BaseBoost * (ul * ul + 1) * Math.Pow(al, tier.TierExponent);
                                        var baseline = 1 + s.Augments.Sum(x => x.BaseBoost * (x.Upgrade.Level * x.Upgrade.Level + 1) * Math.Pow(x.Augment.Level, x.TierExponent));
                                        var delta = bonus(tier.Augment.Level + ag.Value, tier.Upgrade.Level + ug.Value) - bonus(tier.Augment.Level, tier.Upgrade.Level);
                                        gain = Math.Max(1, s.AugmentGlobalFactor * (baseline + delta)) - Math.Max(1, s.AugmentGlobalFactor * baseline);
                                    }
                                }
                                else if (a.AugmentEnergy + a.UpgradeEnergy > 0) valid = false;
                            }
                        }
                        if (gain.HasValue && (!Finite(gain.Value) || gain.Value < 0)) gain = null;
                        c.RawMetrics[g] = duration > 0 ? gain / duration : gain.HasValue ? 0 : (double?)null;
                        c.Metrics[g] = gain / r.Horizon;
                        if (w[g] > 0 && !gain.HasValue) valid = false;
                    }
                    var ritualReserve = s.Rituals.FirstOrDefault(x => x.Id == c.Ritual);
                    valid &= Finite(spent) && spent <= Math.Max(0, s.Gold - (ritualReserve != null && !ritualReserve.Paid ? ritualReserve.Cost : 0));
                    if (funding != null && c.Blood > 0 && ritualReserve != null && !ritualReserve.Paid)
                        valid &= spent < s.Gold - ritualReserve.Cost;
                    c.ActivityGoldSpent = spent;
                    c.Feasible &= valid;
                    c.Score = CandidateScore(s, r, c, w, norms);
                    c.Warnings = c.Warnings.Concat(new[] { "Extra lanes use fixed allocations and captured factors; partial bars are discarded conservatively. TM/AUG reserve all forecast bar starts from current liquid gold; future income is not borrowed. Wandoos keeps the installed OS and frozen captured AT/bootup speed. No extra-activity two-phase forecast is offered." })
                        .Concat(missingRequiredAT ? new[] { "Mandatory stage AT targets require positive allocations on every unfinished stat lane." } : new string[0])
                        .Concat(Enumerable.Range(6, 4).Where(g => w[g] > 0 && !c.Metrics[g].HasValue).Select(g => Keys[g] + ": " + (s.Reasons[g] ?? "native parity or bounded forecast unavailable")))
                        .Concat(!valid ? new[] { "Extra activity allocation, native availability, projection bound, OS, or liquid gold constraint failed." } : new string[0]).ToArray();
                    if (winner == null || Rank(s, r, c, w, norms) > Rank(s, r, winner, w, norms)) winner = c;
                }
                return winner;
            };
            if (pinned != null || priorityActivities != null)
            {
                // Non-activity specialists deliberately fund no extra lanes.
                best = trial(null, null, priorityActivities ?? pinned.Activities ?? new PresetActivityAllocation { Os = s.WandoosOs });
                if (best == null) throw new InvalidOperationException("Cached AUG tier is unavailable; preview again.");
                return best;
            }
            var eStart = distribute(eWeights, eCaps, energy); var mStart = distribute(mWeights, mCaps, magic);
            best = trial(eStart, mStart, null);
            if (best == null) best = trial(new long[5], mStart, null);
            var bestE = eStart; var bestM = mStart;
            // Bounded coordinate transfers evaluate REAL normalized production, not largest-weight dispatch.
            // Include full-lane endpoints, then two shrinking transfers around the best mixed policy.
            for (var resource = 0; resource < 2; resource++)
            {
                var laneWeights = resource == 0 ? eWeights : mWeights;
                var available = resource == 0 ? eCaps : mCaps;
                var pool = resource == 0 ? energy : magic;
                foreach (var step in new[] { 100, 25, 10 })
                {
                    var seed = resource == 0 ? bestE : bestM;
                    for (var to = 0; to < laneWeights.Length; to++)
                    {
                        if (!available[to] || laneWeights[to] <= 0) continue;
                        var next = seed.ToArray();
                        var moved = 0L;
                        for (var from = 0; from < next.Length; from++) if (from != to)
                        { var amount = ResourcePercent(next[from], step); next[from] -= amount; moved += amount; }
                        next[to] += moved;
                        if (seed.Sum() == 0) next[to] = pool;
                        var c = trial(resource == 0 ? next : bestE, resource == 0 ? bestM : next, null);
                        if (c != null && (best == null || Rank(s, r, c, w, norms) > Rank(s, r, best, w, norms) + 1e-12))
                        { best = c; if (resource == 0) bestE = next; else bestM = next; }
                    }
                }
            }
            return best;
        }

        internal const double ActivityRateSafetyFactor = .99999;

        internal static double? ActivityGain(PresetActivityBar bar, long allocation, double power, double seconds, double speed)
        {
            if (allocation == 0 || seconds <= 0) return 0;
            if (bar == null || !bar.Unlocked || !Finite(bar.Coefficient) || bar.Coefficient <= 0 || !Finite(bar.Level)
                || bar.Level < 0 || bar.Level != Math.Floor(bar.Level) || !Finite(speed) || speed <= 0) return null;
            var target = bar.Target < 0 ? bar.Level : bar.Target > 0 ? bar.Target : 9007199254740991d;
            var numerator = allocation * Math.Pow(power, bar.PowerExponent) * bar.Coefficient * speed * ActivityRateSafetyFactor;
            if (!Finite(numerator)) return null;
            if (numerator / Math.Pow(bar.Level + 1, bar.LevelExponent) <= 1e-9) return 0;
            if (bar.LevelExponent == 1)
            {
                var value = NguGain(new PresetNgu { Level = bar.Level, Target = target, HardCap = target }, numerator, seconds);
                return Finite(value) ? (double?)value : null;
            }
            if (bar.LevelExponent != 0) return null;
            var ticks = NguTicks(numerator, 1);
            return Math.Min(Math.Max(0, target - bar.Level), Math.Floor(seconds * 50 / ticks));
        }

        private static double ActivityCost(PresetActivityBar bar, double gain, long allocation)
        {
            if (allocation <= 0) return 0;
            // Charge an additional unfinished bar, even if the last completion lands on the deadline.
            var count = Math.Ceiling(gain / (1 - NguRelativeError - .001)) + 2; var first = bar.Level + 1;
            var cost = bar.CostExponent == 1 ? count * (2 * first + count - 1) / 2
                : bar.CostExponent == 2 ? count * first * first + first * count * (count - 1) + count * (count - 1) * (2 * count - 1) / 6
                : double.PositiveInfinity;
            return Math.Max(0, (cost - (bar.Paid ? Math.Pow(first, bar.CostExponent) : 0)) * bar.CostCoefficient);
        }

        internal static double NguRate(PresetNgu[] targets, long budget, double power, double horizon, double speedBonus = double.NaN, double diggerBonus = double.NaN)
        {
            if (horizon <= 0) return 0;
            var shares = NguShares(targets, budget, power, AllocationLookaheadSeconds, speedBonus, diggerBonus);
            var gains = 0d;
            for (var i = 0; i < targets.Length; i++)
            {
                var x = targets[i]; if (shares[i] <= 0) continue;
                gains += NguGain(x, NguNumerator(x, power, shares[i]), horizon, speedBonus, diggerBonus);
            }
            return gains / horizon;
        }

        private static double? NguMetric(PresetNgu[] targets, long budget, double power, double seconds, double speedBonus, double diggerBonus)
        { var rate = NguRate(targets, budget, power, seconds, speedBonus, diggerBonus); return Finite(rate) ? (double?)rate : null; }

        internal static double NguNumerator(PresetNgu row, double power, long share)
        { return row.SpeedDivider > 0 ? (float)((float)((float)power / row.SpeedDivider) * (float)share) : row.Coefficient * power * share; }

        private static double NguCoefficient(PresetNgu row, double speedBonus, double diggerBonus)
        {
            return row.Coefficient * (double.IsNaN(speedBonus) ? 1 : speedBonus / (row.TickFactors.Length > 0 ? row.TickFactors[0] : 1))
                * (double.IsNaN(diggerBonus) ? 1 : diggerBonus / (row.TickFactors.Length > 4 ? row.TickFactors[4] : 1));
        }

        private static double NguTarget(PresetNgu x) { return x.Target < 0 ? 0 : x.Target > 0 ? Math.Min(x.Target, x.HardCap) : x.HardCap; }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] internal float Value;
            [FieldOffset(0)] internal int Bits;
        }

        // Repeated float addition has a constant ULP increment inside a binade, after tie parity settles.
        // Skip those runs instead of simulating up to thirty million ticks for a single slow level.
        internal static long NguTicks(double a, double divider)
        { return NguTicks(null, a, divider, double.NaN, double.NaN); }

        private static long NguTicks(PresetNgu row, double a, double divider, double speedBonus, double diggerBonus)
        {
            var progress = a / divider;
            if (row != null)
            {
                for (var i = 0; i < row.TickFactors.Length; i++)
                    progress *= i == 0 && !double.IsNaN(speedBonus) ? speedBonus : i == 4 && !double.IsNaN(diggerBonus) ? diggerBonus : row.TickFactors[i];
                if (row.TickFactors.Length == 0) progress *= (double.IsNaN(speedBonus) ? 1 : speedBonus) * (double.IsNaN(diggerBonus) ? 1 : diggerBonus);
                progress /= row.TickDivider;
            }
            var increment = (float)progress;
            if (!(increment > 0)) return long.MaxValue;
            if (increment >= 1) return 1;
            var current = new FloatBits(); var ticks = 0L;
            for (var step = 0; step < 512; step++)
            {
                var next = new FloatBits { Value = (float)(current.Value + increment) };
                if (next.Value >= 1) return ticks + 1;
                if (next.Bits == current.Bits) return long.MaxValue;
                var following = new FloatBits { Value = (float)(next.Value + increment) };
                var exponentEnd = Math.Min(0x3f800000, (current.Bits & 0x7f800000) + 0x00800000);
                var delta = next.Bits - current.Bits;
                if (next.Bits < exponentEnd && following.Bits < exponentEnd && following.Bits - next.Bits == delta)
                {
                    var count = (exponentEnd - 1 - current.Bits) / delta;
                    if (count > 0) { current.Bits += count * delta; ticks += count; continue; }
                }
                current = next; ticks++;
            }
            return long.MaxValue;
        }

        private struct NguBand
        {
            internal long First, Count, Low, High;
        }

        internal const double NguRelativeError = .005;
        internal const int NguBandBudget = 256;

        internal static double NguGain(PresetNgu x, double a, double seconds, double speedBonus = double.NaN, double diggerBonus = double.NaN)
        {
            if (!(a > 0) || !(seconds > 0) || x.Level >= NguTarget(x)) return 0;
            if (!Finite(a) || !Finite(seconds) || seconds > 604800 || !Finite(x.Level) || x.Level < 0 || x.Level != Math.Floor(x.Level)) return double.NaN;
            var ticks = (long)Math.Floor(seconds * 50);
            var firstCost = NguTicks(x, a, x.Level + 1, speedBonus, diggerBonus);
            var maximum = (long)Math.Min(NguTarget(x) - x.Level, ticks / firstCost);
            if (maximum <= 0) return 0;
            var lastCost = NguTicks(x, a, x.Level + maximum, speedBonus, diggerBonus);
            if (lastCost == firstCost) return maximum;
            var bands = new NguBand[(int)Math.Min(NguBandBudget, maximum)]; var count = 1;
            bands[0] = new NguBand { First = 1, Count = maximum, Low = firstCost, High = lastCost };
            for (;;)
            {
                var lowerTicks = ticks; var upperTicks = ticks; var lower = 0L; var upper = 0L;
                var split = -1; var uncertainty = -1d;
                for (var i = 0; i < count; i++)
                {
                    var band = bands[i];
                    var certain = Math.Min(band.Count, lowerTicks / band.High);
                    var possible = Math.Min(band.Count, upperTicks / band.Low);
                    lower += certain; upper += possible;
                    lowerTicks = certain == band.Count ? lowerTicks - certain * band.High : 0;
                    upperTicks = possible == band.Count ? upperTicks - possible * band.Low : 0;
                    var error = possible > 0 && band.Count > 1 ? (double)band.Count * (band.High - band.Low) : -1;
                    if (error > uncertainty) { uncertainty = error; split = i; }
                }
                // These are bounds on completed game levels, not a midpoint production estimate.
                // Small gains are exact; larger gains are conservative within 0.5 percent.
                if (upper - lower <= Math.Floor(lower * NguRelativeError)) return lower;
                if (count == bands.Length || split < 0) return double.NaN;
                var old = bands[split]; var leftCount = old.Count / 2;
                var left = new NguBand { First = old.First, Count = leftCount, Low = old.Low,
                    High = NguTicks(x, a, x.Level + old.First + leftCount - 1, speedBonus, diggerBonus) };
                var right = new NguBand { First = old.First + leftCount, Count = old.Count - leftCount, High = old.High,
                    Low = NguTicks(x, a, x.Level + old.First + leftCount, speedBonus, diggerBonus) };
                Array.Copy(bands, split + 1, bands, split + 2, count - split - 1);
                bands[split] = left; bands[split + 1] = right; count++;
            }
        }

        internal static long[] NguShares(PresetNgu[] targets, long budget, double power, int horizon, double speedBonus = double.NaN, double diggerBonus = double.NaN)
        {
            var caps = targets.Select(x => x.Weight <= 0 || x.Level >= NguTarget(x) || x.Coefficient <= 0 || !(power > 0)
                ? 0 : Math.Ceiling(Math.Min(NguTarget(x), x.Level + 50d * horizon + 2)
                    / Math.Max(1e-100, NguCoefficient(x, speedBonus, diggerBonus) * power) * 1.000002d)).ToArray();
            return Share(caps, budget, targets.Select(x => (double)x.Weight).ToArray());
        }

        internal static long[] Share(double[] caps, long budget, double[] weights)
        {
            return ResourceAllocation.ShareCapped(caps.Select(IntegerBudget).ToArray(), budget, weights.Select(x => checked((int)x)).ToArray());
        }

        private static long IntegerBudget(double value)
        { return value >= long.MaxValue ? long.MaxValue : !(value > 0) ? 0 : (long)Math.Floor(value); }

        internal static long ResourcePercent(long pool, int percent) { return (long)((decimal)pool * percent / 100); }

        internal static int[] SelectRotationBeards(SupportContext s, int[] required, int[] pool, int[] current = null, Random random = null)
        {
            if (!s.BeardsAvailable || s.BeardsDisabled) return new int[0];
            var mandatory = required.Where(s.UnlockedBeards.Contains).Distinct().Take(Math.Max(0, s.BeardSlots)).ToArray();
            var optional = pool.Where(s.UnlockedBeards.Contains).Except(required).Distinct().ToArray();
            var slots = Math.Min(Math.Max(0, s.BeardSlots - mandatory.Length), optional.Length);
            if (random == null) return mandatory.Concat(optional.Take(slots)).ToArray();
            var choices = new List<int[]>();
            for (var mask = 0; mask < 1 << optional.Length; mask++)
            {
                var pick = optional.Where((id, bit) => (mask & 1 << bit) != 0).ToArray();
                if (pick.Length == slots) choices.Add(mandatory.Concat(pick).ToArray());
            }
            var previous = new HashSet<int>(current ?? new int[0]);
            if (choices.Any(choice => !previous.SetEquals(choice))) choices.RemoveAll(choice => previous.SetEquals(choice));
            return choices[choices.Count == 1 ? 0 : random.Next(choices.Count)];
        }

        internal static PresetSupport RotationSupport(SupportContext s, PresetRequest r, PresetSupport acquired, int[] beards)
        {
            if (!r.PlannedSupport) throw new InvalidOperationException("Beard rotation requires planned support.");
            var selection = new SupportSelection(beards, acquired.Diggers.Select(d => new SupportDiggerLevel(d.Id, d.Level)).ToArray());
            var evaluation = SupportProjection.Evaluate(s, selection, r.DiggerPercent / 100d, r.PlannedSupport);
            if (!evaluation.Feasible)
            {
                // Re-fund only the acquired IDs, weights and level ceilings. This is not a support/gear search.
                evaluation = SupportProjection.Plan(s, beards, acquired.Diggers.Select(d => new DiggerBudgetRequest {
                    Id = d.Id, Maximum = d.Level,
                    Weight = acquired.DiggerWeights.Length == 12 ? acquired.DiggerWeights[d.Id] : 1d }).ToArray(), r.DiggerPercent / 100d);
            }
            if (!evaluation.Feasible) throw new InvalidOperationException(evaluation.Reason);
            return new PresetSupport { Beards = evaluation.Selection.Beards, Diggers = evaluation.Selection.Diggers.Select(level => {
                var d = s.Diggers.Single(x => x.Id == level.Id);
                return new SupportDigger { Id = d.Id, Name = d.Name, Level = level.Level, Maximum = d.Maximum, BaseDrain = d.BaseDrain,
                    Growth = d.Growth, ConsumesGold = d.ConsumesGold, StartBonus = d.StartBonus, PerLevel = d.PerLevel };
            }).ToArray(), DiggerWeights = acquired.DiggerWeights.ToArray(), Notes = acquired.Notes.ToArray() };
        }

        private static PresetSupport[] SupportAlternatives(PresetSnapshot s, PresetRequest r, double[] weights)
        {
            weights = Pad(weights, 0d);
            var notes = new[] { "Candidate gross GPS includes immediate Gold beard activation at fixed levels; blood consumes the remainder after diggers. Gear gold does not increase GPS." };
            if (r.PlannedSupport && r.BeardRotation != null) notes = notes.Concat(new[] {
                "Enabled beards lead in priority order; only the explicit optional pool fills remaining slots. Preview/Apply use pool order deterministically. Global ownership rerolls optional slots each minute; future rotations and beard growth are not forecast." }).ToArray();
            if (!r.PlannedSupport) return new[] { new PresetSupport {
                Beards = s.CurrentBeards,
                Diggers = s.CurrentDiggers, Notes = notes } };
            notes = notes.Concat(new[] {
                "Bounded support alternatives are ranked by actual normalized objectives, including gear/route/split interactions, not bonus multipliers. No future beard growth is forecast.",
                "Without BeardRotation, explicit beard priority fixes the filtered/capped selection; legacy auto keeps the ordered core mandatory and searches only optional slots.",
                "Explicit digger weights retain eligible IDs, priority and proportional funding; lower total funding alternatives may leave gold for blood." }).ToArray();
            var beardChoices = new List<int[]>();
            var priorities = r.BeardPriority.Length > 0 ? r.BeardPriority : ActivityPolicy.Current.DefaultBeardPriority();
            var initial = r.BeardRotation != null ? SelectRotationBeards(s, r.BeardPriority, r.BeardRotation)
                : !s.BeardsAvailable || s.BeardsDisabled ? new int[0] : priorities.Where(s.UnlockedBeards.Contains).Distinct().Take(Math.Max(0, s.BeardSlots)).ToArray();
            beardChoices.Add(initial);
            if (r.BeardRotation == null && r.BeardPriority.Length == 0 && s.BeardsAvailable && !s.BeardsDisabled)
            {
                var mandatory = ActivityPolicy.Current.CoreBeards().Where(s.UnlockedBeards.Contains).Take(Math.Max(0, s.BeardSlots)).ToArray();
                var optional = s.UnlockedBeards.Except(mandatory).OrderBy(id => id).ToArray();
                var slots = Math.Min(Math.Max(0, s.BeardSlots), s.UnlockedBeards.Length) - mandatory.Length;
                for (var mask = 0; mask < 1 << optional.Length; mask++)
                {
                    var selection = optional.Where((id, i) => (mask & 1 << i) != 0).ToArray();
                    if (selection.Length == slots && !new HashSet<int>(initial).SetEquals(mandatory.Concat(selection)))
                        beardChoices.Add(mandatory.Concat(selection).ToArray());
                }
            }
            var auto = PresetDefinitions.DefaultDiggerWeights(weights, r.MinimumPower > 0 || r.MinimumToughness > 0);
            var styles = new List<double[]> { r.DiggerWeights.Length == 12 ? r.DiggerWeights : auto };
            if (r.DiggerWeights.Length != 12)
            {
                var immediate = auto.ToArray(); immediate[6] = immediate[7] = 0; styles.Add(immediate);
                // Single-purpose and tilted portfolios can replace a weak multiplier, or free a scarce slot.
                foreach (var id in new[] { 4, 5, 3, 8, 0, 10, 1 }.Where(id => immediate[id] > 0 && s.Diggers.Any(d => d.Id == id && d.Maximum > 0)))
                {
                    var single = new double[12]; single[id] = 1; styles.Add(single);
                    var tilted = immediate.ToArray(); tilted[id] = immediate.Max() * 8; styles.Add(tilted);
                }
            }
            var result = new List<PresetSupport>(); var seen = new HashSet<string>();
            foreach (var style in styles)
            foreach (var fraction in new[] { 1d, .5, .25, 0 })
            foreach (var beards in beardChoices)
            {
                var evaluation = SupportProjection.Plan(s, beards, s.Diggers.Where(d => d.Maximum > 0 && style[d.Id] > 0)
                    .Select(d => new DiggerBudgetRequest { Id = d.Id, Maximum = d.Maximum, Weight = style[d.Id] }).ToArray(), r.DiggerPercent / 100d * fraction);
                if (!evaluation.Feasible || !seen.Add(evaluation.Selection.StableKey)) continue;
                var selected = evaluation.Selection.Diggers.Select(level => {
                    var d = s.Diggers.Single(x => x.Id == level.Id);
                    return new SupportDigger { Id = d.Id, Name = d.Name, Level = level.Level, Maximum = d.Maximum, BaseDrain = d.BaseDrain,
                        Growth = d.Growth, ConsumesGold = d.ConsumesGold, StartBonus = d.StartBonus, PerLevel = d.PerLevel };
                }).ToArray();
                result.Add(new PresetSupport { Beards = evaluation.Selection.Beards, Diggers = selected, DiggerWeights = style.ToArray(), Notes = notes });
                if (result.Count == 64) return result.ToArray();
            }
            return result.ToArray();
        }

        private static double DiggerBonus(PresetSnapshot s, SupportDigger[] diggers, int id)
        {
            return SupportProjection.DiggerBonus(s, new SupportSelection(s.CurrentBeards,
                diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()), id);
        }
        internal static int ItopodFloor(double power, double idleModifier, int maximum)
        {
            if (power < 700 || maximum <= 0) return 0;
            // Character.calculateBestItopodLevel stores both prefix operations as Single;
            // setOptimalFloor/verifyItopodInputs then clamp to the repeatable floor range.
            var ratio = (float)((float)power / 765f);
            ratio = (float)(ratio * (float)idleModifier);
            return Math.Max(0, Math.Min(maximum, (int)Math.Max(1, Math.Floor(Math.Log(Math.Max(1e-100, ratio), 1.05)))));
        }
        internal static double ScoringRatio(double production, double denominator)
        {
            // A late positive observation without a positive frozen reference earns
            // one unit of retained utility, not an epsilon-amplified unbounded score.
            return denominator > 0 ? production / denominator : production > 0 ? 1 : 0;
        }

        internal static double? Score(double?[] metrics, double[] weights, double[] normalization, bool feasible)
        {
            if (!feasible) return null;
            var sum = 0d;
            for (var g = 0; g < weights.Length; g++)
            {
                if (weights[g] <= 0) continue;
                if (g >= metrics.Length || !metrics[g].HasValue || !Finite(metrics[g].Value)) return null;
                var denominator = g < normalization.Length ? normalization[g] : 1;
                // No measured positive reference: zero earns nothing, newly discovered
                // positive production earns at most its own weight. Never divide by epsilon
                // or mutate frozen references when mixed search/replay finds new production.
                sum += weights[g] * ScoringRatio(metrics[g].Value, denominator);
            }
            return Finite(sum) ? (double?)sum : null;
        }
        private static double[] GearBlend(PresetRequest r)
        {
            if (r.GearWeights == null || r.GearWeights.Length != 5 || r.GearWeights.Any(x => !Finite(x) || x < 0))
                throw new ArgumentException("GearWeights must contain five finite nonnegative weights.");
            if (!r.GearWeights.Any(x => x > 0)) return null;
            if (r.GearFightMode < 0 || r.GearFightMode > 2) throw new ArgumentException("GearFightMode must be 0, 1, or 2.");
            var max = Math.Max(r.Weights.Max(), r.GearWeights.Max());
            var basis = r.Weights.Sum(x => x / max);
            var total = basis + r.GearWeights.Sum(x => x / max);
            return new[] { basis / total }.Concat(r.GearWeights.Select(x => x / max / total)).ToArray();
        }

        private static double? CandidateScore(PresetSnapshot s, PresetRequest r, PresetCandidate c, double[] weights,
            double[] norms, double?[] metrics = null)
        {
            var basis = Score(metrics ?? c.Metrics, weights, norms, c.Feasible);
            var blend = GearBlend(r);
            if (blend == null || !basis.HasValue) return basis;
            // Bonus denominators always describe captured current gear/current support, never a search incumbent.
            var original = Totals(s, s.Current); var totals = Totals(s, c.Items);
            var currentPower = AdventureStat(s.BasePower, s.CubePower, original[0], s.PowerFactors, s.PowerFactors[4]);
            var currentToughness = AdventureStat(s.BaseToughness, s.CubeToughness, original[1], s.ToughnessFactors, s.ToughnessFactors[4]);
            var power = c.GearPower / Math.Max(1e-100, currentPower);
            var toughness = c.GearToughness / Math.Max(1e-100, currentToughness);
            Func<PresetStat, double> gearRatio = stat => Math.Max(0, 100 + totals[(int)stat]) / Math.Max(1e-100, 100 + original[(int)stat]);
            var push = r.GearFightMode == 1 ? power * toughness : r.GearFightMode == 2
                ? (c.GearPower + c.GearToughness) / Math.Max(1e-100, currentPower + currentToughness) : Math.Sqrt(power * toughness);
            var utilities = new[] { push * gearRatio(PresetStat.MoveCooldown), power, toughness,
                c.GearDrop / Math.Max(1e-100, s.Drop), gearRatio(PresetStat.Gold) };
            var score = blend[0] * basis.Value;
            for (var i = 0; i < utilities.Length; i++) if (blend[i + 1] > 0) score += blend[i + 1] * utilities[i];
            return Finite(score) ? (double?)score : null;
        }

        private static double Rank(PresetSnapshot s, PresetRequest r, PresetCandidate c, double[] weights, double[] norms)
        {
            if (!c.Feasible) return -1e6 + c.ConstraintFitness;
            // Known-goal fallback only guides search; the public score remains null for unknown positive goals.
            var known = c.Metrics.Select((x, i) => x ?? 0).Select(x => (double?)x).ToArray();
            return CandidateScore(s, r, c, weights, norms, known) ?? double.NegativeInfinity;
        }
        internal static PresetSequence Sequence(PresetSnapshot s, PresetRequest r, PresetCandidate[] candidates, double[] weights, double[] norms,
            Stopwatch clock = null, PresetPlan plan = null)
        {
            clock = clock ?? Stopwatch.StartNew();
            // Extra activities have stateful gold/bars not modeled by the legacy two-phase integrator.
            // Instant stats/capacity and manual gear bonuses describe one setup, not time-shared production.
            if (r.ResourcePrioritiesReviewed || GearBlend(r) != null || weights.Length > 6 && weights.Skip(6).Any(x => x > 0)) return null;
            candidates = candidates.Where(c => c.Activities == null || c.Activities.EnergyTotal == 0 && c.Activities.MagicTotal == 0).ToArray();
            PresetSequence best = null;
            var baseline = candidates.Where(c => c.Score.HasValue).Select(c => c.Score.Value).DefaultIfEmpty(0).Max();
            foreach (var a in candidates.Where(c => c.Feasible))
            foreach (var b in candidates.Where(c => c.Feasible && c.Key != a.Key))
            {
                var sameSupport = a.Support.Beards.OrderBy(x => x).SequenceEqual(b.Support.Beards.OrderBy(x => x))
                    && a.Support.Diggers.OrderBy(x => x.Id).Select(x => x.Id + ":" + x.Level).SequenceEqual(b.Support.Diggers.OrderBy(x => x.Id).Select(x => x.Id + ":" + x.Level));
                var sameSetup = a.Items.SequenceEqual(b.Items) && sameSupport;
                var initial = a.SetupChanged ? r.Swap : 0;
                var internalSwap = sameSetup ? 0 : r.Swap;
                var available = r.Horizon - initial - internalSwap;
                if (available <= 0) continue;
                foreach (var fraction in new[] { .25, .5, .75 })
                {
                    if (clock.ElapsedMilliseconds >= 2600 || plan != null && plan.Evaluated >= EvaluationBudget)
                    { if (plan != null) plan.Budget = true; return best; }
                    var first = available * fraction; var second = available - first;
                    var state = s.ProgressionCopy();
                    var firstGains = ProjectPhase(state, a, first);
                    var secondGains = ProjectPhase(state, b, second);
                    if (plan != null) plan.Evaluated++;
                    if (firstGains == null || secondGains == null) continue;
                    var metrics = new double?[Keys.Length];
                    for (var g = 0; g < Keys.Length; g++)
                        metrics[g] = g < ProductionGoalCount ? (firstGains[g] + secondGains[g]) / r.Horizon : secondGains[g];
                    var score = Score(metrics, weights, norms, true);
                    if (best != null && (score ?? -1) <= (best.Score ?? -1)) continue;
                    best = new PresetSequence { First = a.Key, Second = b.Key, FirstSeconds = first, SecondSeconds = second,
                        SwapSeconds = internalSwap, Metrics = metrics, Score = score, Improvement = score.HasValue && baseline > 0 ? (double?)((score.Value / baseline - 1) * 100) : null };
                }
            }
            return best;
        }

        // Mutates ONLY a private progression clone. Allocation is fixed within each phase; phase two recaps once.
        internal static double?[] ProjectPhase(PresetSnapshot state, PresetCandidate policy, double seconds)
        {
            if (policy.Activities != null && (policy.Activities.EnergyTotal > 0 || policy.Activities.MagicTotal > 0)) return null;
            state.Energy = Math.Min(state.Energy, policy.EnergyPool);
            state.Magic = Math.Min(state.Magic, policy.MagicPool);
            var metrics = Pad(policy.RawMetrics, (double?)null).Select((x, g) => g < ProductionGoalCount ? x * seconds : x).ToArray();
            foreach (var energy in new[] { true, false })
            {
                var targets = energy ? state.EnergyNgu : state.MagicNgu;
                var budget = energy ? state.Energy : ResourcePercent(state.Magic, 100 - policy.BloodPercent);
                var power = energy ? policy.EnergyNguPower : policy.MagicNguPower;
                var digger = energy ? policy.EnergyDiggerBonus : policy.MagicDiggerBonus;
                var shares = NguShares(targets, state.Available[energy ? 0 : 1] ? budget : 0, power, AllocationLookaheadSeconds, policy.NguBonus, digger);
                var gain = 0d;
                for (var i = 0; i < targets.Length; i++)
                {
                    var amount = shares[i] <= 0 ? 0 : NguGain(targets[i], NguNumerator(targets[i], power, shares[i]), seconds, policy.NguBonus, digger);
                    if (!Finite(amount)) return null;
                    targets[i].Level += amount; gain += amount;
                }
                if (state.Available[energy ? 0 : 1]) metrics[energy ? 0 : 1] = gain;
            }
            var bloodDrain = 0d;
            if (policy.Ritual >= 0)
            {
                var ritual = state.Rituals.FirstOrDefault(x => x.Id == policy.Ritual && x.Unlocked && (x.Paid || state.Gold >= x.Cost));
                if (ritual == null) return null;
                var fills = BloodFills(ritual.Coefficient * policy.MagicPower * ResourcePercent(state.Magic, policy.BloodPercent));
                bloodDrain = fills * ritual.Cost;
                if (bloodDrain + policy.Support.Drain >= policy.GrossGold) return null;
                metrics[2] = fills * ritual.Blood * policy.BloodBonus * seconds;
            }
            state.Gold += (policy.GrossGold - policy.Support.Drain - bloodDrain) * seconds;
            return metrics;
        }
        internal static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }

    // Game-independent preview guard; live object identities are additionally verified by the Main adapter.
    internal sealed class PresetPreviewIdentity
    {
        internal string Id, Definition, Material;
        internal int[] EnergyWeights, MagicWeights;
        internal double Created, Elapsed, Total;
        internal long Rebirth;
        internal static bool IsToken(string id)
        { return id != null && id.Length == 32 && id.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f'); }
        internal void Validate(string id, string definition, int[] energy, int[] magic, string material,
            double now, long rebirth, double elapsed, double total)
        {
            if (!IsToken(id) || id != Id || !LoadoutPresetPlanner.Finite(now) || now < Created || now - Created > 120)
                throw new InvalidOperationException("Preview is missing, consumed, expired or invalid; preview again.");
            if (definition != Definition || !EnergyWeights.SequenceEqual(energy) || !MagicWeights.SequenceEqual(magic))
                throw new InvalidOperationException("Preset definition or NGU weights changed; preview again.");
            if (material != Material || rebirth != Rebirth || !LoadoutPresetPlanner.Finite(elapsed) || !LoadoutPresetPlanner.Finite(total)
                || elapsed < Elapsed || total < Total
                || Math.Abs((total - elapsed) - (Total - Elapsed)) > 2)
                throw new InvalidOperationException("Save epoch, clock or material inputs changed; preview again.");
        }
    }
}
