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
        private static readonly string[] EnergyNguNames = {"Augments", "Wandoos", "Respawn", "Gold", "Adventure Alpha", "Power Alpha", "Drop Chance", "Magic NGU", "PP"};
        private static readonly string[] MagicNguNames = {"Yggdrasil", "EXP", "Power Beta", "Number", "Time Machine", "Energy NGU", "Adventure Beta"};

        internal static JSONObject GetNguRuntime(int[] energyWeights, int[] magicWeights)
        {
            var root = new JSONObject();
            root["strategy"] = "weighted-ngu-buffered-cap";
            root["capBasis"] = "last-allocation-pass";
            root["reason"] = "Positive NGU weights divide each resource proportionally up to buffered caps, redistributing capped shares. Zero excludes a target; outer priorities and explicit NGU-n targets remain ordered.";
            root["available"] = Main.Character?.NGU != null;
            foreach (var energy in new[] {true, false})
            {
                var weights = energy ? energyWeights : magicWeights;
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

        internal static void ClearNguAppliedWeights(bool energy)
        {
            if (energy) AppliedEnergyNguWeights = null;
            else AppliedMagicNguWeights = null;
        }

        internal static bool IsValidPriority(string token, bool energy)
        {
            token = (token ?? string.Empty).Trim().ToUpperInvariant();
            if (token == "WAN" || token == "TM" || token == "NGU")
                return true;
            if (token == "AUG" || token == "AT")
                return energy;
            if (token == "RIT")
                return !energy;
            var parts = token.Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var index) || index < 0)
                return false;
            if (parts[1] != index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                return false;
            return parts[0] == "NGU" && index < (energy ? 9 : 7)
                   || energy && parts[0] == "AUG" && index < 14
                   || energy && parts[0] == "AT" && index < 5
                   || !energy && parts[0] == "RIT" && index < 8;
        }

        internal static string[] DefaultPriorities(CustomStageFocus focus, bool energy)
        {
            switch (focus)
            {
                case CustomStageFocus.TimeMachine:
                    return energy ? new[] {"TM", "NGU", "WAN", "AT"} : new[] {"TM", "RIT", "NGU", "WAN"};
                case CustomStageFocus.Augments:
                    return energy ? new[] {"AUG", "NGU", "WAN", "TM"} : new[] {"RIT", "NGU", "WAN", "TM"};
                case CustomStageFocus.BloodMagic:
                    return energy ? new string[0] : new[] {"RIT", "NGU", "WAN", "TM"};
                case CustomStageFocus.Wandoos:
                    return energy ? new[] {"WAN", "NGU", "TM", "AT"} : new[] {"WAN", "RIT", "NGU", "TM"};
                case CustomStageFocus.EnergyNgu:
                    return energy ? new[] {"NGU", "WAN", "TM", "AT"} : new string[0];
                case CustomStageFocus.MagicNgu:
                    return energy ? new string[0] : new[] {"NGU", "RIT", "WAN", "TM"};
                case CustomStageFocus.AdvancedTraining:
                    return energy ? new[] {"AT", "NGU", "WAN", "TM"} : new[] {"RIT", "NGU", "WAN", "TM"};
                default:
                    return new string[0];
            }
        }

        // Callers recall once before this pass, so growing high priorities can reclaim lower allocations.
        internal static string AllocatePriorities(string[] priorities, bool energy, int lookaheadSeconds, int[] nguWeights,
            Func<long, double, int> selectRitual = null)
        {
            var c = Main.Character;
            var idle = energy ? c.idleEnergy : c.magic.idleMagic;
            if (energy) AppliedEnergyNguWeights = null;
            else AppliedMagicNguWeights = null;
            var messages = new List<string>();
            foreach (var key in NguBufferedCaps.Keys.Where(key => key.StartsWith(energy ? "E" : "M")).ToArray())
                NguBufferedCaps.Remove(key);
            AllocateInOrder(ResolveAllocations(priorities, energy, lookaheadSeconds, nguWeights, messages, selectRitual), idle);
            if (energy) AppliedEnergyNguWeights = nguWeights.ToArray();
            else AppliedMagicNguWeights = nguWeights.ToArray();
            var remaining = energy ? c.idleEnergy : c.magic.idleMagic;
            if (energy)
                ExcessIdleEnergy = remaining > 0 && messages.Count > 0 ? remaining : -1;
            else
                ExcessIdleMagic = remaining > 0 && messages.Count > 0 ? remaining : -1;
            if (remaining > 0 && messages.Count > 0)
                messages.Add($"configured priorities capped, complete, or unavailable; {remaining:n0} left idle");
            return messages.Count == 0 ? null : $"{(energy ? "Energy" : "Magic")} priority allocation (weighted NGU groups, ordered buffered caps): " + string.Join("; ", messages.ToArray());
        }

        private static long AllocateInOrder(IEnumerable<KeyValuePair<long, Action<long>>> targets, long available)
        {
            var remaining = Math.Max(0, available);
            using (var iterator = targets.GetEnumerator())
            {
                while (remaining > 0 && iterator.MoveNext())
                {
                    var target = iterator.Current;
                    var amount = Math.Min(remaining, Math.Max(0, target.Key));
                    if (amount > 0)
                        target.Value(amount);
                    remaining -= amount;
                }
            }
            return Math.Max(0, available) - remaining;
        }

        internal static long[] ShareCappedBudget(long[] caps, long available, int[] weights)
        {
            var amounts = new long[caps.Length];
            var active = Enumerable.Range(0, caps.Length).Where(i => caps[i] > 0 && weights[i] > 0).ToList();
            var remaining = Math.Max(0, available);
            while (remaining > 0 && active.Count > 0)
            {
                var totalWeight = active.Sum(i => (long) weights[i]);
                var fractions = new decimal[caps.Length];
                var shares = new long[caps.Length];
                foreach (var i in active)
                {
                    // long * Int32 fits decimal exactly. Subtract the exact remainder before division
                    // so decimal rounding cannot turn a near-integer quotient into an extra resource.
                    var scaled = (decimal) remaining * weights[i];
                    fractions[i] = scaled % totalWeight;
                    shares[i] = (long) ((scaled - fractions[i]) / totalWeight);
                }
                var capped = active.Where(i => caps[i] <= shares[i]).ToArray();
                if (capped.Length > 0)
                {
                    // Remove capped members before redividing, without summing overflowing cap totals.
                    foreach (var i in capped)
                    {
                        amounts[i] = caps[i];
                        remaining -= caps[i];
                        active.Remove(i);
                    }
                    continue;
                }

                foreach (var i in active)
                {
                    amounts[i] = shares[i];
                    remaining -= shares[i];
                }
                foreach (var i in active.OrderByDescending(i => fractions[i]).ThenBy(i => i).Take((int) remaining))
                    amounts[i]++;
                break;
            }
            return amounts;
        }

        private static IEnumerable<KeyValuePair<long, Action<long>>> ResolveAllocations(string[] priorities, bool energy,
            int lookaheadSeconds, int[] nguWeights, List<string> messages, Func<long, double, int> selectRitual)
        {
            var c = Main.Character;
            var lookahead = Math.Max(3, lookaheadSeconds);
            var queue = new Queue<string>(priorities ?? new string[0]);
            var visited = new HashSet<string>();
            var goldBudget = energy ? 0d : Math.Max(0d, c.grossGoldPerSecond());
            while (queue.Count > 0)
            {
                var idle = energy ? c.idleEnergy : c.magic.idleMagic;
                if (idle <= 0)
                    yield break;
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
                    var caps = indexes.Select(i => energy ? EnergyNguCapDelta(i, lookahead) : MagicNguCapDelta(i, lookahead)).ToArray();
                    for (var i = 0; i < indexes.Length; i++)
                        NguBufferedCaps[(energy ? "E" : "M") + indexes[i]] = AddResource(
                            energy ? c.NGU.skills[indexes[i]].energy : c.NGU.magicSkills[indexes[i]].magic, caps[i]);
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
                        yield return new KeyValuePair<long, Action<long>>(shares[i], amount =>
                        {
                            if (energy)
                                AddEnergyNgu(target, amount);
                            else
                                AddMagicNgu(target, amount);
                            messages.Add($"NGU-{target} {amount:n0} (shared{(amount < groupCap ? "; below buffered cap" : string.Empty)})");
                        });
                    }
                    continue;
                }
                else if (token == "AT")
                    expansion = new[] {"AT-1", "AT-0"};
                else if (token == "AUG")
                {
                    var aug = AugmentOptimizer.OptimizeBest(DefaultFocusEstimate(), true);
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

                long needed;
                Action<int, long> add;
                switch (kind)
                {
                    case "WAN":
                        if (!c.buttons.wandoos.interactable || c.wandoos98.disabled)
                            continue;
                        needed = energy ? WandoosEnergyCapDelta(0) : WandoosMagicCapDelta(0);
                        add = energy ? (Action<int, long>)AddWandoosEnergy : AddWandoosMagic;
                        break;
                    case "TM":
                        if (!c.buttons.brokenTimeMachine.interactable || c.challenges.timeMachineChallenge.inChallenge)
                            continue;
                        needed = energy ? TimeMachineEnergyCapDelta(0, lookahead) : TimeMachineMagicCapDelta(0, lookahead);
                        add = energy ? (Action<int, long>)AddTimeMachineEnergy : AddTimeMachineMagic;
                        break;
                    case "NGU":
                        if (!c.buttons.ngu.interactable)
                            continue;
                        needed = energy ? EnergyNguCapDelta(index, lookahead) : MagicNguCapDelta(index, lookahead);
                        NguBufferedCaps[(energy ? "E" : "M") + index] = AddResource(energy ? c.NGU.skills[index].energy : c.NGU.magicSkills[index].magic, needed);
                        add = energy ? (Action<int, long>)AddEnergyNgu : AddMagicNgu;
                        break;
                    case "AUG":
                        if (!c.buttons.augmentation.interactable || c.challenges.noAugsChallenge.inChallenge)
                            continue;
                        needed = AugmentCapDelta(index, lookahead);
                        add = AddAugmentEnergy;
                        break;
                    case "AT":
                        if (!c.buttons.advancedTraining.interactable)
                            continue;
                        needed = AdvancedTrainingCapDelta(index, lookahead);
                        add = AddAdvancedTrainingEnergy;
                        break;
                    case "RIT":
                        // Check gold sustainability at the actual remaining allocation, not total capacity.
                        if (!c.buttons.bloodMagic.interactable)
                            continue;
                        needed = BloodRitualCapDelta(index);
                        if (needed > 0)
                        {
                            var ritual = BloodMagicOptimizer.OptimizeTarget(index, Math.Min(idle, needed), goldBudget);
                            if (!ritual.Success)
                                continue;
                            goldBudget = Math.Max(0d, goldBudget - ritual.BloodMagicGoldPerSecond);
                        }
                        add = AddBloodRitualMagic;
                        break;
                    default:
                        continue;
                }
                if (needed <= 0)
                {
                    messages.Add($"{token} capped or complete");
                    continue;
                }
                yield return new KeyValuePair<long, Action<long>>(needed, amount =>
                {
                    add(index, amount);
                    if (kind != "NGU")
                    {
                        if (energy)
                            c.idleEnergy -= amount;
                        else
                            c.magic.idleMagic -= amount;
                    }
                    messages.Add($"{token} {amount:n0}{(amount < needed ? $" (below buffered cap: need {needed:n0}, resource shortage; later priorities wait)" : string.Empty)}");
                });
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

        private static long TimeMachineEnergyCapDelta(int id, int lookaheadSeconds)
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

            return CapDelta(cap * 1.000002d, c.machine.speedEnergy);
        }

        private static long TimeMachineMagicCapDelta(int id, int lookaheadSeconds)
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

            return CapDelta(cap * 1.000002d, c.machine.goldMultiMagic);
        }

        private static long WandoosEnergyCapDelta(int id)
        {
            var c = Main.Character;
            var speed = (double)c.totalWandoosEnergySpeed();
            return speed <= 0 ? 0 : CapDelta(c.wandoos98Controller.baseEnergyTime() / speed * 1.000002d, c.wandoos98.wandoosEnergy);
        }

        private static long WandoosMagicCapDelta(int id)
        {
            var c = Main.Character;
            var speed = (double)c.totalWandoosMagicSpeed();
            return speed <= 0 ? 0 : CapDelta(c.wandoos98Controller.baseMagicTime() / speed * 1.000002d, c.wandoos98.wandoosMagic);
        }

        private static long AugmentCapDelta(int id, int lookaheadSeconds)
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
                return CapDelta(formula * 1.000002d, c.augments.augs[augIndex].augEnergy);
            }

            if (aug.upgradeLocked() || aug.hitUpgradeTarget() || c.augments.augs[augIndex].upgradeProgress <= 0 && c.realGold < aug.getUpgradeCost())
                return 0;

            var upgradeLevel = ProjectedNextLevel(c.augments.augs[augIndex].upgradeLevel, c.augments.augs[augIndex].upgradeTarget, lookaheadSeconds);
            var upgradeFormula = AugmentCapFormula(augIndex, upgradeLevel, true);
            return CapDelta(upgradeFormula * 1.000002d, c.augments.augs[augIndex].upgradeEnergy);
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

        private static long BloodRitualCapDelta(int id)
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

            return CapDelta(divisor / (power * speedBonus) * 1.000002d, ritual.magic);
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

        private static long EnergyNguCapDelta(int id, int lookaheadSeconds)
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
            return CapDelta(formula * 1.000002d, c.NGU.skills[id].energy);
        }

        private static long MagicNguCapDelta(int id, int lookaheadSeconds)
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
            return CapDelta(formula * 1.000002d, c.NGU.magicSkills[id].magic);
        }

        private static long AdvancedTrainingCapDelta(int id, int lookaheadSeconds)
        {
            var c = Main.Character;
            if (id < 0 || id >= c.advancedTraining.energy.Length || AdvancedTrainingTargetReached(id))
                return 0;
            if (c.wishes.wishes[190].level >= 1)
                return 0;

            var power = (double)c.totalEnergyPower();
            var speed = (double)c.totalAdvancedTrainingSpeedBonus();
            if (power <= 0d || speed <= 0d)
                return 0;

            var projectedLevel = ProjectedNextLevel(c.advancedTraining.level[id], c.advancedTraining.levelTarget[id], lookaheadSeconds);
            var cap = 50d * AdvancedTrainingBaseTime(id) * projectedLevel * Math.Sqrt(power) / (speed * power);
            return CapDelta(cap * 1.000002d, c.advancedTraining.energy[id]);
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

        private static void AddTimeMachineEnergy(int id, long amount)
        {
            Main.Character.machine.speedEnergy = AddResource(Main.Character.machine.speedEnergy, amount);
            Main.Character.timeMachineController.updateSpeedText();
        }

        private static void AddTimeMachineMagic(int id, long amount)
        {
            Main.Character.machine.goldMultiMagic = AddResource(Main.Character.machine.goldMultiMagic, amount);
            Main.Character.timeMachineController.updateGoldMultiText();
        }

        private static void AddWandoosEnergy(int id, long amount)
        {
            Main.Character.wandoos98.wandoosEnergy = AddResource(Main.Character.wandoos98.wandoosEnergy, amount);
            Main.Character.wandoos98Controller.updateText();
        }

        private static void AddWandoosMagic(int id, long amount)
        {
            Main.Character.wandoos98.wandoosMagic = AddResource(Main.Character.wandoos98.wandoosMagic, amount);
            Main.Character.wandoos98Controller.updateText();
        }

        private static void AddAugmentEnergy(int id, long amount)
        {
            var augIndex = id / 2;
            if (id % 2 == 0)
            {
                Main.Character.augments.augs[augIndex].augEnergy = AddResource(Main.Character.augments.augs[augIndex].augEnergy, amount);
                Main.Character.augmentsController.augments[augIndex].updateAugTexts();
                return;
            }

            Main.Character.augments.augs[augIndex].upgradeEnergy = AddResource(Main.Character.augments.augs[augIndex].upgradeEnergy, amount);
            Main.Character.augmentsController.augments[augIndex].updateUpgradeTexts();
        }

        private static void AddBloodRitualMagic(int id, long amount)
        {
            Main.Character.bloodMagic.ritual[id].magic = AddResource(Main.Character.bloodMagic.ritual[id].magic, amount);
            Main.Character.bloodMagicController.bloodMagics[id].updateBloodMagicText();
        }

        private static void AddEnergyNgu(int id, long amount)
        {
            Main.Character.NGU.skills[id].energy = AddResource(Main.Character.NGU.skills[id].energy, amount);
            // Commit both sides of the transfer before a controller/UI refresh can throw.
            Main.Character.idleEnergy -= amount;
            Main.Character.NGUController.NGU[id].updateText();
        }

        private static void AddMagicNgu(int id, long amount)
        {
            Main.Character.NGU.magicSkills[id].magic = AddResource(Main.Character.NGU.magicSkills[id].magic, amount);
            Main.Character.magic.idleMagic -= amount;
            Main.Character.NGUController.NGUMagic[id].updateText();
        }

        private static void AddAdvancedTrainingEnergy(int id, long amount)
        {
            Main.Character.advancedTraining.energy[id] = AddResource(Main.Character.advancedTraining.energy[id], amount);
            AdvancedTrainingController(id).updateText();
        }

        private static AdvancedTrainingController AdvancedTrainingController(int id)
        {
            var c = Main.Character;
            switch (id)
            {
                case 0:
                    return c.advancedTrainingController.defense;
                case 1:
                    return c.advancedTrainingController.attack;
                case 2:
                    return c.advancedTrainingController.block;
                case 3:
                    return c.advancedTrainingController.wandoosEnergy;
                default:
                    return c.advancedTrainingController.wandoosMagic;
            }
        }
    }
}
