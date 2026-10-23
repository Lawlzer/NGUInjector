using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace NGUInjector.Managers
{
    // Each bit authorizes a whole-lane recall, not just the currently selected activity.
    [Flags]
    internal enum GearRecallFootprint { None = 0, Energy = 1, Magic = 2, Res3 = 4 }

    internal sealed class GearOperationOutcome
    {
        internal string Operation;
        internal int EquippedSlot, InventorySlot;
        internal MutationDisposition Disposition;
        internal bool EpochMatches, BeforeMatches, ExpectedMatches;
        internal GearOptimizer.GearObservation Before, Expected, Observed;
        internal string Reason;
        internal Exception Error;
    }

    internal sealed class GearMutationOutcome
    {
        internal MutationDisposition Disposition;
        internal string Reason;
        internal Exception Error;
        internal int PlannedSwaps, VerifiedSwaps;
        internal GearRecallFootprint RecallFootprint;
        internal readonly List<GearOperationOutcome> Operations = new List<GearOperationOutcome>();
        internal bool Verified => Disposition == MutationDisposition.AppliedVerified
            || Disposition == MutationDisposition.ModelAppliedRenderFailed;
        internal bool KnownProgressStillOwned()
        {
            var expected = Operations.LastOrDefault(x => x.EpochMatches && x.ExpectedMatches)?.Expected;
            return Disposition == MutationDisposition.KnownPartial && expected != null
                && expected.EpochMatches() && expected.Matches(new GearOptimizer.GearObservation());
        }
        // Legacy callers must still see presentation errors rather than silently swallowing them.
        internal string LegacyFailure => Disposition == MutationDisposition.AppliedVerified ? null : Reason ?? Disposition.ToString();
    }

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
        ManualAdventure = 13,
        AdventureGold = 14
    }

    internal enum FightScoreMode
    {
        SqrtPowerToughness = 0,
        PowerTimesToughness = 1,
        PowerPlusToughness = 2
    }

    internal class GearOptimizerResult
    {
        internal GearMutationOutcome MutationOutcome;
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

    internal static partial class GearOptimizer
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
            internal Func<double, double, double> CustomScore;
            internal Stopwatch Budget;
            internal long SearchWork;
            internal bool BudgetExceeded;
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
            {(int) GearOptimizationGoal.ManualAdventure, "Manual Adventure (Power * Toughness * Move Cooldown)"},
            {(int) GearOptimizationGoal.AdventureGold, "Adventure Gold (90% Gold / 10% sqrt(Power * Toughness))"}
        };

        internal static readonly Dictionary<int, string> FightScoreModeLabels = new Dictionary<int, string>
        {
            {(int) FightScoreMode.SqrtPowerToughness, "sqrt(Power * Toughness) * Move Cooldown"},
            {(int) FightScoreMode.PowerTimesToughness, "Power * Toughness * Move Cooldown"},
            {(int) FightScoreMode.PowerPlusToughness, "(Power + Toughness) * Move Cooldown"}
        };

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

        private static GearOptimizerResult OptimizeAutokillCandidates(List<GearCandidate> candidates,
            int accessorySlots, float weapon2Factor, Func<double, double, double> score)
        {
            if (score == null) return Fail("Autokill feasibility scorer is missing.");
            return OptimizePreparedCandidates(candidates, accessorySlots, weapon2Factor, new SearchState
            {
                RelevantStats = new[] {StatKey.Power, StatKey.Toughness},
                CustomScore = score,
                Budget = Stopwatch.StartNew()
            });
        }

        private static GearOptimizerResult OptimizeCandidates(GearOptimizationGoal goal, FightScoreMode fightScoreMode,
            List<GearCandidate> candidates, int accessorySlots, float weapon2Factor)
        {
            var relevantStats = GetRelevantStats(goal);
            if (candidates.Count > 0 && relevantStats.Length == 1)
                return OptimizeSingleStatGoal(goal, fightScoreMode, candidates, relevantStats, relevantStats[0], accessorySlots, weapon2Factor);
            return OptimizePreparedCandidates(candidates, accessorySlots, weapon2Factor,
                new SearchState {Goal = goal, FightScoreMode = fightScoreMode, RelevantStats = relevantStats});
        }

        private static GearOptimizerResult OptimizePreparedCandidates(List<GearCandidate> candidates,
            int accessorySlots, float weapon2Factor, SearchState state)
        {
            if (candidates.Count == 0)
            {
                return Fail("No owned gear candidates found.");
            }

            var relevantStats = state.RelevantStats;
            var heads = FilterSlot(candidates, part.Head, relevantStats);
            var chests = FilterSlot(candidates, part.Chest, relevantStats);
            var legs = FilterSlot(candidates, part.Legs, relevantStats);
            var boots = FilterSlot(candidates, part.Boots, relevantStats);

            var weaponSlots = weapon2Factor > 0 ? 2 : 1;
            var weapons = FilterSlot(candidates, part.Weapon, relevantStats, weaponSlots);
            var accessories = FilterSlot(candidates, part.Accessory, relevantStats, Math.Max(1, accessorySlots))
                .OrderByDescending(x => state.CustomScore == null
                    ? ScoreItems(state.Goal, state.FightScoreMode, relevantStats, new[] {new SelectedGear {Candidate = x}})
                    : state.CustomScore(Get(x, StatKey.Power), Get(x, StatKey.Toughness)))
                .ToList();

            if (heads.Count == 0 || chests.Count == 0 || legs.Count == 0 || boots.Count == 0 || weapons.Count == 0)
            {
                return Fail("Missing at least one required gear slot for optimization.");
            }

            var accessoryPickCount = Math.Min(accessorySlots, accessories.Select(x => x.Id).Distinct().Count());

            var weaponSets = BuildWeaponSets(weapons, weapon2Factor);
            var mainGroups = new List<List<GearCandidate>> {heads, chests, legs, boots};
            SearchGear(mainGroups, weaponSets, accessories, accessoryPickCount, state);

            if (state.CustomScore != null && (state.BestItems.Count == 0 || state.BestScore != 1))
            {
                var failed = Fail(state.BudgetExceeded
                    ? "Autokill gear search incomplete: reached the 750 ms / 100,000-work budget; feasibility is unknown."
                    : "No projected-feasible autokill loadout found in the equipment sum model; native completeness is not certified.");
                failed.EvaluatedCombinations = state.EvaluatedCombinations;
                failed.VisitedNodes = state.VisitedNodes;
                failed.PrunedBranches = state.PrunedBranches;
                failed.ElapsedMilliseconds = state.Budget.ElapsedMilliseconds;
                return failed;
            }
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
                ElapsedMilliseconds = state.Budget == null ? 0 : state.Budget.ElapsedMilliseconds,
                Message = (state.CustomScore != null ? "Found projected-feasible autokill gear. " :
                    $"Optimized {GoalDescription(state.Goal, state.FightScoreMode)} gear exactly. Score {state.BestScore:0.###}; ") +
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

        internal static GearOptimizerResult GetCachedOrOptimize(GearOptimizationGoal goal, FightScoreMode fightScoreMode)
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
            if (SearchStopped(state)) return;
            state.AccessoryPickCount = Math.Max(0, Math.Min(accessoryPickCount, accessories.Select(x => x.Id).Distinct().Count()));
            var groups = new List<SearchOption[]> {weaponSets.Select(x => MakeSearchOption(x.ToArray(), state)).ToArray()};
            groups.AddRange(mainGroups.Select(group => group.Select(x => MakeSearchOption(new[] {new SelectedGear {Candidate = x}}, state)).ToArray()));
            state.MainOptions = groups.ToArray();
            state.AccessoryOptions = accessories.GroupBy(x => x.Id)
                .Select(group => group.Select(x => MakeSearchOption(new[] {new SelectedGear {Candidate = x}}, state)).ToArray()).ToArray();
            if (state.MainOptions.Any(group => group.Length == 0))
                return;
            PrepareAccessoryImplications(state);
            if (SearchStopped(state)) return;

            // Each component may choose a different item/copy. This is deliberately optimistic,
            // but each top-k sum uses distinct IDs, including when an ID has several tradeoff copies.
            var statCount = Enum.GetValues(typeof(StatKey)).Length;
            state.AccessoryBounds = new double[state.AccessoryOptions.Length + 1, state.AccessoryPickCount + 1, statCount];
            for (var i = state.AccessoryOptions.Length - 1; i >= 0; i--)
            {
                if (SearchStopped(state)) return;
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
            if (SearchStopped(state)) return;
            if (state.CustomScore != null && !CanImprove(state, 0, 0, 0, state.AccessoryPickCount)) return;
            SeedSearch(state);
            SearchMainSlots(0, 0, state);
        }

        private static void PrepareAccessoryImplications(SearchState state)
        {
            var groups = state.AccessoryOptions;
            for (var i = 0; i < groups.Length; i++)
            {
                if (SearchStopped(state)) return;
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
                if (SearchStopped(state)) return;
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
                    if (SearchStopped(state)) return;
                    if (state.CustomScore != null) state.SearchWork++;
                    if (i >= state.MainOptions.Length && usedIds.Contains(option.Items[0].Candidate.Id))
                        continue;
                    foreach (var stat in state.RelevantStats)
                        trial[(int) stat] = totals[(int) stat] + option.Stats[(int) stat];
                    var score = ScoreSearchTotals(state, trial, state.CustomScore != null && i == seed.Length - 1
                        ? seed.Take(i).SelectMany(x => x.Items).Concat(option.Items) : null);
                    if (i == seed.Length - 1)
                    {
                        state.EvaluatedCombinations++;
                        state.HeuristicEvaluations++;
                        if (state.CustomScore != null && score == 1)
                        {
                            seed[i] = option;
                            state.BestItems = seed.SelectMany(x => x.Items).ToList();
                            state.BestScore = state.SeedScore = score;
                            return;
                        }
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
                        if (SearchStopped(state)) return;
                        if (state.CustomScore != null) state.SearchWork++;
                        if (option == seed[i] || (i >= state.MainOptions.Length && usedIds.Contains(option.Items[0].Candidate.Id)))
                            continue;
                        Array.Copy(state.Totals[0], trial, trial.Length);
                        for (var j = 0; j < seed.Length; j++)
                            foreach (var stat in state.RelevantStats)
                                trial[(int) stat] += (j == i ? option : seed[j]).Stats[(int) stat];
                        var score = ScoreSearchTotals(state, trial, state.CustomScore != null
                            ? seed.SelectMany((x, index) => (index == i ? option : x).Items) : null);
                        state.EvaluatedCombinations++;
                        state.HeuristicEvaluations++;
                        if (score <= state.BestScore)
                            continue;
                        seed[i] = option;
                        state.BestScore = score;
                        improved = true;
                        if (state.CustomScore != null && score == 1)
                        {
                            state.BestItems = seed.SelectMany(x => x.Items).ToList();
                            state.SeedScore = score;
                            return;
                        }
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
                if (state.CustomScore != null)
                {
                    // Nonnegative float inputs: cover offhand multiplication, reordered additions,
                    // subnormals and double bound arithmetic. Never prune a float threshold crossing.
                    var operations = state.Current.Length + 2;
                    var paddedBound = state.BoundTotals[s] / (1 - operations * 1.1920928955078125e-7) + operations * (double) float.Epsilon;
                    state.BoundTotals[s] = paddedBound >= float.MaxValue ? double.PositiveInfinity : paddedBound;
                }
            }
            var bound = ScoreSearchTotals(state, state.BoundTotals);
            // Only prune comfortably below the incumbent, never on an approximate equality.
            var cutoff = state.CustomScore != null ? 1 : state.BestScore - Math.Max(1, Math.Abs(state.BestScore)) * 1e-12;
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
            if (state.CustomScore != null) return;
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
                    (state.Goal == GearOptimizationGoal.AdvancedTraining && stat == StatKey.EnergyPower) ||
                    (rootResources && (stat == StatKey.EnergyCap || stat == StatKey.EnergyPower || stat == StatKey.MagicCap || stat == StatKey.MagicPower)) ? .5 : 1;
                if (state.Goal == GearOptimizationGoal.AdventureGold)
                    exponent = stat == StatKey.Gold ? .9 : .05;
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
            if (SearchStopped(state)) return;
            if (state.CustomScore != null) state.SearchWork++;
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
                if (SearchStopped(state)) return;
                SelectOption(option, groupIndex, currentCount, state);
                SearchMainSlots(groupIndex + 1, currentCount + option.Items.Length, state);
            }
        }

        private static void SearchAccessories(int start, int remaining, int depth, int currentCount, SearchState state)
        {
            if (SearchStopped(state)) return;
            if (state.CustomScore != null) state.SearchWork++;
            state.VisitedNodes++;
            if (remaining == 0)
            {
                var score = ScoreSearchTotals(state, state.Totals[depth], state.CustomScore != null ? state.Current.Take(currentCount) : null);
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
                if (SearchStopped(state)) return;
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
                    if (SearchStopped(state)) return;
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

        private static bool SearchStopped(SearchState state)
        {
            if (state.CustomScore == null) return false;
            if (state.BestScore == 1) return true;
            if (state.SearchWork >= 100000 || state.Budget.ElapsedMilliseconds >= 750)
                state.BudgetExceeded = true;
            return state.BudgetExceeded;
        }

        private static double ScoreSearchTotals(SearchState state, double[] totals, IEnumerable<SelectedGear> complete = null)
        {
            if (state.CustomScore == null) return ScoreTotals(state.Goal, state.FightScoreMode, totals);
            if (complete == null) return state.CustomScore(totals[(int) StatKey.Power], totals[(int) StatKey.Toughness]);

            var items = complete.ToArray();
            var native = new float[2];
            for (var s = 0; s < 2; s++)
            {
                var stat = s == 0 ? StatKey.Power : StatKey.Toughness;
                var main = new float[6];
                var weapon = 0;
                foreach (var item in items)
                {
                    var slot = item.Candidate.Slot;
                    var index = slot == part.Weapon ? (weapon++ == 0 ? 0 : 5) :
                        slot == part.Head ? 1 : slot == part.Chest ? 2 : slot == part.Legs ? 3 : slot == part.Boots ? 4 : -1;
                    if (index >= 0)
                        main[index] = (float) Get(item.Candidate, stat) * (float) item.Factor;
                }
                // PlanEquipGear assigns weapons and accessories in result order, not source-slot order.
                // Native equipment totals add weapon, armor, scaled offhand, then accessory slots.
                var sum = main[0];
                for (var i = 1; i < main.Length; i++) sum = (float) (sum + main[i]);
                foreach (var item in items)
                    if (item.Candidate.Slot == part.Accessory) sum = (float) (sum + (float) Get(item.Candidate, stat));
                native[s] = sum;
            }
            return state.CustomScore(native[0], native[1]);
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
                case GearOptimizationGoal.AdventureGold:
                    return Math.Pow(totals[(int) StatKey.Gold] / 100.0, .9)
                           * Math.Pow(Math.Max(0, totals[(int) StatKey.Power]), .05)
                           * Math.Pow(Math.Max(0, totals[(int) StatKey.Toughness]), .05);
                case GearOptimizationGoal.Drop:
                    return totals[(int) StatKey.Drop] / 100.0;
                case GearOptimizationGoal.Fight:
                    return ScoreFightGear(totals, fightScoreMode);
                case GearOptimizationGoal.ManualAdventure:
                    return ScoreManualAdventureGear(totals);
                case GearOptimizationGoal.Augments:
                    return Product(totals, StatKey.EnergyCap, StatKey.EnergyPower, StatKey.AugmentSpeed);
                case GearOptimizationGoal.AdvancedTraining:
                    return (totals[(int) StatKey.EnergyCap] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.EnergyPower] / 100.0)
                           * (totals[(int) StatKey.AdvancedTrainingSpeed] / 100.0);
                case GearOptimizationGoal.Wandoos:
                    return Math.Sqrt(totals[(int) StatKey.EnergyCap] / 100.0)
                           * Math.Sqrt(totals[(int) StatKey.MagicCap] / 100.0)
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
                case GearOptimizationGoal.AdventureGold:
                    return new[] {StatKey.Gold, StatKey.Power, StatKey.Toughness};
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
                    return new[] {StatKey.EnergyCap, StatKey.MagicCap, StatKey.WandoosSpeed};
                default:
                    return new[] {StatKey.Power, StatKey.Toughness};
            }
        }

        private static double Get(GearCandidate candidate, StatKey stat)
        {
            return candidate.Stats.ContainsKey(stat) ? candidate.Stats[stat] : 0;
        }

        internal static GearRecallFootprint LegacyRecallFootprint()
        {
            return (Main.Settings?.ManageEnergy == true ? GearRecallFootprint.Energy : 0)
                | (Main.Settings?.ManageMagic == true ? GearRecallFootprint.Magic : 0)
                | (Main.Settings?.ManageR3 == true ? GearRecallFootprint.Res3 : 0);
        }

        // Run on the game thread. No settings-derived recall authority exists on this path.
        // The callback must continue to authorize the entire footprint at every native boundary.
        internal static GearMutationOutcome EquipGearObserved(ih[] gear,
            GearRecallFootprint recallFootprint = GearRecallFootprint.None, bool restoreOriginalSlots = false,
            ih[] exactMap = null, Func<bool> authorized = null)
        {
            return ResourceWriter.Execute(() => EquipGearCore(gear, recallFootprint, restoreOriginalSlots, exactMap, authorized));
        }

        private static GearMutationOutcome EquipGearCore(ih[] gear, GearRecallFootprint footprint, bool restoreOriginalSlots,
            ih[] exactMap, Func<bool> authorized)
        {
            var result = new GearMutationOutcome { Disposition = MutationDisposition.RejectedUnchanged, RecallFootprint = footprint };
            GearObservation expected = null;
            var writes = 0;
            try
            {
                if (gear == null || gear.Length == 0 && exactMap == null)
                    throw new InvalidOperationException("Cannot equip an empty loadout.");
                var instances = new HashSet<Equipment>();
                foreach (var item in gear)
                    if (item?.equipment == null || !instances.Add(item.equipment))
                        throw new InvalidOperationException("Loadout contains missing or duplicate equipment instances.");
                if ((footprint & ~(GearRecallFootprint.Energy | GearRecallFootprint.Magic | GearRecallFootprint.Res3)) != 0)
                    throw new InvalidOperationException("Invalid gear recall footprint.");
                var controller = Main.Controller;
                var c = Main.Character;
                var inv = c.inventory;
                if (controller.midDrag) throw new InvalidOperationException("Cannot equip gear while an inventory drag is in progress.");
                if (exactMap != null)
                {
                    var validation = LoadoutManager.ValidateExactLoadout(exactMap);
                    if (validation != null) throw new InvalidOperationException(validation);
                }
                expected = new GearObservation();
                Dictionary<Equipment, int> destinations;
                List<EquipSwap> swaps;
                var failure = PlanEquipGear(gear, inv, controller.weapon2Unlocked(), controller.accessorySpaces(),
                    controller.curSpaces(), restoreOriginalSlots, out destinations, out swaps, exactMap);
                if (failure != null) throw new InvalidOperationException(failure);
                result.PlannedSwaps = swaps.Count;
                if (authorized != null && !authorized()) throw new InvalidOperationException("Gear mutation footprint is not authorized.");
                // A true no-op must not recall allocations or refresh/clamp resource amounts.
                if (swaps.Count == 0)
                {
                    var observed = new GearObservation();
                    if (controller.midDrag) throw new InvalidOperationException("Cannot equip gear while an inventory drag is in progress.");
                    result.Disposition = expected.EpochMatches() && expected.Matches(observed)
                        && (authorized == null || authorized())
                        ? MutationDisposition.AppliedVerified : MutationDisposition.ForeignConflict;
                    if (!result.Verified) result.Reason = "Gear context changed during no-op preflight.";
                    return result;
                }
                var retainedEnergy = (footprint & GearRecallFootprint.Energy) == 0 ? Math.Max(c.curEnergy, c.idleEnergy)
                    : c.training.attackEnergy.Concat(c.training.defenseEnergy)
                        .Aggregate(0m, (sum, amount) => sum + Math.Max(0, amount));
                var retainedMagic = (footprint & GearRecallFootprint.Magic) == 0 ? Math.Max(c.magic.curMagic, c.magic.idleMagic) : 0m;
                var retainedRes3 = (footprint & GearRecallFootprint.Res3) == 0 ? Math.Max(c.res3.curRes3, c.res3.idleRes3) : 0m;
                failure = CheckSwapCapacity(swaps, retainedEnergy, retainedRes3, retainedMagic, footprint);
                if (failure != null) throw new InvalidOperationException(failure);
                var recalled = new GearObservation(expected);
                for (var lane = 0; lane < 3; lane++)
                    if (((int)footprint & 1 << lane) != 0) recalled.Recall(lane);
                foreach (var swap in swaps) recalled.ClampNativePools(footprint, swap.NativeCaps);
                long[] currentCaps = null;

                // All mutation boundaries, including recalls and final presentation, use the
                // same observe-after-throw rule. Never retry a possibly committed native call.
                Func<string, EquipSwap, int, Action, bool> apply = (name, swap, recallLane, action) =>
                {
                    var operation = new GearOperationOutcome { Operation = name,
                        EquippedSlot = swap?.EquippedSlot ?? 0, InventorySlot = swap?.InventorySlot ?? 0 };
                    result.Operations.Add(operation);
                    var before = new GearObservation();
                    operation.Before = before;
                    operation.EpochMatches = expected.EpochMatches();
                    if (!operation.EpochMatches || !expected.Matches(before) || authorized != null && !authorized())
                    {
                        operation.Disposition = result.Disposition = MutationDisposition.ForeignConflict;
                        result.Reason = operation.Reason = "Gear epoch, complete map, material, resources or authority changed before the next operation.";
                        return false;
                    }
                    if (controller.midDrag)
                    {
                        result.Disposition = operation.Disposition = writes == 0 ? MutationDisposition.RejectedUnchanged : MutationDisposition.KnownPartial;
                        result.Reason = operation.Reason = "Cannot equip gear while an inventory drag is in progress.";
                        return false;
                    }
                    expected = new GearObservation(expected);
                    if (swap != null)
                    {
                        expected.Swap(swap.EquippedSlot, swap.InventorySlot);
                        currentCaps = swap.NativeCaps;
                    }
                    if (recallLane >= 0) expected.Recall(recallLane);
                    operation.Expected = expected;
                    try { action(); }
                    catch (Exception error) { operation.Error = result.Error = error; result.Reason = operation.Reason = error.Message; }
                    var observed = new GearObservation();
                    operation.Observed = observed;
                    operation.EpochMatches = expected.EpochMatches();
                    operation.ExpectedMatches = operation.EpochMatches && expected.Matches(observed);
                    if (operation.EpochMatches && !operation.ExpectedMatches && recallLane < 0 && currentCaps != null)
                    {
                        // Generation callbacks can clamp an over-cap owned pool during native
                        // swap/rendering. Derive the exact debit from the preflight cap, never
                        // adopt arbitrary observed totals or discard an unexpected allocation.
                        var clamped = new GearObservation(expected);
                        var lanes = GearRecallFootprint.None;
                        for (var lane = 0; lane < 2; lane++)
                            if (!expected.PoolMatches(observed, lane)) lanes |= (GearRecallFootprint)(1 << lane);
                        clamped.ClampNativePools(footprint & lanes, currentCaps);
                        if (clamped.Matches(observed))
                        {
                            operation.Expected = expected = clamped;
                            operation.ExpectedMatches = true;
                        }
                    }
                    operation.BeforeMatches = operation.EpochMatches && before.Matches(observed);
                    if (swap != null && operation.ExpectedMatches) result.VerifiedSwaps++;
                    if (!operation.EpochMatches || authorized != null && !authorized()
                        || !operation.ExpectedMatches && !operation.BeforeMatches)
                    {
                        result.Disposition = operation.Disposition = MutationDisposition.ForeignConflict;
                        result.Reason = operation.Reason = "Foreign gear, epoch, material, resource or authority mutation during " + name + ". " + result.Reason;
                        return false;
                    }
                    if (operation.ExpectedMatches)
                    {
                        if (!operation.BeforeMatches) writes++;
                        operation.Disposition = operation.Error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed;
                        if (operation.Error == null) return true;
                        result.Disposition = result.VerifiedSwaps == swaps.Count ? MutationDisposition.ModelAppliedRenderFailed
                            : writes > 0 ? MutationDisposition.KnownPartial : MutationDisposition.RejectedUnchanged;
                        return false;
                    }
                    result.Disposition = writes > 0 ? MutationDisposition.KnownPartial : MutationDisposition.RejectedUnchanged;
                    operation.Disposition = MutationDisposition.RejectedUnchanged;
                    result.Reason = operation.Reason = "Game did not apply the expected " + name + ". " + result.Reason;
                    return false;
                };

                for (var lane = 0; lane < 3; lane++)
                {
                    if (((int)footprint & 1 << lane) == 0) continue;
                    var recallLane = lane;
                    if (!apply("recall " + lane, null, lane, () => {
                        if (recallLane == 2) ResourceWriter.RecallRes3(); else ResourceWriter.Recall(recallLane == 0);
                    })) return result;
                }
                foreach (var swap in swaps)
                    if (!apply("swap", swap, -1, () => {
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
                    })) return result;
                if (!apply("refresh bonuses", null, -1, () => controller.updateBonuses())) return result;
                if (!apply("refresh inventory", null, -1, () => controller.updateInventory())) return result;
                result.Disposition = MutationDisposition.AppliedVerified;
            }
            catch (Exception error)
            {
                result.Error = error; result.Reason = error.Message;
                // Once a native boundary was entered, failed observation is not evidence of no write.
                if (result.Operations.Count > 0)
                {
                    result.Disposition = MutationDisposition.UnknownOutcome;
                    var operation = result.Operations.Last();
                    operation.Disposition = MutationDisposition.UnknownOutcome;
                    operation.Error = error; operation.Reason = error.Message;
                }
                else if (expected != null)
                {
                    try
                    {
                        if (!expected.EpochMatches() || !expected.Matches(new GearObservation()))
                            result.Disposition = MutationDisposition.ForeignConflict;
                    }
                    catch { result.Disposition = MutationDisposition.UnknownOutcome; }
                }
            }
            return result;
        }

        private class EquipSwap
        {
            internal int EquippedSlot;
            internal int InventorySlot;
            internal Equipment EquippedItem;
            internal Equipment InventoryItem;
            internal long[] NativeCaps;
        }

        // Detached read evidence only. The actuator never writes these snapshots back to the game.
        internal sealed class GearObservation
        {
            private readonly LoadoutManager.SnapshotEpoch epoch;
            private readonly object controller;
            private readonly Dictionary<int, Equipment> slots = new Dictionary<int, Equipment>();
            private readonly Dictionary<Equipment, object[]> material = new Dictionary<Equipment, object[]>();
            private static readonly FieldInfo[] MaterialFields = typeof(Equipment).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private readonly object[] context;
            private readonly long[][][] allocations;
            private readonly long[] idle, totals;
            private readonly long[] baseCaps;
            private readonly float[] macguffins;
            private readonly float weapon2Factor;
            private readonly int spaces, accessories;
            private readonly bool weapon2;

            internal GearObservation(GearObservation source)
            {
                epoch = source.epoch; controller = source.controller;
                slots = new Dictionary<int, Equipment>(source.slots);
                material = new Dictionary<Equipment, object[]>(source.material);
                context = source.context; spaces = source.spaces; accessories = source.accessories; weapon2 = source.weapon2;
                allocations = source.allocations.Select(lane => lane.Select(target => target?.ToArray()).ToArray()).ToArray();
                idle = source.idle.ToArray(); totals = source.totals.ToArray();
                baseCaps = source.baseCaps; macguffins = source.macguffins; weapon2Factor = source.weapon2Factor;
            }

            internal GearObservation()
            {
                epoch = new LoadoutManager.SnapshotEpoch();
                controller = Main.Controller;
                var c = Main.Character;
                var inv = c.inventory;
                slots.Add(-1, inv.head); slots.Add(-2, inv.chest); slots.Add(-3, inv.legs);
                slots.Add(-4, inv.boots); slots.Add(-5, inv.weapon); slots.Add(-6, inv.weapon2);
                for (var i = 0; i < inv.accs.Count; i++) slots.Add(10000 + i, inv.accs[i]);
                for (var i = 0; i < inv.inventory.Count; i++) slots.Add(i, inv.inventory[i]);
                foreach (var item in slots.Values.Where(x => x != null).Distinct())
                    material.Add(item, MaterialFields.Select(field => {
                        var value = field.GetValue(item);
                        return value is Array ? ((Array)value).Clone() : value;
                    }).ToArray());
                spaces = Main.Controller.curSpaces(); accessories = Main.Controller.accessorySpaces();
                weapon2 = Main.Controller.weapon2Unlocked();
                weapon2Factor = Main.Controller.weapon2Factor();
                baseCaps = new[] { c.capEnergy, c.magic.capMagic, c.res3.capRes3 };
                macguffins = inv.macguffinBonuses?.ToArray();
                context = new object[] { c.rebirthTime, c.totalPlaytime, inv.accs, inv.inventory, inv.macguffinBonuses, c.NGU, c.NGU?.skills, c.NGU?.magicSkills, c.magic, c.res3,
                    c.advancedTraining, c.advancedTraining?.energy, c.augments, c.augments?.augs, c.machine,
                    c.wandoos98, c.bloodMagic, c.bloodMagic?.ritual, c.wishes, c.wishes?.wishes,
                    c.hacks, c.hacks?.hacks, c.training, c.training?.attackEnergy, c.training?.defenseEnergy }
                    .Concat(c.NGU?.skills?.Cast<object>() ?? Enumerable.Empty<object>())
                    .Concat(c.NGU?.magicSkills?.Cast<object>() ?? Enumerable.Empty<object>())
                    .Concat(c.augments?.augs?.Cast<object>() ?? Enumerable.Empty<object>())
                    .Concat(c.bloodMagic?.ritual?.Cast<object>() ?? Enumerable.Empty<object>())
                    .Concat(c.wishes?.wishes?.Cast<object>() ?? Enumerable.Empty<object>())
                    .Concat(c.hacks?.hacks?.Cast<object>() ?? Enumerable.Empty<object>()).ToArray();
                allocations = new[] {
                    new[] { c.NGU?.skills?.Select(x => x.energy).ToArray(), c.advancedTraining?.energy?.ToArray(),
                        c.augments?.augs?.Select(x => x.augEnergy).ToArray(), c.augments?.augs?.Select(x => x.upgradeEnergy).ToArray(),
                        c.machine == null ? null : new[] { c.machine.speedEnergy },
                        c.wandoos98 == null ? null : new[] { c.wandoos98.wandoosEnergy }, c.wishes?.wishes?.Select(x => x.energy).ToArray(),
                        c.training?.attackEnergy?.ToArray(), c.training?.defenseEnergy?.ToArray() },
                    new[] { c.NGU?.magicSkills?.Select(x => x.magic).ToArray(), c.machine == null ? null : new[] { c.machine.goldMultiMagic },
                        c.wandoos98 == null ? null : new[] { c.wandoos98.wandoosMagic }, c.bloodMagic?.ritual?.Select(x => x.magic).ToArray(),
                        c.wishes?.wishes?.Select(x => x.magic).ToArray() },
                    new[] { c.hacks?.hacks?.Select(x => x.res3).ToArray(), c.wishes?.wishes?.Select(x => x.res3).ToArray() }
                };
                idle = new[] { c.idleEnergy, c.magic.idleMagic, c.res3.idleRes3 };
                totals = new[] { c.curEnergy, c.magic.curMagic, c.res3.curRes3 };
                if (idle.Any(x => x < 0) || totals.Any(x => x < 0)
                    || allocations.SelectMany(x => x).Where(x => x != null).Any(x => x.Any(v => v < 0)))
                    throw new InvalidOperationException("Cannot verify negative native gear resource accounting.");
            }

            internal bool EpochMatches() => epoch.Matches() && ReferenceEquals(controller, Main.Controller);

            internal void Swap(int equipped, int inventory)
            {
                var previous = slots[equipped];
                slots[equipped] = slots[inventory];
                slots[inventory] = previous;
            }

            internal void Recall(int lane)
            {
                var targets = allocations[lane];
                // The last two energy targets are basic training and must survive removeMost.
                for (var i = 0; i < targets.Length - (lane == 0 ? 2 : 0); i++)
                    if (targets[i] != null)
                        for (var j = 0; j < targets[i].Length; j++)
                        {
                            idle[lane] = checked(idle[lane] + targets[i][j]);
                            targets[i][j] = 0;
                        }
            }

            internal bool PoolMatches(GearObservation other, int lane)
            {
                return totals[lane] == other.totals[lane] && idle[lane] == other.idle[lane];
            }

            internal void ClampNativePools(GearRecallFootprint footprint, long[] caps)
            {
                // Energy.addEnergy / MagicDisplay.updateMagicBar debit both total and idle
                // by total - cap when over cap. No refill, rounding tolerance, or R3 authority.
                for (var lane = 0; lane < 2; lane++)
                {
                    if (((int)footprint & 1 << lane) == 0) continue;
                    var loss = Math.Max(0, totals[lane] - caps[lane]);
                    if (loss > idle[lane]) throw new InvalidOperationException("Native cap depletion would consume retained allocations; no gear or resources were changed.");
                    totals[lane] -= loss;
                    idle[lane] -= loss;
                }
            }

            internal bool Matches(GearObservation other)
            {
                if (spaces != other.spaces || accessories != other.accessories || weapon2 != other.weapon2 || weapon2Factor != other.weapon2Factor
                    || !baseCaps.SequenceEqual(other.baseCaps)
                    || (macguffins == null ? other.macguffins != null : other.macguffins == null || !macguffins.SequenceEqual(other.macguffins))
                    || context.Length != other.context.Length || context.Where((x, i) => !ReferenceEquals(x, other.context[i])).Any()
                    || slots.Count != other.slots.Count || slots.Any(x => !other.slots.ContainsKey(x.Key) || !ReferenceEquals(x.Value, other.slots[x.Key]))
                    || !idle.SequenceEqual(other.idle) || !totals.SequenceEqual(other.totals)) return false;
                foreach (var pair in material)
                {
                    object[] actual;
                    if (!other.material.TryGetValue(pair.Key, out actual)) return false;
                    for (var i = 0; i < pair.Value.Length; i++)
                    {
                        var array = pair.Value[i] as Array;
                        if (array != null ? !(actual[i] is Array) || !array.Cast<object>().SequenceEqual(((Array)actual[i]).Cast<object>())
                            : !Equals(pair.Value[i], actual[i])) return false;
                    }
                }
                for (var lane = 0; lane < 3; lane++)
                    for (var target = 0; target < allocations[lane].Length; target++)
                    {
                        var wanted = allocations[lane][target]; var actual = other.allocations[lane][target];
                        if (wanted == null ? actual != null : actual == null || !wanted.SequenceEqual(actual)) return false;
                    }
                return true;
            }
        }

        internal static string CheckExactSwapCapacity(ih[] wanted, decimal retainedEnergy, decimal retainedRes3)
        {
            if (retainedEnergy < 0 || retainedRes3 < 0)
                return "Cannot verify negative retained resource totals; no gear or resources were changed.";
            var c = Main.Character;
            if (Main.Settings?.ManageEnergy != true)
                retainedEnergy = Math.Max(retainedEnergy, Math.Max(c.curEnergy, c.idleEnergy));
            if (Main.Settings?.ManageR3 != true)
                retainedRes3 = Math.Max(retainedRes3, Math.Max(c.res3.curRes3, c.res3.idleRes3));
            var retainedMagic = Main.Settings?.ManageMagic != true ? Math.Max(c.magic.curMagic, c.magic.idleMagic) : 0m;
            Dictionary<Equipment, int> destinations;
            List<EquipSwap> swaps;
            var failure = PlanEquipGear(wanted.Where(x => x.id != 0).ToArray(), Main.Character.inventory,
                Main.Controller.weapon2Unlocked(), Main.Controller.accessorySpaces(), Main.Controller.curSpaces(),
                true, out destinations, out swaps, wanted);
            return failure ?? CheckSwapCapacity(swaps, retainedEnergy, retainedRes3, retainedMagic);
        }

        internal sealed class CapacityTransitionPlan
        {
            internal GearRecallFootprint Recall;
            internal long EnergyMinimum, MagicMinimum;
            internal string Failure;
        }

        // Explicit opt-in only. Use the actuator's exact slot order and projection,
        // retaining every unclaimed pool and the immutable basic-training floor.
        internal static CapacityTransitionPlan PreflightCapacityTransition(ih[] wanted, GearRecallFootprint permitted)
        {
            var result = new CapacityTransitionPlan();
            if ((permitted & ~(GearRecallFootprint.Energy | GearRecallFootprint.Magic)) != 0)
            { result.Failure = "Capacity transition cannot acquire R3 recall authority."; return result; }
            var c = Main.Character;
            Dictionary<Equipment, int> destinations; List<EquipSwap> swaps;
            result.Failure = LoadoutManager.ValidateExactLoadout(wanted);
            if (result.Failure != null) return result;
            result.Failure = PlanEquipGear(wanted.Where(x => x.id != 0).ToArray(), c.inventory,
                Main.Controller.weapon2Unlocked(), Main.Controller.accessorySpaces(), Main.Controller.curSpaces(),
                true, out destinations, out swaps, wanted);
            if (result.Failure != null) return result;
            var basic = c.training.attackEnergy.Concat(c.training.defenseEnergy).Aggregate(0m, (sum, value) => sum + Math.Max(0, value));
            var r3 = Math.Max(c.res3.curRes3, c.res3.idleRes3);
            result.Failure = CheckSwapCapacity(swaps, basic, r3, 0, GearRecallFootprint.Energy | GearRecallFootprint.Magic);
            if (result.Failure != null) return result;
            result.EnergyMinimum = swaps.Aggregate(c.totalCapEnergy(), (cap, swap) => Math.Min(cap, swap.NativeCaps[0]));
            result.MagicMinimum = swaps.Aggregate(c.totalCapMagic(), (cap, swap) => Math.Min(cap, swap.NativeCaps[1]));
            if (Math.Max(c.curEnergy, c.idleEnergy) > result.EnergyMinimum) result.Recall |= GearRecallFootprint.Energy;
            if (Math.Max(c.magic.curMagic, c.magic.idleMagic) > result.MagicMinimum) result.Recall |= GearRecallFootprint.Magic;
            if ((result.Recall & ~permitted) != 0)
            { result.Failure = "Lower-capacity gear requires an unavailable managed resource lane; preserving native allocations."; return result; }
            result.Failure = CheckSwapCapacity(swaps,
                (result.Recall & GearRecallFootprint.Energy) != 0 ? basic : Math.Max(c.curEnergy, c.idleEnergy), r3,
                (result.Recall & GearRecallFootprint.Magic) != 0 ? 0 : Math.Max(c.magic.curMagic, c.magic.idleMagic), result.Recall);
            return result;
        }

        private static string CheckSwapCapacity(List<EquipSwap> swaps, decimal retainedEnergy, decimal retainedRes3, decimal retainedMagic,
            GearRecallFootprint depletionFootprint = GearRecallFootprint.None)
        {
            if (retainedEnergy < 0 || retainedMagic < 0 || retainedRes3 < 0)
                return "Cannot verify negative retained resource totals; no gear or resources were changed.";
            if (retainedEnergy == 0 && retainedRes3 == 0 && retainedMagic == 0
                && (depletionFootprint & (GearRecallFootprint.Energy | GearRecallFootprint.Magic)) == 0) return null;
            var c = Main.Character;
            var energy = retainedEnergy > 0 || (depletionFootprint & GearRecallFootprint.Energy) != 0 ? ResourceProjectionCapture.Create(c, true, true) : null;
            var magic = retainedMagic > 0 || (depletionFootprint & GearRecallFootprint.Magic) != 0 ? ResourceProjectionCapture.Create(c, false, true) : null;
            var map = LoadoutManager.CaptureExactLoadout().ToDictionary(x => x.slot);
            if (energy != null && energy.Project(ResourceProjectionCapture.GetBonuses(map.Values)) != c.totalCapEnergy())
                return "Cannot verify retained energy capacity; no gear or resources were changed.";
            if (retainedEnergy > c.totalCapEnergy())
                return "Gear swap energy capacity is below retained allocations; no gear or resources were changed.";
            if (magic != null && magic.Project(ResourceProjectionCapture.GetBonuses(map.Values)) != c.totalCapMagic())
                return "Cannot verify retained magic capacity; no gear or resources were changed.";
            if (retainedMagic > 0 && retainedMagic > c.totalCapMagic())
                return "Gear swap magic capacity is below retained allocations; no gear or resources were changed.";
            // R3 uses only its own cap spec. Keep its native double multiplication order.
            Func<IEnumerable<ih>, long> res3Cap = gear => {
                var items = gear.ToDictionary(x => x.slot);
                Func<int, float> raw = slot => items.ContainsKey(slot) && items[slot].id != 0
                    ? Main.Controller.equipSpecBonus(specType.Res3Cap, items[slot].equipment) : 0f;
                var points = raw(-5) + raw(-1) + raw(-2) + raw(-3) + raw(-4) + raw(-6) * Main.Controller.weapon2Factor();
                foreach (var slot in items.Keys.Where(x => x >= 10000).OrderBy(x => x)) points += raw(slot);
                var value = (double)c.res3.capRes3 * (1d + Main.Controller.getBonusFactor(points, specType.Res3Cap)) * c.inventory.macguffinBonuses[21];
                value *= c.adventureController.itopod.totalRes3CapBonus();
                value *= c.beastQuestPerkController.totalRes3CapBonus();
                value *= c.wishesController.totalRes3CapBonus();
                return value >= c.hardCap() ? c.hardCap() : Math.Max(1, Convert.ToInt64(Math.Max(1d, value)));
            };
            if (retainedRes3 > 0 && res3Cap(map.Values) != c.totalCapRes3())
                return "Cannot verify retained R3 capacity; no gear or resources were changed.";
            if (retainedRes3 > 0 && retainedRes3 > c.totalCapRes3())
                return "Gear swap R3 capacity is below retained allocations; no gear or resources were changed.";
            foreach (var swap in swaps)
            {
                map[swap.EquippedSlot] = new ih { slot = swap.EquippedSlot, id = swap.InventoryItem.id, equipment = swap.InventoryItem };
                var bonuses = energy != null || magic != null ? ResourceProjectionCapture.GetBonuses(map.Values) : null;
                swap.NativeCaps = new[] { energy != null ? (long)energy.Project(bonuses) : 0, magic != null ? (long)magic.Project(bonuses) : 0 };
                // Capacity is an integer; avoid double-to-decimal's 15-digit conversion rounding.
                if (retainedEnergy > 0 && (decimal)swap.NativeCaps[0] < retainedEnergy)
                    return "Gear swap energy capacity is below retained allocations in an intermediate or final slot map; no gear or resources were changed.";
                if (retainedMagic > 0 && (decimal)swap.NativeCaps[1] < retainedMagic)
                    return "Gear swap magic capacity is below retained allocations in an intermediate or final slot map; no gear or resources were changed.";
                if (retainedRes3 > 0 && res3Cap(map.Values) < retainedRes3)
                    return "Gear swap R3 capacity is below retained allocations in an intermediate or final slot map; no gear or resources were changed.";
            }
            return null;
        }

        // Simulate controller-compatible swaps without touching game data or resource allocations.
        private static string PlanEquipGear(ih[] gear, Inventory inv, bool weapon2Unlocked, int accessorySpaces,
            int inventorySpaces, bool restoreOriginalSlots, out Dictionary<Equipment, int> destinations, out List<EquipSwap> swaps, ih[] exactMap = null)
        {
            destinations = new Dictionary<Equipment, int>();
            swaps = new List<EquipSwap>();
            if (gear == null || gear.Length == 0 && exactMap == null)
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
            if (exactMap != null)
            {
                foreach (var item in exactMap.Where(x => x.id == 0))
                {
                    if (!available(item.slot)) return "Exact loadout requires an unavailable empty destination.";
                    if (slots[item.slot]?.id == 0) continue;
                    var empty = slots.Where(x => x.Key >= 0 && x.Key < 10000 && x.Value != null && x.Value.id == 0)
                        .Select(x => x.Key).DefaultIfEmpty(-1).First();
                    if (empty < 0) return "Safe exact gear restoration requires an empty inventory slot; no gear was changed.";
                    swap(item.slot, empty);
                }
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
