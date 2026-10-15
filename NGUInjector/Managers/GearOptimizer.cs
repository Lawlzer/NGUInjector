using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NGUInjector.Managers
{
    internal enum GearOptimizationGoal
    {
        EnergyCap = 0,
        MagicCap = 1,
        EnergyNgu = 2,
        MagicNgu = 3,
        AllNgu = 4,
        TimeMachine = 5,
        Blood = 6,
        Gold = 7,
        Drop = 8,
        Fight = 9,
        Augments = 10,
        AdvancedTraining = 11,
        Wandoos = 12,
        ManualAdventure = 13
    }

    internal enum FightScoreMode
    {
        SqrtPowerToughness = 0,
        PowerTimesToughness = 1,
        PowerPlusToughness = 2
    }

    internal class GearOptimizerResult
    {
        internal bool Success { get; set; }
        internal string Message { get; set; }
        internal int[] ItemIds { get; set; }
        internal ih[] Items { get; set; }
        internal double Score { get; set; }
        internal long EvaluatedCombinations { get; set; }
        internal long VisitedNodes { get; set; }
        internal long PrunedBranches { get; set; }
        internal long ElapsedMilliseconds { get; set; }
    }

    internal static class GearOptimizer
    {
        private static readonly Dictionary<string, GearOptimizerResult> CachedResults = new Dictionary<string, GearOptimizerResult>();
        private static Dictionary<Equipment, GearCandidate> CachedCandidates;
        private static int CachedAccessorySlots;
        private static bool CachedWeapon2Unlocked;
        private static float CachedWeapon2Factor;

        private enum StatKey
        {
            Power,
            Toughness,
            EnergyCap,
            EnergyPower,
            EnergyBars,
            MagicCap,
            MagicPower,
            MagicBars,
            Res3Cap,
            Res3Power,
            NguSpeed,
            Gold,
            Drop,
            BeardSpeed,
            WandoosSpeed,
            AugmentSpeed,
            AdvancedTrainingSpeed,
            MoveCooldown,
            WishSpeed,
            HackSpeed
        }

        private class GearCandidate
        {
            internal ih Item { get; set; }
            internal int Id => Item.id;
            internal part Slot => Item.equipment.type;
            internal Dictionary<StatKey, double> Stats { get; } = new Dictionary<StatKey, double>();
        }

        private class SelectedGear
        {
            internal GearCandidate Candidate { get; set; }
            internal double Factor { get; set; } = 1.0;
        }

        private class SearchState
        {
            internal GearOptimizationGoal Goal { get; set; }
            internal FightScoreMode FightScoreMode { get; set; }
            internal StatKey[] RelevantStats { get; set; }
            internal List<SelectedGear> BestItems { get; set; } = new List<SelectedGear>();
            internal double BestScore { get; set; } = double.NegativeInfinity;
            internal long EvaluatedCombinations { get; set; }
            internal SearchOption[][] MainOptions;
            internal SearchOption[][] AccessoryOptions;
            internal double[][] MainBounds;
            internal double[,,] AccessoryBounds;
            internal double[][] Totals;
            internal double[] BoundTotals;
            internal SelectedGear[] Current;
            internal int AccessoryPickCount;
            internal double[] LinearWeights;
            internal double[] LinearMainBounds;
            internal double[,] LinearAccessoryBounds;
            internal double LinearAnchorScore;
            internal double LinearExponent;
            internal long VisitedNodes;
            internal long PrunedBranches;
            internal long DominancePrunedBranches;
            internal long SymmetryPrunedBranches;
            // Every complete greedy trial/local-swap score, including repeats; partial marginal probes do not count.
            internal long HeuristicEvaluations;
            internal double SeedScore;
            internal int[] ChosenAccessoryOptions;
            internal int[] PreviousEquivalentAccessory;
        }

        private class SearchOption
        {
            internal SelectedGear[] Items;
            internal double[] Stats;
            internal int[] RequiredAccessoryGroups;
        }

        internal static readonly Dictionary<int, string> GoalLabels = new Dictionary<int, string>
        {
            {(int) GearOptimizationGoal.EnergyCap, "Energy Cap"},
            {(int) GearOptimizationGoal.MagicCap, "Magic Cap"},
            {(int) GearOptimizationGoal.EnergyNgu, "Energy NGU"},
            {(int) GearOptimizationGoal.MagicNgu, "Magic NGU"},
            {(int) GearOptimizationGoal.AllNgu, "All NGUs"},
            {(int) GearOptimizationGoal.TimeMachine, "Time Machine"},
            {(int) GearOptimizationGoal.Blood, "Blood Magic"},
            {(int) GearOptimizationGoal.Gold, "Gold"},
            {(int) GearOptimizationGoal.Drop, "Drop Chance"},
            {(int) GearOptimizationGoal.Fight, "Fight"},
            {(int) GearOptimizationGoal.Augments, "Augments"},
            {(int) GearOptimizationGoal.AdvancedTraining, "Advanced Training"},
            {(int) GearOptimizationGoal.Wandoos, "Wandoos"},
            {(int) GearOptimizationGoal.ManualAdventure, "Manual Adventure (Power * Toughness * Move Cooldown)"}
        };

        internal static readonly Dictionary<int, string> FightScoreModeLabels = new Dictionary<int, string>
        {
            {(int) FightScoreMode.SqrtPowerToughness, "sqrt(Power * Toughness) * Move Cooldown"},
            {(int) FightScoreMode.PowerTimesToughness, "Power * Toughness * Move Cooldown"},
            {(int) FightScoreMode.PowerPlusToughness, "(Power + Toughness) * Move Cooldown"}
        };

        internal static GearOptimizerResult OptimizeAndEquip(GearOptimizationGoal goal, FightScoreMode fightScoreMode = FightScoreMode.SqrtPowerToughness)
        {
            return OptimizeAndEquip(goal, fightScoreMode, false);
        }

        internal static GearOptimizerResult OptimizeAndEquip(GearOptimizationGoal goal, FightScoreMode fightScoreMode, bool ignoreLoadoutLock)
        {
            return OptimizeAndEquip(goal, fightScoreMode, ignoreLoadoutLock, false);
        }

        internal static GearOptimizerResult OptimizeAndEquip(GearOptimizationGoal goal, FightScoreMode fightScoreMode, bool ignoreLoadoutLock, bool resourcesAlreadyRemoved)
        {
            var result = GetCachedOrOptimize(goal, fightScoreMode);
            if (!result.Success)
                return result;

            if (!ignoreLoadoutLock && !LoadoutManager.CanSwap())
            {
                result.Success = false;
                result.Message = "Cannot equip optimized gear while another loadout swap is locked.";
                return result;
            }

            var failure = EquipGear(result.Items, resourcesAlreadyRemoved);
            if (failure != null)
            {
                result.Success = false;
                result.Message = failure;
                return result;
            }
            result.Message = $"Equipped {GoalDescription(goal, fightScoreMode)} gear: {string.Join(",", result.ItemIds.Select(x => x.ToString()).ToArray())}";
            return result;
        }

        internal static GearOptimizerResult Optimize(GearOptimizationGoal goal, FightScoreMode fightScoreMode = FightScoreMode.SqrtPowerToughness)
        {
            var timer = Stopwatch.StartNew();
            var candidates = BuildCandidates();
            var accessorySlots = Math.Max(0, Math.Min(Main.Controller.accessorySpaces(), Main.Character.inventory.accs.Count));
            var weapon2Factor = Main.Controller.weapon2Unlocked() ? Main.Controller.weapon2Factor() : 0f;
            var result = OptimizeCandidates(goal, fightScoreMode, candidates, accessorySlots, weapon2Factor);
            result.ElapsedMilliseconds = timer.ElapsedMilliseconds;
            result.Message += $" Took {result.ElapsedMilliseconds:n0} ms.";
            return result;
        }

        private static GearOptimizerResult OptimizeCandidates(GearOptimizationGoal goal, FightScoreMode fightScoreMode,
            List<GearCandidate> candidates, int accessorySlots, float weapon2Factor)
        {
            if (candidates.Count == 0)
            {
                return Fail("No owned gear candidates found.");
            }

            var relevantStats = GetRelevantStats(goal);
            if (relevantStats.Length == 1)
                return OptimizeSingleStatGoal(goal, fightScoreMode, candidates, relevantStats, relevantStats[0], accessorySlots, weapon2Factor);

            var heads = FilterSlot(candidates, part.Head, relevantStats);
            var chests = FilterSlot(candidates, part.Chest, relevantStats);
            var legs = FilterSlot(candidates, part.Legs, relevantStats);
            var boots = FilterSlot(candidates, part.Boots, relevantStats);

            var weaponSlots = weapon2Factor > 0 ? 2 : 1;
            var weapons = FilterSlot(candidates, part.Weapon, relevantStats, weaponSlots);
            var accessories = FilterSlot(candidates, part.Accessory, relevantStats, Math.Max(1, accessorySlots))
                .OrderByDescending(x => ScoreItems(goal, fightScoreMode, relevantStats, new[] {new SelectedGear {Candidate = x}}))
                .ToList();

            if (heads.Count == 0 || chests.Count == 0 || legs.Count == 0 || boots.Count == 0 || weapons.Count == 0)
            {
                return Fail("Missing at least one required gear slot for optimization.");
            }

            var accessoryPickCount = Math.Min(accessorySlots, accessories.Select(x => x.Id).Distinct().Count());

            var weaponSets = BuildWeaponSets(weapons, weapon2Factor);
            var mainGroups = new List<List<GearCandidate>> {heads, chests, legs, boots};
            var state = new SearchState {Goal = goal, FightScoreMode = fightScoreMode, RelevantStats = relevantStats};

            SearchGear(mainGroups, weaponSets, accessories, accessoryPickCount, state);

            if (state.BestItems.Count == 0)
            {
                return Fail("No valid gear combination found.");
            }

            var items = state.BestItems.Select(x => x.Candidate.Item).ToArray();
            var ids = items.Select(x => x.id).ToArray();
            return new GearOptimizerResult
            {
                Success = true,
                ItemIds = ids,
                Items = items,
                Score = state.BestScore,
                EvaluatedCombinations = state.EvaluatedCombinations,
                VisitedNodes = state.VisitedNodes,
                PrunedBranches = state.PrunedBranches,
                Message = $"Optimized {GoalDescription(goal, fightScoreMode)} gear exactly. Score {state.BestScore:0.###}; " +
                    $"{accessories.Count} accessory candidates for {accessoryPickCount} slots; scored {state.EvaluatedCombinations:n0} complete loadouts " +
                    $"({state.HeuristicEvaluations:n0} seed/swap trials), visited {state.VisitedNodes:n0} nodes, pruned {state.PrunedBranches:n0} branches."
            };
        }

        private static GearOptimizerResult OptimizeSingleStatGoal(
            GearOptimizationGoal goal,
            FightScoreMode fightScoreMode,
            List<GearCandidate> candidates,
            StatKey[] relevantStats,
            StatKey stat,
            int accessorySlots,
            float weapon2Factor)
        {
            var selected = new List<SelectedGear>();
            var mainSlots = new[] {part.Head, part.Chest, part.Legs, part.Boots};
            foreach (var slot in mainSlots)
            {
                var item = candidates
                    .Where(x => x.Slot == slot)
                    .OrderByDescending(x => Get(x, stat))
                    .FirstOrDefault();
                if (item == null)
                    return Fail("Missing at least one required gear slot for optimization.");

                selected.Add(new SelectedGear {Candidate = item});
            }

            var weapons = candidates.Where(x => x.Slot == part.Weapon).ToList();
            var weaponSet = BestSingleStatWeaponSet(weapons, stat, weapon2Factor);
            if (weaponSet.Count == 0)
                return Fail("Missing at least one required gear slot for optimization.");
            selected.AddRange(weaponSet);

            if (accessorySlots > 0)
            {
                selected.AddRange(candidates
                    .Where(x => x.Slot == part.Accessory)
                    .GroupBy(x => x.Id)
                    .Select(x => x.OrderByDescending(y => Get(y, stat)).First())
                    .OrderByDescending(x => Get(x, stat))
                    .Take(accessorySlots)
                    .Select(x => new SelectedGear {Candidate = x}));
            }

            var score = ScoreItems(goal, fightScoreMode, relevantStats, selected);
            var items = selected.Select(x => x.Candidate.Item).ToArray();
            var ids = items.Select(x => x.id).ToArray();
            return new GearOptimizerResult
            {
                Success = true,
                ItemIds = ids,
                Items = items,
                Score = score,
                EvaluatedCombinations = 1,
                Message = $"Optimized {GoalDescription(goal, fightScoreMode)} gear. Score {score:0.###}, checked 1 single-stat pass."
            };
        }

        private static List<SelectedGear> BestSingleStatWeaponSet(List<GearCandidate> weapons, StatKey stat, float weapon2Factor)
        {
            if (weapons == null || weapons.Count == 0)
                return new List<SelectedGear>();

            weapons = weapons.GroupBy(x => x.Id).Select(x => x.OrderByDescending(y => Get(y, stat)).First())
                .OrderByDescending(x => Get(x, stat)).ToList();
            if (weapon2Factor <= 0 || weapons.Count == 1)
            {
                var best = weapons[0];
                return new List<SelectedGear> {new SelectedGear {Candidate = best}};
            }

            // The game's offhand factor is at most one, so the strongest item belongs in mainhand.
            return new List<SelectedGear>
            {
                new SelectedGear {Candidate = weapons[0]},
                new SelectedGear {Candidate = weapons[1], Factor = weapon2Factor}
            };
        }

        internal static string PreCache(IEnumerable<GearOptimizationGoal> goals, FightScoreMode fightScoreMode = FightScoreMode.SqrtPowerToughness)
        {
            var uniqueGoals = goals == null
                ? new GearOptimizationGoal[0]
                : goals.Distinct().Where(x => GoalLabels.ContainsKey((int) x)).ToArray();
            if (uniqueGoals.Length == 0)
                return "No equipment loadouts to pre-cache.";

            var success = 0;
            var failures = new List<string>();
            EnsureCacheFresh();
            foreach (var goal in uniqueGoals)
            {
                var result = Optimize(goal, fightScoreMode);
                if (result.Success)
                {
                    CachedResults[CacheKey(goal, fightScoreMode)] = CloneResult(result);
                    success++;
                }
                else
                {
                    failures.Add($"{GoalDescription(goal, fightScoreMode)}: {result.Message}");
                }
            }

            var message = $"Pre-cached {success}/{uniqueGoals.Length} generated equipment loadouts.";
            if (failures.Count > 0)
            {
                message += " Failed: " + string.Join("; ", failures.ToArray());
            }

            Main.Log(message);
            return message;
        }

        internal static void ClearCache()
        {
            CachedResults.Clear();
            CachedCandidates = null;
        }

        internal static string FightScoreModeLabel(FightScoreMode mode)
        {
            return FightScoreModeLabels.ContainsKey((int) mode) ? FightScoreModeLabels[(int) mode] : FightScoreModeLabels[0];
        }

        private static string GoalDescription(GearOptimizationGoal goal, FightScoreMode fightScoreMode)
        {
            var label = GoalLabels.ContainsKey((int) goal) ? GoalLabels[(int) goal] : goal.ToString();
            return goal == GearOptimizationGoal.Fight ? $"{label} ({FightScoreModeLabel(fightScoreMode)})" : label;
        }

        private static GearOptimizerResult Fail(string message)
        {
            return new GearOptimizerResult {Success = false, Message = message, ItemIds = new int[0], Items = new ih[0]};
        }

        private static GearOptimizerResult GetCachedOrOptimize(GearOptimizationGoal goal, FightScoreMode fightScoreMode)
        {
            EnsureCacheFresh();
            var key = CacheKey(goal, fightScoreMode);
            if (CachedResults.TryGetValue(key, out var cached) && cached.Success)
            {
                var result = CloneResult(cached);
                result.Message = $"Using pre-cached {GoalDescription(goal, fightScoreMode)} gear.";
                return result;
            }

            var optimized = Optimize(goal, fightScoreMode);
            if (optimized.Success)
            {
                CachedResults[key] = CloneResult(optimized);
            }

            return optimized;
        }

        private static void EnsureCacheFresh()
        {
            var candidates = BuildCandidates();
            var accessorySlots = Math.Min(Main.Controller.accessorySpaces(), Main.Character.inventory.accs.Count);
            var weapon2Unlocked = Main.Controller.weapon2Unlocked();
            var weapon2Factor = weapon2Unlocked ? Main.Controller.weapon2Factor() : 0f;
            if (CacheMatches(candidates, accessorySlots, weapon2Unlocked, weapon2Factor))
                return;

            CachedResults.Clear();
            CachedCandidates = candidates.ToDictionary(x => x.Item.equipment);
            CachedAccessorySlots = accessorySlots;
            CachedWeapon2Unlocked = weapon2Unlocked;
            CachedWeapon2Factor = weapon2Factor;
        }

        private static bool CacheMatches(List<GearCandidate> candidates, int accessorySlots, bool weapon2Unlocked, float weapon2Factor)
        {
            if (CachedCandidates == null || CachedCandidates.Count != candidates.Count ||
                CachedAccessorySlots != accessorySlots || CachedWeapon2Unlocked != weapon2Unlocked || CachedWeapon2Factor != weapon2Factor)
                return false;

            foreach (var candidate in candidates)
            {
                if (!CachedCandidates.TryGetValue(candidate.Item.equipment, out var previous) || previous.Id != candidate.Id ||
                    previous.Stats.Count != candidate.Stats.Count ||
                    candidate.Stats.Any(x => !previous.Stats.ContainsKey(x.Key) || previous.Stats[x.Key] != x.Value))
                    return false;
            }

            return true;
        }

        private static string CacheKey(GearOptimizationGoal goal, FightScoreMode fightScoreMode)
        {
            return $"{(int) goal}:{(int) fightScoreMode}";
        }

        private static GearOptimizerResult CloneResult(GearOptimizerResult source)
        {
            return new GearOptimizerResult
            {
                Success = source.Success,
                Message = source.Message,
                ItemIds = source.ItemIds == null ? new int[0] : source.ItemIds.ToArray(),
                Items = source.Items == null ? new ih[0] : source.Items.ToArray(),
                Score = source.Score,
                EvaluatedCombinations = source.EvaluatedCombinations,
                VisitedNodes = source.VisitedNodes,
                PrunedBranches = source.PrunedBranches,
                ElapsedMilliseconds = source.ElapsedMilliseconds
            };
        }

        private static List<GearCandidate> BuildCandidates()
        {
            return Main.Character.inventory.GetConvertedEquips()
                .Concat(Main.Character.inventory.GetConvertedInventory())
                .Where(x => x.id >= 40 && x.equipment != null && x.equipment.isEquipment())
                .Select(BuildCandidate)
                .ToList();
        }

        private static GearCandidate BuildCandidate(ih item)
        {
            var candidate = new GearCandidate {Item = item};
            var equip = item.equipment;
            Add(candidate, StatKey.Power, Main.Controller.equipAttackBonus(equip));
            Add(candidate, StatKey.Toughness, Main.Controller.equipDefenseBonus(equip));
            foreach (var type in new[] {equip.spec1Type, equip.spec2Type, equip.spec3Type}.Distinct())
                AddSpec(candidate, type, Main.Controller.equipSpecBonus(type, equip));
            return candidate;
        }

        private static void AddSpec(GearCandidate candidate, specType type, float value)
        {
            if (type == specType.None || value == 0)
                return;

            // Boost points have different scales per spec tier. Store actual percentage bonuses.
            value = Main.Controller.getBonusFactor(value, type) * 100f;
            switch (type)
            {
                case specType.EnergyPower:
                case specType.EnergyPower2:
                case specType.EnergyPower3:
                    Add(candidate, StatKey.EnergyPower, value);
                    break;
                case specType.MagicPower:
                case specType.MagicPower2:
                case specType.MagicPower3:
                    Add(candidate, StatKey.MagicPower, value);
                    break;
                case specType.AllPower:
                    Add(candidate, StatKey.EnergyPower, value);
                    Add(candidate, StatKey.MagicPower, value);
                    break;
                case specType.EnergyCap:
                case specType.EnergyCap3:
                    Add(candidate, StatKey.EnergyCap, value);
                    break;
                case specType.MagicCap:
                case specType.MagicCap3:
                    Add(candidate, StatKey.MagicCap, value);
                    break;
                case specType.AllCap:
                    Add(candidate, StatKey.EnergyCap, value);
                    Add(candidate, StatKey.MagicCap, value);
                    break;
                case specType.EnergyPerBar:
                case specType.EnergyPerBar2:
                case specType.EnergyPerBar3:
                    Add(candidate, StatKey.EnergyBars, value);
                    break;
                case specType.MagicPerBar:
                case specType.MagicPerBar2:
                case specType.MagicPerBar3:
                    Add(candidate, StatKey.MagicBars, value);
                    break;
                case specType.AllPerBar:
                    Add(candidate, StatKey.EnergyBars, value);
                    Add(candidate, StatKey.MagicBars, value);
                    break;
                case specType.NGU:
                case specType.NGU2:
                    Add(candidate, StatKey.NguSpeed, value);
                    break;
                case specType.GoldDropAmount:
                case specType.GoldDrop2:
                    Add(candidate, StatKey.Gold, value);
                    break;
                case specType.Looting:
                case specType.Looting2:
                    Add(candidate, StatKey.Drop, value);
                    break;
                case specType.Beards:
                case specType.Beards2:
                    Add(candidate, StatKey.BeardSpeed, value);
                    break;
                case specType.Wandoos98:
                case specType.Wandoos2:
                    Add(candidate, StatKey.WandoosSpeed, value);
                    break;
                case specType.Augs:
                    Add(candidate, StatKey.AugmentSpeed, value);
                    break;
                case specType.AdvTraining:
                case specType.AdvTraining2:
                    Add(candidate, StatKey.AdvancedTrainingSpeed, value);
                    break;
                case specType.Cooldown:
                    Add(candidate, StatKey.MoveCooldown, value);
                    break;
                case specType.Res3Cap:
                    Add(candidate, StatKey.Res3Cap, value);
                    break;
                case specType.Res3Power:
                    Add(candidate, StatKey.Res3Power, value);
                    break;
                case specType.HackSpeed:
                    Add(candidate, StatKey.HackSpeed, value);
                    break;
                case specType.WishSpeed:
                    Add(candidate, StatKey.WishSpeed, value);
                    break;
            }
        }

        private static void Add(GearCandidate candidate, StatKey stat, double value)
        {
            if (value == 0)
                return;

            if (!candidate.Stats.ContainsKey(stat))
            {
                candidate.Stats[stat] = 0;
            }

            candidate.Stats[stat] += value;
        }

        private static List<GearCandidate> FilterSlot(IEnumerable<GearCandidate> candidates, part slot, StatKey[] relevantStats, int dominanceCutoff = 1)
        {
            var slotCandidates = candidates.Where(x => x.Slot == slot).ToList();
            return RemoveDominated(slotCandidates, relevantStats, dominanceCutoff);
        }

        private static List<GearCandidate> RemoveDominated(List<GearCandidate> candidates, StatKey[] relevantStats, int dominanceCutoff)
        {
            var result = new List<GearCandidate>();
            for (var i = 0; i < candidates.Count; i++)
            {
                var dominators = new HashSet<int>();
                var sameIdDominated = false;
                for (var j = 0; j < candidates.Count; j++)
                {
                    if (i == j)
                        continue;

                    var dominates = Dominates(candidates[j], candidates[i], relevantStats);
                    if (candidates[j].Id == candidates[i].Id)
                    {
                        // Copies compete for one ID, not for separate accessory/weapon slots.
                        if (dominates || (j < i && relevantStats.All(stat => Get(candidates[j], stat) == Get(candidates[i], stat))))
                        {
                            sameIdDominated = true;
                            break;
                        }
                    }
                    else if (dominates)
                    {
                        dominators.Add(candidates[j].Id);
                        if (dominators.Count >= dominanceCutoff)
                            break;
                    }
                }

                if (!sameIdDominated && dominators.Count < dominanceCutoff)
                {
                    result.Add(candidates[i]);
                }
            }

            return result;
        }

        private static bool Dominates(GearCandidate a, GearCandidate b, StatKey[] relevantStats)
        {
            var better = false;
            foreach (var stat in relevantStats)
            {
                var av = Get(a, stat);
                var bv = Get(b, stat);
                if (av < bv)
                    return false;

                if (av > bv)
                    better = true;
            }

            return better;
        }

        private static List<List<SelectedGear>> BuildWeaponSets(List<GearCandidate> weapons, float weapon2Factor)
        {
            var sets = new List<List<SelectedGear>>();
            if (weapon2Factor <= 0 || weapons.Select(x => x.Id).Distinct().Count() == 1)
            {
                sets.AddRange(weapons.Select(x => new List<SelectedGear> {new SelectedGear {Candidate = x}}));
                return sets;
            }

            foreach (var mainhand in weapons)
            {
                foreach (var offhand in weapons)
                {
                    if (mainhand.Id == offhand.Id)
                        continue;

                    sets.Add(new List<SelectedGear>
                    {
                        new SelectedGear {Candidate = mainhand},
                        new SelectedGear {Candidate = offhand, Factor = weapon2Factor}
                    });
                }
            }

            return sets;
        }

        private static void SearchGear(List<List<GearCandidate>> mainGroups, List<List<SelectedGear>> weaponSets,
            List<GearCandidate> accessories, int accessoryPickCount, SearchState state)
        {
            state.AccessoryPickCount = Math.Max(0, Math.Min(accessoryPickCount, accessories.Select(x => x.Id).Distinct().Count()));
            var groups = new List<SearchOption[]> {weaponSets.Select(x => MakeSearchOption(x.ToArray(), state)).ToArray()};
            groups.AddRange(mainGroups.Select(group => group.Select(x => MakeSearchOption(new[] {new SelectedGear {Candidate = x}}, state)).ToArray()));
            state.MainOptions = groups.ToArray();
            state.AccessoryOptions = accessories.GroupBy(x => x.Id)
                .Select(group => group.Select(x => MakeSearchOption(new[] {new SelectedGear {Candidate = x}}, state)).ToArray()).ToArray();
            if (state.MainOptions.Any(group => group.Length == 0))
                return;
            PrepareAccessoryImplications(state);

            // Each component may choose a different item/copy. This is deliberately optimistic,
            // but each top-k sum uses distinct IDs, including when an ID has several tradeoff copies.
            var statCount = Enum.GetValues(typeof(StatKey)).Length;
            state.AccessoryBounds = new double[state.AccessoryOptions.Length + 1, state.AccessoryPickCount + 1, statCount];
            for (var i = state.AccessoryOptions.Length - 1; i >= 0; i--)
            {
                foreach (var stat in state.RelevantStats)
                {
                    var s = (int) stat;
                    var maximum = state.AccessoryOptions[i].Max(x => x.Stats[s]);
                    for (var k = 1; k <= state.AccessoryPickCount; k++)
                        state.AccessoryBounds[i, k, s] = Math.Max(state.AccessoryBounds[i + 1, k, s],
                            maximum + state.AccessoryBounds[i + 1, k - 1, s]);
                }
            }

            state.MainBounds = new double[groups.Count + 1][];
            state.MainBounds[groups.Count] = new double[statCount];
            for (var i = groups.Count - 1; i >= 0; i--)
            {
                state.MainBounds[i] = new double[statCount];
                foreach (var stat in state.RelevantStats)
                    state.MainBounds[i][(int) stat] = state.MainBounds[i + 1][(int) stat] +
                        (groups[i].Length == 0 ? 0 : groups[i].Max(x => x.Stats[(int) stat]));
            }

            state.Totals = new double[groups.Count + state.AccessoryPickCount + 1][];
            for (var i = 0; i < state.Totals.Length; i++)
                state.Totals[i] = new double[statCount];
            foreach (var stat in state.RelevantStats)
                state.Totals[0][(int) stat] = stat == StatKey.Power || stat == StatKey.Toughness ? 0 : 100;
            state.BoundTotals = new double[statCount];
            state.LinearWeights = new double[statCount];
            state.LinearMainBounds = new double[groups.Count + 1];
            state.LinearAccessoryBounds = new double[state.AccessoryOptions.Length + 1, state.AccessoryPickCount + 1];
            state.Current = new SelectedGear[mainGroups.Count + 2 + state.AccessoryPickCount];
            SeedSearch(state);
            SearchMainSlots(0, 0, state);
        }

        private static void PrepareAccessoryImplications(SearchState state)
        {
            var groups = state.AccessoryOptions;
            for (var i = 0; i < groups.Length; i++)
            {
                Array.Sort(groups[i], (a, b) =>
                {
                    foreach (var stat in state.RelevantStats)
                    {
                        var comparison = a.Stats[(int) stat].CompareTo(b.Stats[(int) stat]);
                        if (comparison != 0)
                            return comparison;
                    }
                    return 0;
                });
                groups[i] = groups[i].Where((option, index) => index == 0 ||
                    state.RelevantStats.Any(stat => option.Stats[(int) stat] != groups[i][index - 1].Stats[(int) stat])).ToArray();
            }

            var predecessorCounts = new int[groups.Length];
            for (var b = 0; b < groups.Length; b++)
            {
                var predecessors = new HashSet<int>();
                foreach (var option in groups[b])
                {
                    var required = new List<int>();
                    for (var a = 0; a < groups.Length; a++)
                    {
                        // Require an ID, not a particular copy: any A copy must safely replace B.
                        if (a == b || !groups[a].All(copy =>
                            state.RelevantStats.All(stat => copy.Stats[(int) stat] >= option.Stats[(int) stat]) &&
                            state.RelevantStats.Any(stat => copy.Stats[(int) stat] > option.Stats[(int) stat])))
                            continue;
                        required.Add(a);
                        predecessors.Add(a);
                    }
                    option.RequiredAccessoryGroups = required.ToArray();
                }
                predecessorCounts[b] = predecessors.Count;
            }

            // A -> B means every A copy strictly dominates some B copy. This relation is
            // transitive and acyclic, so predecessor count is a topological ordering.
            var order = Enumerable.Range(0, groups.Length).OrderBy(i => predecessorCounts[i]).ToArray();
            var positions = new int[groups.Length];
            for (var i = 0; i < order.Length; i++)
                positions[order[i]] = i;
            state.AccessoryOptions = order.Select(i => groups[i]).ToArray();
            state.ChosenAccessoryOptions = Enumerable.Repeat(-1, groups.Length).ToArray();
            state.PreviousEquivalentAccessory = Enumerable.Repeat(-1, groups.Length).ToArray();
            for (var i = 0; i < groups.Length; i++)
            {
                var options = state.AccessoryOptions[i];
                foreach (var option in options)
                    option.RequiredAccessoryGroups = option.RequiredAccessoryGroups.Select(a => positions[a]).ToArray();
                for (var j = i - 1; j >= 0; j--)
                {
                    var previous = state.AccessoryOptions[j];
                    if (previous.Length == options.Length && options.Select((option, index) =>
                        state.RelevantStats.All(stat => option.Stats[(int) stat] == previous[index].Stats[(int) stat])).All(equal => equal))
                    {
                        state.PreviousEquivalentAccessory[i] = j;
                        break;
                    }
                }
            }
        }

        private static void SeedSearch(SearchState state)
        {
            var seed = new SearchOption[state.MainOptions.Length + state.AccessoryPickCount];
            var totals = (double[]) state.Totals[0].Clone();
            var trial = new double[totals.Length];
            var usedIds = new HashSet<int>();
            var accessoryAlternatives = state.AccessoryOptions.SelectMany(group => group).ToArray();
            for (var i = 0; i < seed.Length; i++)
            {
                var options = i < state.MainOptions.Length ? state.MainOptions[i] : accessoryAlternatives;
                var best = double.NegativeInfinity;
                foreach (var option in options)
                {
                    if (i >= state.MainOptions.Length && usedIds.Contains(option.Items[0].Candidate.Id))
                        continue;
                    foreach (var stat in state.RelevantStats)
                        trial[(int) stat] = totals[(int) stat] + option.Stats[(int) stat];
                    var score = ScoreTotals(state.Goal, state.FightScoreMode, trial);
                    if (i == seed.Length - 1)
                    {
                        state.EvaluatedCombinations++;
                        state.HeuristicEvaluations++;
                    }
                    if (seed[i] != null && score <= best)
                        continue;
                    best = score;
                    seed[i] = option;
                }
                foreach (var stat in state.RelevantStats)
                    totals[(int) stat] += seed[i].Stats[(int) stat];
                if (i >= state.MainOptions.Length)
                    usedIds.Add(seed[i].Items[0].Candidate.Id);
                if (i == seed.Length - 1)
                    state.BestScore = best;
            }

            // Coordinate ascent over full feasible loadouts, including weapon-set/main choices,
            // alternative copies of an accessory ID, and replacements by currently unused IDs.
            bool improved;
            do
            {
                improved = false;
                for (var i = 0; i < seed.Length; i++)
                {
                    var options = i < state.MainOptions.Length ? state.MainOptions[i] : accessoryAlternatives;
                    if (i >= state.MainOptions.Length)
                        usedIds.Remove(seed[i].Items[0].Candidate.Id);
                    foreach (var option in options)
                    {
                        if (option == seed[i] || (i >= state.MainOptions.Length && usedIds.Contains(option.Items[0].Candidate.Id)))
                            continue;
                        Array.Copy(state.Totals[0], trial, trial.Length);
                        for (var j = 0; j < seed.Length; j++)
                            foreach (var stat in state.RelevantStats)
                                trial[(int) stat] += (j == i ? option : seed[j]).Stats[(int) stat];
                        var score = ScoreTotals(state.Goal, state.FightScoreMode, trial);
                        state.EvaluatedCombinations++;
                        state.HeuristicEvaluations++;
                        if (score <= state.BestScore)
                            continue;
                        seed[i] = option;
                        state.BestScore = score;
                        improved = true;
                    }
                    if (i >= state.MainOptions.Length)
                        usedIds.Add(seed[i].Items[0].Candidate.Id);
                }
            } while (improved);

            Array.Copy(state.Totals[0], totals, totals.Length);
            foreach (var option in seed)
                foreach (var stat in state.RelevantStats)
                    totals[(int) stat] += option.Stats[(int) stat];
            state.BestItems = seed.SelectMany(option => option.Items).ToList();
            state.SeedScore = state.BestScore;
            UpdateLinearBounds(totals, state);
        }

        private static SearchOption MakeSearchOption(SelectedGear[] items, SearchState state)
        {
            var option = new SearchOption {Items = items, Stats = new double[Enum.GetValues(typeof(StatKey)).Length]};
            foreach (var item in items)
                foreach (var stat in state.RelevantStats)
                    option.Stats[(int) stat] += Get(item.Candidate, stat) * item.Factor;
            return option;
        }

        private static bool CanImprove(SearchState state, int depth, int mainIndex, int accessoryStart, int remaining)
        {
            foreach (var stat in state.RelevantStats)
            {
                var s = (int) stat;
                state.BoundTotals[s] = state.Totals[depth][s] + state.MainBounds[mainIndex][s] +
                    state.AccessoryBounds[accessoryStart, remaining, s];
            }
            var bound = ScoreTotals(state.Goal, state.FightScoreMode, state.BoundTotals);
            // Only prune comfortably below the incumbent, never on an approximate equality.
            var cutoff = state.BestScore - Math.Max(1, Math.Abs(state.BestScore)) * 1e-12;
            if (bound < cutoff)
            {
                state.PrunedBranches++;
                return false;
            }
            if (state.LinearAnchorScore > 0)
            {
                var linear = WeightedStats(state.Totals[depth], state) + state.LinearMainBounds[mainIndex] +
                    state.LinearAccessoryBounds[accessoryStart, remaining];
                bound = state.LinearAnchorScore * Math.Pow(linear / state.LinearExponent, state.LinearExponent);
                if (bound < cutoff)
                {
                    state.PrunedBranches++;
                    return false;
                }
            }
            return true;
        }

        private static double WeightedStats(double[] stats, SearchState state)
        {
            var total = 0d;
            foreach (var stat in state.RelevantStats)
                total += stats[(int) stat] * state.LinearWeights[(int) stat];
            return total;
        }

        private static void UpdateLinearBounds(double[] anchor, SearchState state)
        {
            if (state.BestScore <= 0 || double.IsInfinity(state.BestScore) ||
                state.BestScore <= state.LinearAnchorScore * 1.01)
                return;

            // Weighted AM-GM: product(t_s^a_s) / product(anchor_s^a_s)
            // <= (sum(a_s * t_s / anchor_s) / sum(a_s)) ^ sum(a_s).
            // Maximizing that linear sum over distinct IDs also bounds conflicting stat maxima.
            var fight = state.Goal == GearOptimizationGoal.Fight || state.Goal == GearOptimizationGoal.ManualAdventure;
            var sumFight = state.Goal == GearOptimizationGoal.Fight && state.FightScoreMode == FightScoreMode.PowerPlusToughness;
            var rootFight = state.Goal == GearOptimizationGoal.Fight &&
                state.FightScoreMode != FightScoreMode.PowerTimesToughness && !sumFight;
            var rootResources = state.Goal == GearOptimizationGoal.AllNgu || state.Goal == GearOptimizationGoal.TimeMachine ||
                state.Goal == GearOptimizationGoal.Wandoos;
            state.LinearExponent = 0;
            foreach (var stat in state.RelevantStats)
            {
                var combatStat = stat == StatKey.Power || stat == StatKey.Toughness;
                var exponent = (fight && combatStat && rootFight) ||
                    (rootResources && (stat == StatKey.EnergyCap || stat == StatKey.EnergyPower || stat == StatKey.MagicCap || stat == StatKey.MagicPower)) ? .5 : 1;
                var denominator = sumFight && combatStat ? anchor[(int) StatKey.Power] + anchor[(int) StatKey.Toughness] : anchor[(int) stat];
                if (denominator <= 0)
                    return;
                state.LinearWeights[(int) stat] = exponent / denominator;
                // P+T is one affine factor, despite being stored in two stat components.
                if (!sumFight || stat != StatKey.Toughness)
                    state.LinearExponent += exponent;
            }
            state.LinearAnchorScore = state.BestScore;
            for (var i = state.MainOptions.Length - 1; i >= 0; i--)
            {
                var maximum = 0d;
                foreach (var option in state.MainOptions[i])
                    maximum = Math.Max(maximum, WeightedStats(option.Stats, state));
                state.LinearMainBounds[i] = maximum + state.LinearMainBounds[i + 1];
            }
            for (var i = state.AccessoryOptions.Length - 1; i >= 0; i--)
            {
                var maximum = 0d;
                foreach (var option in state.AccessoryOptions[i])
                    maximum = Math.Max(maximum, WeightedStats(option.Stats, state));
                for (var k = 1; k <= state.AccessoryPickCount; k++)
                    state.LinearAccessoryBounds[i, k] = Math.Max(state.LinearAccessoryBounds[i + 1, k],
                        maximum + state.LinearAccessoryBounds[i + 1, k - 1]);
            }
        }

        private static void SelectOption(SearchOption option, int depth, int currentCount, SearchState state)
        {
            // Parent totals are never subtracted/restored, avoiding drift across sibling branches.
            foreach (var stat in state.RelevantStats)
                state.Totals[depth + 1][(int) stat] = state.Totals[depth][(int) stat] + option.Stats[(int) stat];
            Array.Copy(option.Items, 0, state.Current, currentCount, option.Items.Length);
        }

        private static void SearchMainSlots(int groupIndex, int currentCount, SearchState state)
        {
            state.VisitedNodes++;
            if (!CanImprove(state, groupIndex, groupIndex, 0, state.AccessoryPickCount))
                return;
            if (groupIndex == state.MainOptions.Length)
            {
                SearchAccessories(0, state.AccessoryPickCount, groupIndex, currentCount, state);
                return;
            }
            foreach (var option in state.MainOptions[groupIndex])
            {
                SelectOption(option, groupIndex, currentCount, state);
                SearchMainSlots(groupIndex + 1, currentCount + option.Items.Length, state);
            }
        }

        private static void SearchAccessories(int start, int remaining, int depth, int currentCount, SearchState state)
        {
            state.VisitedNodes++;
            if (remaining == 0)
            {
                var score = ScoreTotals(state.Goal, state.FightScoreMode, state.Totals[depth]);
                state.EvaluatedCombinations++;
                if (score > state.BestScore)
                {
                    state.BestScore = score;
                    state.BestItems = new List<SelectedGear>(currentCount);
                    for (var i = 0; i < currentCount; i++)
                        state.BestItems.Add(state.Current[i]);
                    UpdateLinearBounds(state.Totals[depth], state);
                }
                return;
            }
            for (var i = start; i <= state.AccessoryOptions.Length - remaining; i++)
            {
                if (!CanImprove(state, depth, state.MainOptions.Length, i, remaining))
                    break;
                var previous = state.PreviousEquivalentAccessory[i];
                // Identical alternative sets use a prefix of IDs and sorted alternative indices.
                if (previous >= 0 && state.ChosenAccessoryOptions[previous] < 0)
                {
                    state.PrunedBranches++;
                    state.SymmetryPrunedBranches++;
                    continue;
                }
                for (var o = 0; o < state.AccessoryOptions[i].Length; o++)
                {
                    if (previous >= 0 && o < state.ChosenAccessoryOptions[previous])
                    {
                        state.PrunedBranches++;
                        state.SymmetryPrunedBranches++;
                        continue;
                    }
                    var option = state.AccessoryOptions[i][o];
                    if (option.RequiredAccessoryGroups.Any(a => state.ChosenAccessoryOptions[a] < 0))
                    {
                        state.PrunedBranches++;
                        state.DominancePrunedBranches++;
                        continue;
                    }
                    state.ChosenAccessoryOptions[i] = o;
                    SelectOption(option, depth, currentCount, state);
                    SearchAccessories(i + 1, remaining - 1, depth + 1, currentCount + 1, state);
                    state.ChosenAccessoryOptions[i] = -1;
                }
            }
        }

        private static double ScoreItems(GearOptimizationGoal goal, FightScoreMode fightScoreMode, StatKey[] relevantStats, IEnumerable<SelectedGear> items)
        {
            var totals = new double[Enum.GetValues(typeof(StatKey)).Length];
            foreach (var stat in relevantStats)
            {
                totals[(int) stat] = stat == StatKey.Power || stat == StatKey.Toughness ? 0 : 100;
            }

            foreach (var item in items)
            {
                foreach (var stat in relevantStats)
                {
                    totals[(int) stat] += Get(item.Candidate, stat) * item.Factor;
                }
            }

            return ScoreTotals(goal, fightScoreMode, totals);
        }

        private static double ScoreTotals(GearOptimizationGoal goal, FightScoreMode fightScoreMode, double[] totals)
        {
            switch (goal)
            {
                case GearOptimizationGoal.EnergyCap:
                    return totals[(int) StatKey.EnergyCap];
                case GearOptimizationGoal.MagicCap:
                    return totals[(int) StatKey.MagicCap];
                case GearOptimizationGoal.EnergyNgu:
                    return Product(totals, StatKey.EnergyCap, StatKey.EnergyPower, StatKey.NguSpeed);
                case GearOptimizationGoal.MagicNgu:
                    return Product(totals, StatKey.MagicCap, StatKey.MagicPower, StatKey.NguSpeed);
                case GearOptimizationGoal.AllNgu:
                    return Math.Sqrt(totals[(int) StatKey.EnergyCap] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.EnergyPower] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.MagicCap] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.MagicPower] / 100.0)
                           * (totals[(int) StatKey.NguSpeed] / 100.0);
                case GearOptimizationGoal.TimeMachine:
                    return Math.Sqrt(totals[(int) StatKey.EnergyCap] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.EnergyPower] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.MagicCap] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.MagicPower] / 100.0);
                case GearOptimizationGoal.Blood:
                    return Product(totals, StatKey.MagicCap, StatKey.MagicPower);
                case GearOptimizationGoal.Gold:
                    return totals[(int) StatKey.Gold] / 100.0;
                case GearOptimizationGoal.Drop:
                    return totals[(int) StatKey.Drop] / 100.0;
                case GearOptimizationGoal.Fight:
                    return ScoreFightGear(totals, fightScoreMode);
                case GearOptimizationGoal.ManualAdventure:
                    return ScoreManualAdventureGear(totals);
                case GearOptimizationGoal.Augments:
                    return Product(totals, StatKey.EnergyCap, StatKey.EnergyPower, StatKey.AugmentSpeed);
                case GearOptimizationGoal.AdvancedTraining:
                    return Product(totals, StatKey.EnergyCap, StatKey.EnergyPower, StatKey.AdvancedTrainingSpeed);
                case GearOptimizationGoal.Wandoos:
                    return Math.Sqrt(totals[(int) StatKey.EnergyCap] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.EnergyPower] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.MagicCap] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.MagicPower] / 100.0)
                           * (totals[(int) StatKey.WandoosSpeed] / 100.0);
                default:
                    return 0;
            }
        }

        private static double ScoreFightGear(double[] totals, FightScoreMode mode)
        {
            var power = Math.Max(0, totals[(int) StatKey.Power]);
            var toughness = Math.Max(0, totals[(int) StatKey.Toughness]);
            var moveCooldown = FightMoveCooldownMultiplier(totals);
            switch (mode)
            {
                case FightScoreMode.PowerTimesToughness:
                    return power * toughness * moveCooldown;
                case FightScoreMode.PowerPlusToughness:
                    return (power + toughness) * moveCooldown;
                case FightScoreMode.SqrtPowerToughness:
                default:
                    return Math.Sqrt(power * toughness) * moveCooldown;
            }
        }

        private static double ScoreManualAdventureGear(double[] totals)
        {
            return ScoreFightGear(totals, FightScoreMode.PowerTimesToughness);
        }

        private static double FightMoveCooldownMultiplier(double[] totals)
        {
            return Math.Max(0, totals[(int) StatKey.MoveCooldown]) / 100.0;
        }

        private static double Product(double[] totals, StatKey first, StatKey second)
        {
            return (totals[(int) first] / 100.0) * (totals[(int) second] / 100.0);
        }

        private static double Product(double[] totals, StatKey first, StatKey second, StatKey third)
        {
            return Product(totals, first, second) * (totals[(int) third] / 100.0);
        }

        private static StatKey[] GetRelevantStats(GearOptimizationGoal goal)
        {
            switch (goal)
            {
                case GearOptimizationGoal.EnergyCap:
                    return new[] {StatKey.EnergyCap};
                case GearOptimizationGoal.MagicCap:
                    return new[] {StatKey.MagicCap};
                case GearOptimizationGoal.EnergyNgu:
                    return new[] {StatKey.EnergyCap, StatKey.EnergyPower, StatKey.NguSpeed};
                case GearOptimizationGoal.MagicNgu:
                    return new[] {StatKey.MagicCap, StatKey.MagicPower, StatKey.NguSpeed};
                case GearOptimizationGoal.AllNgu:
                    return new[] {StatKey.EnergyCap, StatKey.EnergyPower, StatKey.MagicCap, StatKey.MagicPower, StatKey.NguSpeed};
                case GearOptimizationGoal.TimeMachine:
                    return new[] {StatKey.EnergyCap, StatKey.EnergyPower, StatKey.MagicCap, StatKey.MagicPower};
                case GearOptimizationGoal.Blood:
                    return new[] {StatKey.MagicCap, StatKey.MagicPower};
                case GearOptimizationGoal.Gold:
                    return new[] {StatKey.Gold};
                case GearOptimizationGoal.Drop:
                    return new[] {StatKey.Drop};
                case GearOptimizationGoal.Fight:
                    return new[] {StatKey.Power, StatKey.Toughness, StatKey.MoveCooldown};
                case GearOptimizationGoal.ManualAdventure:
                    return new[] {StatKey.Power, StatKey.Toughness, StatKey.MoveCooldown};
                case GearOptimizationGoal.Augments:
                    return new[] {StatKey.EnergyCap, StatKey.EnergyPower, StatKey.AugmentSpeed};
                case GearOptimizationGoal.AdvancedTraining:
                    return new[] {StatKey.EnergyCap, StatKey.EnergyPower, StatKey.AdvancedTrainingSpeed};
                case GearOptimizationGoal.Wandoos:
                    return new[] {StatKey.EnergyCap, StatKey.EnergyPower, StatKey.MagicCap, StatKey.MagicPower, StatKey.WandoosSpeed};
                default:
                    return new[] {StatKey.Power, StatKey.Toughness};
            }
        }

        private static double Get(GearCandidate candidate, StatKey stat)
        {
            return candidate.Stats.ContainsKey(stat) ? candidate.Stats[stat] : 0;
        }

        internal static string EquipGear(ih[] gear, bool resourcesAlreadyRemoved, bool restoreOriginalSlots = false)
        {
            if (gear == null || gear.Length == 0)
                return "Cannot equip an empty loadout.";

            var controller = Main.Controller;
            var inv = Main.Character.inventory;

            try
            {
                var instances = new HashSet<Equipment>();
                foreach (var item in gear)
                {
                    if (item?.equipment == null || !instances.Add(item.equipment))
                        return "Loadout contains missing or duplicate equipment instances.";
                }

                Dictionary<Equipment, int> destinations;
                List<EquipSwap> swaps;
                var failure = PlanEquipGear(gear, inv, controller.weapon2Unlocked(), controller.accessorySpaces(),
                    controller.curSpaces(), restoreOriginalSlots, out destinations, out swaps);
                if (failure != null)
                    return failure;

                if (!resourcesAlreadyRemoved)
                    LoadoutManager.RemoveResourcesForGearSwap();

                foreach (var swap in swaps)
                {
                    if (FindCurrentSlot(swap.EquippedItem) != swap.EquippedSlot ||
                        FindCurrentSlot(swap.InventoryItem) != swap.InventorySlot)
                        return "Equipment positions changed during the gear swap; stopped before the next operation.";
                    if (swap.EquippedItem.id != 0 && swap.EquippedItem.id == swap.InventoryItem.id)
                        return "Equipment IDs changed during the gear swap; stopped to prevent merging.";

                    inv.item1 = swap.EquippedSlot;
                    inv.item2 = swap.InventorySlot;
                    switch (swap.EquippedSlot)
                    {
                        case -1: controller.swapHead(); break;
                        case -2: controller.swapChest(); break;
                        case -3: controller.swapLegs(); break;
                        case -4: controller.swapBoots(); break;
                        case -5: controller.swapWeapon(); break;
                        case -6: controller.swapWeapon2(); break;
                        default: controller.swapAcc(); break;
                    }

                    if (FindCurrentSlot(swap.InventoryItem) != swap.EquippedSlot ||
                        FindCurrentSlot(swap.EquippedItem) != swap.InventorySlot)
                        return $"Game did not swap the expected equipment instances in slots {swap.EquippedSlot} and {swap.InventorySlot}.";
                }

                if (destinations.Any(x => FindCurrentSlot(x.Key) != x.Value))
                    return "Equipped items do not match the selected loadout.";

                controller.updateBonuses();
                controller.updateInventory();
                return null;
            }
            catch (Exception e)
            {
                Main.LogError("Equip optimized gear", e);
                return $"Failed to equip loadout: {e.Message}";
            }
        }

        private class EquipSwap
        {
            internal int EquippedSlot;
            internal int InventorySlot;
            internal Equipment EquippedItem;
            internal Equipment InventoryItem;
        }

        // Simulate controller-compatible swaps without touching game data or resource allocations.
        private static string PlanEquipGear(ih[] gear, Inventory inv, bool weapon2Unlocked, int accessorySpaces,
            int inventorySpaces, bool restoreOriginalSlots, out Dictionary<Equipment, int> destinations, out List<EquipSwap> swaps)
        {
            destinations = new Dictionary<Equipment, int>();
            swaps = new List<EquipSwap>();
            if (gear == null || gear.Length == 0)
                return "Cannot equip an empty loadout.";

            var instances = new HashSet<Equipment>();
            foreach (var item in gear)
                if (item?.equipment == null || !instances.Add(item.equipment))
                    return "Loadout contains missing or duplicate equipment instances.";
            var ids = new HashSet<int>();
            foreach (var item in gear)
                if (item.equipment.id == 0 || !ids.Add(item.equipment.id))
                    return "Loadout contains empty equipment or duplicate item IDs.";

            var slots = new Dictionary<int, Equipment>
            {
                {-1, inv.head}, {-2, inv.chest}, {-3, inv.legs}, {-4, inv.boots},
                {-5, inv.weapon}, {-6, inv.weapon2}
            };
            for (var i = 0; i < inv.accs.Count; i++) slots.Add(10000 + i, inv.accs[i]);
            for (var i = 0; i < Math.Min(inventorySpaces, inv.inventory.Count); i++) slots.Add(i, inv.inventory[i]);
            var equippedSlots = slots.Keys.Where(x => x < 0 || x >= 10000).ToArray();
            var availableAccessories = Math.Min(accessorySpaces, inv.accs.Count);
            Func<int, bool> available = slot => (slot >= -5 && slot <= -1) ||
                (slot == -6 && weapon2Unlocked) || (slot >= 10000 && slot - 10000 < availableAccessories);
            var weaponSlot = -5;
            var accSlot = 10000;
            foreach (var item in gear)
            {
                int destination;
                switch (item.equipment.type)
                {
                    case part.Head: destination = -1; break;
                    case part.Chest: destination = -2; break;
                    case part.Legs: destination = -3; break;
                    case part.Boots: destination = -4; break;
                    case part.Weapon: destination = weaponSlot--; break;
                    case part.Accessory: destination = accSlot++; break;
                    default: return $"Item {item.id} is not equippable gear.";
                }
                if (restoreOriginalSlots)
                {
                    var original = item.slot;
                    var typeMatches = item.equipment.type == part.Weapon ? original == -5 || original == -6 :
                        item.equipment.type == part.Accessory ? original >= 10000 : original == destination;
                    if (!typeMatches)
                        return "Original equipment slot does not match the selected item's type.";
                    destination = original;
                }
                if (!available(destination) || destinations.ContainsValue(destination))
                    return "Loadout requires duplicate or unavailable equipment slots.";
                if (!slots.Any(x => ReferenceEquals(x.Value, item.equipment) &&
                                    (x.Key >= 0 && x.Key < 10000 || available(x.Key))))
                    return $"Cannot find selected item {item.name} ({item.id}); loadout was not equipped.";
                destinations.Add(item.equipment, destination);
            }

            var plannedSwaps = swaps;
            Action<int, int> swap = (equipped, inventory) =>
            {
                plannedSwaps.Add(new EquipSwap {EquippedSlot = equipped, InventorySlot = inventory,
                    EquippedItem = slots[equipped], InventoryItem = slots[inventory]});
                var previous = slots[equipped];
                slots[equipped] = slots[inventory];
                slots[inventory] = previous;
            };
            var pending = gear.ToList();
            while (pending.Count > 0)
            {
                for (var i = pending.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(slots[destinations[pending[i].equipment]], pending[i].equipment))
                        pending.RemoveAt(i);
                if (pending.Count == 0)
                    break;

                // Finish the active swap cycle before consuming another empty inventory slot.
                var item = pending.FirstOrDefault(x => slots.Any(slot => slot.Key >= 0 && slot.Key < 10000 &&
                    ReferenceEquals(slot.Value, x.equipment))) ?? pending[0];
                var destination = destinations[item.equipment];

                // Never send same-ID copies to a merge-capable controller path. Also clear
                // equipped sources/ID conflicts so weapon/accessory alreadyEquipped checks pass.
                foreach (var equipped in equippedSlots)
                {
                    var occupant = slots[equipped];
                    if (occupant == null)
                        return "Loadout encountered a missing equipped slot.";
                    if (occupant.id == 0 || occupant.id != item.equipment.id)
                        continue;
                    if (!available(equipped))
                        return "Selected item ID conflicts with an unavailable equipment slot.";
                    var empty = slots.Where(x => x.Key >= 0 && x.Key < 10000 && x.Value != null && x.Value.id == 0)
                        .Select(x => x.Key).DefaultIfEmpty(-1).First();
                    if (empty == -1)
                        return "Safe gear swap requires an empty inventory slot; loadout was not equipped.";
                    swap(equipped, empty);
                }
                var source = slots.First(x => ReferenceEquals(x.Value, item.equipment)).Key;
                swap(destination, source);
                pending.Remove(item);
            }
            return null;
        }

        private static int FindCurrentSlot(Equipment equipment)
        {
            if (equipment == null) return int.MinValue;
            var inv = Main.Character.inventory;
            if (ReferenceEquals(inv.head, equipment)) return -1;
            if (ReferenceEquals(inv.chest, equipment)) return -2;
            if (ReferenceEquals(inv.legs, equipment)) return -3;
            if (ReferenceEquals(inv.boots, equipment)) return -4;
            if (ReferenceEquals(inv.weapon, equipment)) return -5;
            if (Main.Controller.weapon2Unlocked() && ReferenceEquals(inv.weapon2, equipment)) return -6;

            for (var i = 0; i < inv.accs.Count; i++)
            {
                if (ReferenceEquals(inv.accs[i], equipment))
                    return 10000 + i;
            }

            for (var i = 0; i < inv.inventory.Count; i++)
            {
                if (ReferenceEquals(inv.inventory[i], equipment))
                    return i;
            }

            return int.MinValue;
        }
    }
}
