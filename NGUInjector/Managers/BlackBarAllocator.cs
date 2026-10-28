using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector.Managers
{
    internal static class BlackBarAllocator
    {
        internal const int DefaultLookaheadSeconds = 5;
        private const int GameTicksPerSecond = 50;
        internal static long ExcessIdleEnergy { get; private set; } = -1;
        internal static long ExcessIdleMagic { get; private set; } = -1;
        private static readonly Dictionary<string, long> NguBufferedCaps = new Dictionary<string, long>();
        private static int[] AppliedEnergyNguWeights;
        private static int[] AppliedMagicNguWeights;
        private static readonly string[] EnergyNguNames = {"Augments", "Wandoos", "Respawn", "Gold", "Adventure Alpha", "Power", "Drop Chance", "Magic NGU", "PP"};
        private static readonly string[] MagicNguNames = {"Yggdrasil", "EXP", "Power Beta", "Number", "Time Machine", "Energy NGU", "Adventure Beta"};

        internal static JSONObject GetNguRuntime()
        {
            var root = new JSONObject();
            root["strategy"] = "weighted-ngu-buffered-cap";
            root["capBasis"] = "last-allocation-pass";
            root["reason"] = "Misc global NGU weights are authoritative. Positive weights share up to buffered caps; zero excludes targets even from explicit NGU-n priorities. Outer priorities remain ordered.";
            root["globalNguWeightsVersion"] = 1;
            var available = Main.Settings != null && Main.Character?.NGU != null;
            root["available"] = available;
            if (!available) return root;
            foreach (var energy in new[] {true, false})
            {
                var weights = energy ? Main.Settings.EnergyNguWeights : Main.Settings.MagicNguWeights;
                var applied = energy ? AppliedEnergyNguWeights : AppliedMagicNguWeights;
                var lane = energy ? "energy" : "magic";
                var configuredJson = new JSONArray();
                foreach (var weight in weights) configuredJson.Add(weight);
                root[lane + "Weights"] = configuredJson;
                root[lane + "WeightsApplied"] = applied != null && applied.SequenceEqual(weights);
                var appliedJson = new JSONArray();
                foreach (var weight in applied ?? new int[0]) appliedJson.Add(weight);
                root[lane + "AppliedWeights"] = applied == null ? (JSONNode) JSONNull.CreateOrGet() : appliedJson;
                var rows = new JSONArray();
                for (var index = 0; index < weights.Length; index++)
                {
                    var row = new JSONObject();
                    row["index"] = index;
                    row["name"] = (energy ? EnergyNguNames : MagicNguNames)[index];
                    row["weight"] = weights[index];
                    var amount = Main.Character?.NGU == null ? 0L
                        : energy ? Main.Character.NGU.skills[index].energy : Main.Character.NGU.magicSkills[index].magic;
                    row["allocated"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    long cap;
                    var known = NguBufferedCaps.TryGetValue((energy ? "E" : "M") + index, out cap);
                    row["bufferedCap"] = known ? (JSONNode) new JSONString(cap.ToString(System.Globalization.CultureInfo.InvariantCulture)) : JSONNull.CreateOrGet();
                    row["belowBufferedCap"] = known ? (JSONNode) new JSONBool(amount < cap) : JSONNull.CreateOrGet();
                    rows.Add(row);
                }
                root[energy ? "energyTargets" : "magicTargets"] = rows;
            }
            return root;
        }

        internal static void ResetExcessIdle()
        {
            ExcessIdleEnergy = -1;
            ExcessIdleMagic = -1;
        }

        internal static void ResetAfterVerifiedSaveTestLoad()
        {
            NguBufferedCaps.Clear();
            AppliedEnergyNguWeights = AppliedMagicNguWeights = null;
            ResetExcessIdle();
        }

        internal static void ClearNguAppliedWeights(bool energy)
        {
            if (energy) AppliedEnergyNguWeights = null;
            else AppliedMagicNguWeights = null;
        }

        internal sealed class PriorityResourcePlan
        {
            internal Dictionary<ResourceTarget, long[]> Desired;
            internal readonly Dictionary<int, long> NguCaps = new Dictionary<int, long>();
            internal bool Energy;
            internal int[] Weights;
            internal long Remaining;
            internal string Message;
        }

        internal static PriorityResourcePlan[] CalculateCombatPrepFallbacks(Dictionary<ResourceTarget, long[]> desired, long energyBudget, long magicBudget, bool allowTimeMachine)
        {
            var c = Main.Character;
            var plans = new List<PriorityResourcePlan>();
            foreach (var energy in new[] { true, false })
            {
                var weights = energy ? Main.Settings.EnergyNguWeights : Main.Settings.MagicNguWeights;
                var budget = Math.Max(0, energy ? energyBudget : magicBudget);
                var plan = new PriorityResourcePlan { Energy = energy, Weights = c.buttons.ngu.interactable ? weights.ToArray() : null };
                plans.Add(plan);
                if (c.buttons.ngu.interactable)
                {
                    var caps = Enumerable.Range(0, weights.Length).Select(i => energy ? EnergyNguCapDelta(i, 0, true) : MagicNguCapDelta(i, 0, true)).ToArray();
                    for (var i = 0; i < caps.Length; i++) plan.NguCaps[i] = caps[i];
                    var shares = ShareCappedBudget(caps, budget, weights);
                    var target = energy ? ResourceTarget.EnergyNgu : ResourceTarget.MagicNgu;
                    for (var i = 0; i < shares.Length; i++)
                    {
                        desired[target][i] = shares[i];
                        budget -= shares[i];
                    }
                }
                plan.Remaining = budget;
                if (!allowTimeMachine || budget <= 0 || !c.buttons.brokenTimeMachine.interactable || c.challenges.timeMachineChallenge.inChallenge) continue;
                var cap = energy ? TimeMachineEnergyCapDelta(0, DefaultLookaheadSeconds, true) : TimeMachineMagicCapDelta(0, DefaultLookaheadSeconds, true);
                var amount = Math.Min(budget, Math.Max(0, cap));
                desired[energy ? ResourceTarget.TimeMachineEnergy : ResourceTarget.TimeMachineMagic][0] = amount;
                plan.Remaining -= amount;
            }
            return plans.ToArray();
        }

        internal static bool IsValidPriority(string token, bool energy)
        {
            return ActivityPolicy.Current.IsValidPriority(token, energy);
        }

        internal static string[] DefaultPriorities(CustomStageFocus focus, bool energy)
        {
            return ActivityPolicy.Current.DefaultPriorities((int) focus, energy);
        }

        internal static PriorityResourcePlan CalculatePriorities(string[] priorities, bool energy, int lookaheadSeconds,
            Func<long, double, int> selectRitual = null, Func<long, AugmentOptimizerResult> selectAugment = null,
            long[] atTargets = null)
        {
            var nguWeights = energy ? Main.Settings.EnergyNguWeights : Main.Settings.MagicNguWeights;
            var plan = new PriorityResourcePlan { Energy = energy, Weights = nguWeights.ToArray(),
                Desired = ResourceWriter.EmptyDesiredMap(energy, !energy), Remaining = Main.reference?.ExecutionResourceBudget(energy,
                    ResourceWriter.Reclaimable(energy)) ?? ResourceWriter.Reclaimable(energy) };
            var messages = new List<string>();
            ResolveAllocations(plan, priorities, lookaheadSeconds, messages, selectRitual, selectAugment, atTargets);
            if (plan.Remaining > 0 && messages.Count > 0)
                messages.Add($"configured priorities capped, complete, or unavailable; {plan.Remaining:n0} left idle");
            plan.Message = messages.Count == 0 ? null : $"{(energy ? "Energy" : "Magic")} priority allocation (weighted NGU groups, equal AT/augment pairs, ordered buffered caps): " + string.Join("; ", messages.ToArray());
            return plan;
        }

        internal static void ObserveApplied(PriorityResourcePlan plan)
        {
            var prefix = plan.Energy ? "E" : "M";
            foreach (var key in NguBufferedCaps.Keys.Where(key => key.StartsWith(prefix)).ToArray()) NguBufferedCaps.Remove(key);
            foreach (var cap in plan.NguCaps) NguBufferedCaps[prefix + cap.Key] = cap.Value;
            if (plan.Energy) { AppliedEnergyNguWeights = plan.Weights; ExcessIdleEnergy = plan.Remaining > 0 ? plan.Remaining : -1; }
            else { AppliedMagicNguWeights = plan.Weights; ExcessIdleMagic = plan.Remaining > 0 ? plan.Remaining : -1; }
        }

        internal static long[] ShareCappedBudget(long[] caps, long available, int[] weights)
        {
            return ResourceAllocation.ShareCapped(caps, available, weights);
        }

        internal static long[] ShareAugmentBudget(int[] indexes, long available, int lookaheadSeconds)
        { return ShareAugmentBudget(indexes, available, lookaheadSeconds, true); }

        private static long[] ShareAugmentBudget(int[] indexes, long available, int lookaheadSeconds, bool absolute)
        {
            var c = Main.Character;
            var caps = indexes.Select(i => AugmentCapDelta(i, Math.Max(3, lookaheadSeconds), absolute)).ToArray();
            var requiredGold = 0d;
            for (var i = 0; i < indexes.Length; i++)
            {
                if (caps[i] <= 0) continue;
                var id = indexes[i];
                var data = c.augments.augs[id / 2];
                var bar = c.augmentsController.augments[id / 2];
                if ((id % 2 == 0 ? data.augProgress : data.upgradeProgress) <= 0)
                    requiredGold += Math.Max(0d, id % 2 == 0 ? bar.getAugCost() : bar.getUpgradeCost());
            }
            if (requiredGold > c.realGold) return new long[indexes.Length];
            return ShareCappedBudget(caps, available, Enumerable.Repeat(1, indexes.Length).ToArray());
        }

        private static void ResolveAllocations(PriorityResourcePlan plan, string[] priorities,
            int lookaheadSeconds, List<string> messages, Func<long, double, int> selectRitual,
            Func<long, AugmentOptimizerResult> selectAugment, long[] atTargets)
        {
            var c = Main.Character;
            var energy = plan.Energy; var nguWeights = plan.Weights;
            Action<ResourceTarget, int, long> fund = (target, id, amount) => {
                amount = Math.Min(plan.Remaining, Math.Max(0, amount));
                plan.Desired[target][id] = checked(plan.Desired[target][id] + amount);
                plan.Remaining -= amount;
            };
            var lookahead = Math.Max(3, lookaheadSeconds);
            var queue = new Queue<string>(priorities ?? new string[0]);
            var visited = new HashSet<string>();
            var goldBudget = energy ? 0d : Math.Max(0d, c.grossGoldPerSecond());
            while (queue.Count > 0)
            {
                var idle = plan.Remaining;
                if (idle <= 0)
                    break;
                var token = (queue.Dequeue() ?? string.Empty).Trim().ToUpperInvariant();
                if (!IsValidPriority(token, energy) || !visited.Add(token))
                    continue;

                var parts = token.Split('-');
                var kind = parts[0];
                var index = parts.Length == 2 ? int.Parse(parts[1]) : 0;
                string[] expansion = null;
                if (token == "NGU")
                {
                    if (!c.buttons.ngu.interactable)
                        continue;
                    var indexes = Enumerable.Range(0, nguWeights.Length)
                        .Where(i => nguWeights[i] > 0 && visited.Add("NGU-" + i)).ToArray();
                    var caps = indexes.Select(i => energy ? EnergyNguCapDelta(i, lookahead, true) : MagicNguCapDelta(i, lookahead, true)).ToArray();
                    for (var i = 0; i < indexes.Length; i++)
                        plan.NguCaps[indexes[i]] = caps[i];
                    var shares = ShareCappedBudget(caps, idle, indexes.Select(i => nguWeights[i]).ToArray());
                    for (var i = 0; i < indexes.Length; i++)
                    {
                        var target = indexes[i];
                        var groupCap = caps[i];
                        if (groupCap <= 0)
                        {
                            messages.Add($"NGU-{target} capped or complete");
                            continue;
                        }
                        fund(energy ? ResourceTarget.EnergyNgu : ResourceTarget.MagicNgu, target, shares[i]);
                        messages.Add($"NGU-{target} {shares[i]:n0} (shared{(shares[i] < groupCap ? "; below buffered cap" : string.Empty)})");
                    }
                    continue;
                }
                else if (token == "AT")
                    expansion = new[] {"AT-1", "AT-0"};
                else if (token == "AUG")
                {
                    var aug = selectAugment != null ? selectAugment(idle)
                        : AugmentOptimizer.OptimizeForActualBudget(DefaultFocusEstimate(), idle, lookahead);
                    if (aug.Success)
                        expansion = aug.UsesUpgrade ? new[] {"AUG-" + aug.AugmentIndex * 2, "AUG-" + (aug.AugmentIndex * 2 + 1)} : new[] {"AUG-" + aug.AugmentIndex * 2};
                    else
                        continue;
                }
                else if (token == "RIT")
                {
                    var ritualIndex = selectRitual != null
                        ? selectRitual(idle, goldBudget)
                        : BloodMagicOptimizer.OptimizeBest(idle, goldBudget).RitualIndex;
                    if (ritualIndex < 0)
                        continue;
                    expansion = new[] {"RIT-" + ritualIndex};
                }
                if (expansion != null)
                {
                    queue = new Queue<string>(expansion.Concat(queue));
                    continue;
                }

                // Adjacent related bars are one priority, not competing sequential sinks.
                var partner = kind == "AUG" ? index ^ 1
                    : kind == "AT" && index < 2 ? 1 - index
                    : kind == "AT" && index >= 3 ? 7 - index : -1;
                var partnerToken = kind + "-" + partner;
                if (energy && partner >= 0 && (kind == "AUG" || kind == "AT") && queue.Count > 0
                    && string.Equals((queue.Peek() ?? string.Empty).Trim(), partnerToken, StringComparison.OrdinalIgnoreCase)
                    && !visited.Contains(partnerToken))
                {
                    queue.Dequeue();
                    visited.Add(partnerToken);
                    if (kind == "AUG" ? !c.buttons.augmentation.interactable || c.challenges.noAugsChallenge.inChallenge
                        : !c.buttons.advancedTraining.interactable)
                        continue;

                    var indexes = new[] {index, partner};
                    var caps = indexes.Select(i => kind == "AUG" ? AugmentCapDelta(i, lookahead, true) : AdvancedTrainingCapDelta(i, lookahead, true, atTargets)).ToArray();
                    var shares = kind == "AUG" ? ShareAugmentBudget(indexes, idle, lookahead, true)
                        : ShareCappedBudget(caps, idle, new[] {1, 1});
                    for (var i = 0; i < indexes.Length; i++)
                    {
                        var target = indexes[i];
                        var cap = caps[i];
                        if (cap <= 0)
                        {
                            messages.Add($"{kind}-{target} capped or complete");
                            continue;
                        }
                        fund(kind == "AUG" ? target % 2 == 0 ? ResourceTarget.Augment : ResourceTarget.AugmentUpgrade : ResourceTarget.AdvancedTraining,
                            kind == "AUG" ? target / 2 : target, shares[i]);
                        messages.Add($"{kind}-{target} {shares[i]:n0} (shared{(shares[i] < cap ? "; below buffered cap" : string.Empty)})");
                    }
                    continue;
                }

                long needed;
                ResourceTarget destination;
                switch (kind)
                {
                    case "WAN":
                        if (!c.buttons.wandoos.interactable || c.wandoos98.disabled)
                            continue;
                        needed = energy ? WandoosEnergyCapDelta(0, true) : WandoosMagicCapDelta(0, true);
                        destination = energy ? ResourceTarget.WandoosEnergy : ResourceTarget.WandoosMagic;
                        break;
                    case "TM":
                        if (!c.buttons.brokenTimeMachine.interactable || c.challenges.timeMachineChallenge.inChallenge)
                            continue;
                        needed = energy ? TimeMachineEnergyCapDelta(0, lookahead, true) : TimeMachineMagicCapDelta(0, lookahead, true);
                        destination = energy ? ResourceTarget.TimeMachineEnergy : ResourceTarget.TimeMachineMagic;
                        break;
                    case "NGU":
                        if (!c.buttons.ngu.interactable || index >= nguWeights.Length || nguWeights[index] <= 0)
                            continue;
                        needed = energy ? EnergyNguCapDelta(index, lookahead, true) : MagicNguCapDelta(index, lookahead, true);
                        plan.NguCaps[index] = needed;
                        destination = energy ? ResourceTarget.EnergyNgu : ResourceTarget.MagicNgu;
                        break;
                    case "AUG":
                        if (!c.buttons.augmentation.interactable || c.challenges.noAugsChallenge.inChallenge)
                            continue;
                        needed = AugmentCapDelta(index, lookahead, true);
                        destination = index % 2 == 0 ? ResourceTarget.Augment : ResourceTarget.AugmentUpgrade;
                        index /= 2;
                        break;
                    case "AT":
                        if (!c.buttons.advancedTraining.interactable)
                            continue;
                        needed = AdvancedTrainingCapDelta(index, lookahead, true, atTargets);
                        destination = ResourceTarget.AdvancedTraining;
                        break;
                    case "RIT":
                        // Check gold sustainability at the actual remaining allocation, not total capacity.
                        if (!c.buttons.bloodMagic.interactable)
                            continue;
                        needed = BloodRitualCapDelta(index, true);
                        if (needed > 0)
                        {
                            var ritual = BloodMagicOptimizer.OptimizeTarget(index, Math.Min(idle, needed), goldBudget);
                            if (!ritual.Success)
                                continue;
                            goldBudget = Math.Max(0d, goldBudget - ritual.BloodMagicGoldPerSecond);
                        }
                        destination = ResourceTarget.BloodRitual;
                        break;
                    default:
                        continue;
                }
                if (needed <= 0)
                {
                    messages.Add($"{token} capped or complete");
                    continue;
                }
                var funded = Math.Min(idle, needed);
                fund(destination, index, funded);
                messages.Add($"{token} {funded:n0}{(funded < needed ? $" (below buffered cap: need {needed:n0}, resource shortage; later priorities wait)" : string.Empty)}");
            }
        }

        private static int DefaultFocusEstimate()
        {
            return Main.Settings == null ? 300 : Main.Settings.CustomAugmentsBloodEstimateSeconds;
        }

        private static bool EnergyNguTargetReached(int id)
        {
            var target = EnergyNguTarget(id);
            return target < 0 || target > 0 && EnergyNguLevel(id) >= target;
        }

        private static bool MagicNguTargetReached(int id)
        {
            var target = MagicNguTarget(id);
            return target < 0 || target > 0 && MagicNguLevel(id) >= target;
        }

        private static long TimeMachineEnergyCapDelta(int id, int lookaheadSeconds, bool absolute = false)
        {
            var c = Main.Character;
            if (c.machine.speedTarget == -1 || c.machine.speedTarget > 0 && c.machine.levelSpeed >= c.machine.speedTarget)
                return 0;

            var divisor = (double)c.totalEnergyPower()
                          * c.allChallenges.timeMachineChallenge.TMSpeedBonus()
                          * c.hacksController.totalTMSpeedBonus()
                          * c.cardsController.getBonus(cardBonus.TMSpeed);
            if (divisor <= 0d)
                return 0;

            var projectedLevel = ProjectedNextLevel(c.machine.levelSpeed, c.machine.speedTarget, lookaheadSeconds);
            var cap = 50000d * projectedLevel * c.timeMachineController.baseSpeedDivider() / divisor;
            if (c.settings.rebirthDifficulty >= difficulty.sadistic)
                cap *= c.timeMachineController.sadisticDivider();

            return CapDelta(cap * 1.000002d, absolute ? 0 : c.machine.speedEnergy);
        }

        private static long TimeMachineMagicCapDelta(int id, int lookaheadSeconds, bool absolute = false)
        {
            var c = Main.Character;
            if (c.machine.multiTarget == -1 || c.machine.multiTarget > 0 && c.machine.levelGoldMulti >= c.machine.multiTarget)
                return 0;

            var divisor = (double)c.totalMagicPower()
                          * c.allChallenges.timeMachineChallenge.TMSpeedBonus()
                          * c.hacksController.totalTMSpeedBonus()
                          * c.cardsController.getBonus(cardBonus.TMSpeed);
            if (divisor <= 0d)
                return 0;

            var projectedLevel = ProjectedNextLevel(c.machine.levelGoldMulti, c.machine.multiTarget, lookaheadSeconds);
            var cap = 50000d * projectedLevel * c.timeMachineController.baseGoldMultiDivider() / divisor;
            if (c.settings.rebirthDifficulty >= difficulty.sadistic)
                cap *= c.timeMachineController.sadisticDivider();

            return CapDelta(cap * 1.000002d, absolute ? 0 : c.machine.goldMultiMagic);
        }

        private static long WandoosEnergyCapDelta(int id, bool absolute = false)
        {
            var c = Main.Character;
            var speed = (double)c.totalWandoosEnergySpeed();
            return speed <= 0 ? 0 : CapDelta(c.wandoos98Controller.baseEnergyTime() / speed * 1.000002d, absolute ? 0 : c.wandoos98.wandoosEnergy);
        }

        private static long WandoosMagicCapDelta(int id, bool absolute = false)
        {
            var c = Main.Character;
            var speed = (double)c.totalWandoosMagicSpeed();
            return speed <= 0 ? 0 : CapDelta(c.wandoos98Controller.baseMagicTime() / speed * 1.000002d, absolute ? 0 : c.wandoos98.wandoosMagic);
        }

        private static long AugmentCapDelta(int id, int lookaheadSeconds, bool absolute = false)
        {
            var c = Main.Character;
            var augIndex = id / 2;
            if (augIndex < 0 || augIndex >= c.augmentsController.augments.Length)
                return 0;

            var aug = c.augmentsController.augments[augIndex];
            if (id % 2 == 0)
            {
                if (aug.augLocked() || aug.hitAugmentTarget() || c.augments.augs[augIndex].augProgress <= 0 && c.realGold < aug.getAugCost())
                    return 0;

                var level = ProjectedNextLevel(c.augments.augs[augIndex].augLevel, c.augments.augs[augIndex].augmentTarget, lookaheadSeconds);
                var formula = AugmentCapFormula(augIndex, level, false);
                return CapDelta(formula * 1.000002d, absolute ? 0 : c.augments.augs[augIndex].augEnergy);
            }

            if (aug.upgradeLocked() || aug.hitUpgradeTarget() || c.augments.augs[augIndex].upgradeProgress <= 0 && c.realGold < aug.getUpgradeCost())
                return 0;

            var upgradeLevel = ProjectedNextLevel(c.augments.augs[augIndex].upgradeLevel, c.augments.augs[augIndex].upgradeTarget, lookaheadSeconds);
            var upgradeFormula = AugmentCapFormula(augIndex, upgradeLevel, true);
            return CapDelta(upgradeFormula * 1.000002d, absolute ? 0 : c.augments.augs[augIndex].upgradeEnergy);
        }

        private static double AugmentCapFormula(int augIndex, double level, bool upgrade)
        {
            var c = Main.Character;
            var divisor = (double)c.totalEnergyPower()
                          * (1d + c.inventoryController.bonuses[specType.Augs])
                          * c.inventory.macguffinBonuses[12]
                          * c.hacksController.totalAugSpeedBonus()
                          * c.cardsController.getBonus(cardBonus.augSpeed)
                          * c.adventureController.itopod.totalAugSpeedBonus()
                          * (1d + c.allChallenges.noAugsChallenge.evilCompletions() * 0.0500000007450581d);
            if (divisor <= 0d)
                return 0d;

            var formula = (c.settings.rebirthDifficulty == difficulty.sadistic ? 1d : 50000d) * level / divisor;
            if (c.allChallenges.noAugsChallenge.completions() >= 1)
                formula /= 1.10000002384186d;
            if (c.allChallenges.noAugsChallenge.evilCompletions() >= c.allChallenges.noAugsChallenge.maxCompletions)
                formula /= 1.25d;
            if (c.settings.rebirthDifficulty >= difficulty.sadistic)
                formula *= c.augmentsController.augments[augIndex].sadisticDivider();

            if (upgrade)
            {
                if (c.settings.rebirthDifficulty == difficulty.normal)
                    formula *= c.augmentsController.normalUpgradeSpeedDividers[augIndex];
                else if (c.settings.rebirthDifficulty == difficulty.evil)
                    formula *= c.augmentsController.evilUpgradeSpeedDividers[augIndex];
                else if (c.settings.rebirthDifficulty == difficulty.sadistic)
                    formula *= c.augmentsController.sadisticUpgradeSpeedDividers[augIndex];
            }
            else
            {
                if (c.settings.rebirthDifficulty == difficulty.normal)
                    formula *= c.augmentsController.normalAugSpeedDividers[augIndex];
                else if (c.settings.rebirthDifficulty == difficulty.evil)
                    formula *= c.augmentsController.evilAugSpeedDividers[augIndex];
                else if (c.settings.rebirthDifficulty == difficulty.sadistic)
                    formula *= c.augmentsController.sadisticAugSpeedDividers[augIndex];
            }

            return formula;
        }

        private static long BloodRitualCapDelta(int id, bool absolute = false)
        {
            var c = Main.Character;
            if (!c.buttons.bloodMagic.interactable || id < 0 || id >= c.bloodMagic.ritual.Count || id >= c.bloodMagicController.ritualsUnlocked())
                return 0;

            var ritual = c.bloodMagic.ritual[id];
            var controller = c.bloodMagicController.bloodMagics[id];
            if (c.bossID <= ritual.boss)
                return 0;
            if (ritual.progress <= 0 && c.realGold <= controller.currentCost())
                return 0;

            var divisor = BloodRitualSpeedDivider(id);
            var power = (double)c.totalMagicPower();
            var speedBonus = controller.totalBloodMagicSpeedBonus();
            if (divisor <= 0d || power <= 0d || speedBonus <= 0d)
                return 0;

            return CapDelta(divisor / (power * speedBonus) * 1.000002d, absolute ? 0 : ritual.magic);
        }

        private static double BloodRitualSpeedDivider(int id)
        {
            var c = Main.Character;
            if (c.settings.rebirthDifficulty == difficulty.normal)
                return c.bloodMagicController.normalSpeedDividers[id] * 50000d;
            if (c.settings.rebirthDifficulty == difficulty.evil)
                return c.bloodMagicController.evilSpeedDividers[id] * 50000d;
            if (c.settings.rebirthDifficulty == difficulty.sadistic)
                return c.bloodMagicController.sadisticSpeedDividers[id] * c.bloodMagicController.bloodMagics[id].sadisticDivider();
            return 0d;
        }

        private static long EnergyNguCapDelta(int id, int lookaheadSeconds, bool absolute = false)
        {
            var c = Main.Character;
            if (EnergyNguTargetReached(id))
                return 0;

            var divisor = (double)c.totalEnergyPower()
                          * c.totalNGUSpeedBonus()
                          * c.adventureController.itopod.totalEnergyNGUBonus()
                          * c.inventory.macguffinBonuses[4]
                          * c.NGUController.energyNGUBonus()
                          * c.allDiggers.totalEnergyNGUBonus()
                          * c.hacksController.totalEnergyNGUBonus()
                          * c.beastQuestPerkController.totalEnergyNGUSpeed()
                          * c.wishesController.totalEnergyNGUSpeed()
                          * c.cardsController.getBonus(cardBonus.energyNGUSpeed);
            if (c.allChallenges.trollChallenge.sadisticCompletions() >= 1)
                divisor *= 3d;
            if (c.settings.nguLevelTrack >= difficulty.sadistic)
                divisor /= c.NGUController.NGU[id].sadisticDivider();
            if (divisor <= 0d)
                return 0;

            var projectedLevel = ProjectedNextLevel(EnergyNguLevel(id), EnergyNguTarget(id), lookaheadSeconds);
            var formula = c.NGUController.energySpeedDivider(id) * projectedLevel / divisor;
            return CapDelta(formula * 1.000002d, absolute ? 0 : c.NGU.skills[id].energy);
        }

        private static long MagicNguCapDelta(int id, int lookaheadSeconds, bool absolute = false)
        {
            var c = Main.Character;
            if (MagicNguTargetReached(id))
                return 0;

            var divisor = (double)c.totalMagicPower()
                          * c.totalNGUSpeedBonus()
                          * c.adventureController.itopod.totalMagicNGUBonus()
                          * c.inventory.macguffinBonuses[5]
                          * c.NGUController.magicNGUBonus()
                          * c.allDiggers.totalMagicNGUBonus()
                          * c.hacksController.totalMagicNGUBonus()
                          * c.beastQuestPerkController.totalMagicNGUSpeed()
                          * c.wishesController.totalMagicNGUSpeed()
                          * c.cardsController.getBonus(cardBonus.magicNGUSpeed);
            if (c.allChallenges.trollChallenge.completions() >= 1)
                divisor *= 3d;
            if (c.settings.nguLevelTrack >= difficulty.sadistic)
                divisor /= c.NGUController.NGUMagic[id].sadisticDivider();
            if (divisor <= 0d)
                return 0;

            var projectedLevel = ProjectedNextLevel(MagicNguLevel(id), MagicNguTarget(id), lookaheadSeconds);
            var formula = c.NGUController.magicSpeedDivider(id) * projectedLevel / divisor;
            return CapDelta(formula * 1.000002d, absolute ? 0 : c.NGU.magicSkills[id].magic);
        }

        private static long AdvancedTrainingCapDelta(int id, int lookaheadSeconds, bool absolute = false, long[] targets = null)
        {
            var c = Main.Character;
            var target = targets != null && id >= 0 && id < targets.Length ? targets[id] : id >= 0 && id < c.advancedTraining.levelTarget.Length ? c.advancedTraining.levelTarget[id] : -1;
            if (id < 0 || id >= c.advancedTraining.energy.Length || target < 0 || target > 0 && c.advancedTraining.level[id] >= target)
                return 0;
            if (c.wishes.wishes[190].level >= 1)
                return 0;

            var power = (double)c.totalEnergyPower();
            var speed = (double)c.totalAdvancedTrainingSpeedBonus();
            if (power <= 0d || speed <= 0d)
                return 0;

            var projectedLevel = ProjectedNextLevel(c.advancedTraining.level[id], target, lookaheadSeconds);
            var cap = 50d * AdvancedTrainingBaseTime(id) * projectedLevel * Math.Sqrt(power) / (speed * power);
            return CapDelta(cap * 1.000002d, absolute ? 0 : c.advancedTraining.energy[id]);
        }

        private static bool AdvancedTrainingTargetReached(int id)
        {
            var c = Main.Character;
            var target = c.advancedTraining.levelTarget[id];
            return target < 0 || target > 0 && c.advancedTraining.level[id] >= target;
        }

        private static float AdvancedTrainingBaseTime(int id)
        {
            var c = Main.Character;
            switch (id)
            {
                case 0:
                    return c.advancedTrainingController.defense.baseTime;
                case 1:
                    return c.advancedTrainingController.attack.baseTime;
                case 2:
                    return c.advancedTrainingController.block.baseTime;
                case 3:
                    return c.advancedTrainingController.wandoosEnergy.baseTime;
                case 4:
                    return c.advancedTrainingController.wandoosMagic.baseTime;
                default:
                    return 0f;
            }
        }

        private static long EnergyNguLevel(int id)
        {
            var c = Main.Character;
            if (c.settings.nguLevelTrack == difficulty.normal)
                return c.NGU.skills[id].level;
            if (c.settings.nguLevelTrack == difficulty.evil)
                return c.NGU.skills[id].evilLevel;
            return c.NGU.skills[id].sadisticLevel;
        }

        private static long MagicNguLevel(int id)
        {
            var c = Main.Character;
            if (c.settings.nguLevelTrack == difficulty.normal)
                return c.NGU.magicSkills[id].level;
            if (c.settings.nguLevelTrack == difficulty.evil)
                return c.NGU.magicSkills[id].evilLevel;
            return c.NGU.magicSkills[id].sadisticLevel;
        }

        private static long EnergyNguTarget(int id)
        {
            var c = Main.Character;
            if (c.settings.nguLevelTrack == difficulty.normal)
                return c.NGU.skills[id].target;
            if (c.settings.nguLevelTrack == difficulty.evil)
                return c.NGU.skills[id].evilTarget;
            return c.NGU.skills[id].sadisticTarget;
        }

        private static long MagicNguTarget(int id)
        {
            var c = Main.Character;
            if (c.settings.nguLevelTrack == difficulty.normal)
                return c.NGU.magicSkills[id].target;
            if (c.settings.nguLevelTrack == difficulty.evil)
                return c.NGU.magicSkills[id].evilTarget;
            return c.NGU.magicSkills[id].sadisticTarget;
        }

        private static double ProjectedNextLevel(long currentLevel, long target, int lookaheadSeconds)
        {
            // One level per game tick while black-barred. Include an extra tick for phase/rounding.
            var projected = currentLevel + 1d + (double)Math.Max(0, lookaheadSeconds) * GameTicksPerSecond + 1d;
            if (target > 0)
                projected = Math.Min(projected, target);
            return Math.Max(1d, projected);
        }

        private static long CapDelta(double cap, long current)
        {
            var target = CapToLong(cap);
            var delta = target - current;
            if (current + delta < 0)
                delta = -current;
            return delta;
        }

        private static long CapToLong(double value)
        {
            if (double.IsNaN(value) || value <= 0d)
                return 0;
            if (double.IsInfinity(value) || value >= long.MaxValue)
                return long.MaxValue;

            // Always round up: a fractional cap of 1.5 must allocate 2, never 1.
            var result = value <= 1d ? 1L : (long)Math.Ceiling(value);
            return result;
        }

        internal static long AddResource(long current, long amount)
        {
            if (amount > 0 && current > long.MaxValue - amount)
                return long.MaxValue;
            if (amount < 0 && current < -amount)
                return 0;
            return current + amount;
        }

    }
}
