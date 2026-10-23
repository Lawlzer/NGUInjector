using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private sealed class CombatPrepCapture
        {
            internal CombatPrepSnapshot Snapshot;
            internal Dictionary<string, ih[]> Gear = new Dictionary<string, ih[]>();
            internal Dictionary<Equipment, string> Revisions = new Dictionary<Equipment, string>();
            internal ih[] OriginalGear;
            internal object Wandoos, Augments, Training;
            internal object Inventory, InventoryController;
            internal long Rebirth;
            internal double Origin, Elapsed, Total;
            internal string Material;
            internal SupportContext SupportContext;
            internal SupportState OriginalSupportState;
            internal object[] SupportObjects;
            internal string SupportMaterial;
            internal Dictionary<string, CombatPrepSupport> Support = new Dictionary<string, CombatPrepSupport>();
            internal readonly List<string> UnavailableGearGoals = new List<string>();
            internal double AdventureDynamicWeight;
            internal string AdventureTaskKey, AdventureTask;
            internal Dictionary<string, PresetCandidate> AdventureRoutes = new Dictionary<string, PresetCandidate>();
            internal Dictionary<string, PresetSnapshot> AdventureSnapshots = new Dictionary<string, PresetSnapshot>();
            internal bool WeightedProfiles => AdventureDynamicWeight > 0;
        }

        private sealed class CombatPrepSupport
        {
            internal SupportEvaluation Evaluation;
            internal double BudgetFraction;
            internal bool EnforceBudget;
            internal string ProfileGoal;
        }

        private static void RequireCombatPrepContext()
        {
            var c = Character;
            if (Settings == null || c?.rebirthTime == null || c.totalPlaytime == null || c.stats == null
                || c.wandoos98 == null || c.wandoos98Controller == null || c.augmentsController == null
                || c.advancedTrainingController == null || c.inventory == null || Controller == null)
                throw new InvalidOperationException("Combat prep game data is unavailable.");
            if (!c.buttons.wandoos.interactable || !c.settings.wandoos98On || c.wandoos98.disabled || c.inventory.disabled)
                throw new InvalidOperationException("Combat prep requires enabled Wandoos and equipment.");
            if (!c.wandoos98.installed && (c.wandoos98.installTime == null || c.wandoos98.installTime.totalseconds < 86400))
                throw new InvalidOperationException("Combat prep requires installed Wandoos; installation progress is not modeled.");
            if (c.wandoos98.bootupTime == null)
                throw new InvalidOperationException("Combat prep requires the native Wandoos bootup clock.");
            if (!c.advancedTraining.transferredBankedLevels && c.advancedTraining.bankedLevel != null
                && c.advancedTraining.bankedLevel.Any(x => x > 0) && c.advancedTrainingController.advancedTrainingUnlocked())
                throw new InvalidOperationException("Native banked AT transfer is pending; preview after it completes.");
            // Challenge side effects are not modeled by the pure engine. Fail closed, not an optimistic forecast.
            foreach (var field in c.challenges.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var challenge = field.GetValue(c.challenges);
                var active = challenge?.GetType().GetField("inChallenge");
                if (active != null && (bool) active.GetValue(challenge))
                    throw new InvalidOperationException("Combat prep is unavailable during challenges.");
            }
        }

        private static ih[] CombatPrepEquipped()
        {
            return LoadoutManager.CaptureExactLoadout();
        }

        private static string CombatPrepGearRevision(Equipment item)
        {
            return string.Join("|", item.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string))
                .OrderBy(f => f.Name).Select(f => f.Name + "=" + Convert.ToString(f.GetValue(item), CultureInfo.InvariantCulture)).ToArray());
        }

        private static string CombatPrepSupportKey()
        {
            return CaptureSupportState().StableKey;
        }

        private static string CombatPrepSupportMaterial(SupportContext context)
        {
            return context.BeardsAvailable + "/" + context.BeardsDisabled + "/" + context.DiggersAvailable
                + "/" + context.BeardSlots + "/" + context.DiggerSlots + "/" + string.Join(",", context.UnlockedBeards)
                + "/" + string.Join(",", context.Diggers.Select(x => x.Id + "=" + x.Maximum.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        private static string CombatPrepMaterialKey()
        {
            var c = Character;
            return c.settings.rebirthDifficulty + ";" + (int) c.wandoos98.os + ";" + CombatPrepSupportKey()
                + ";support=" + CombatPrepSupportMaterial(CaptureSupportContext())
                + ";slots=" + Controller.accessorySpaces() + "/" + Controller.weapon2Unlocked() + "/" + Controller.weapon2Factor()
                + ";targets=" + string.Join(",", c.advancedTraining.levelTarget.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray())
                + ";augs=" + string.Join(",", c.augments.augs.Select(x => x.augmentTarget + "/" + x.upgradeTarget).ToArray());
        }

        private static bool CombatPrepEpochMatches(CombatPrepCapture capture)
        {
            var c = Character;
            return c?.rebirthTime != null && c.totalPlaytime != null && c.stats != null
                && ReferenceEquals(c.wandoos98, capture.Wandoos) && ReferenceEquals(c.augments, capture.Augments)
                && ReferenceEquals(c.inventory, capture.Inventory) && ReferenceEquals(c.inventoryController, capture.InventoryController)
                && ReferenceEquals(Controller, capture.InventoryController)
                && ReferenceEquals(c.advancedTraining, capture.Training) && c.stats.rebirthNumber == capture.Rebirth
                && (capture.Snapshot == null || (c.wishes.wishes[190].level > 0) == capture.Snapshot.FreeAdvancedTraining)
                && CombatPrepSupportObjectsMatch(capture)
                && Math.Abs(c.totalPlaytime.totalseconds - c.rebirthTime.totalseconds - capture.Origin) <= 2d
                && c.rebirthTime.totalseconds + .1 >= capture.Elapsed && c.totalPlaytime.totalseconds + .1 >= capture.Total;
        }

        private static bool CombatPrepSupportObjectsMatch(CombatPrepCapture capture)
        {
            try
            {
                var objects = SupportObjects();
                return capture.SupportObjects != null && objects.Length == capture.SupportObjects.Length
                    && !objects.Where((x, i) => !ReferenceEquals(x, capture.SupportObjects[i])).Any();
            }
            catch { return false; }
        }

        private static void VerifyCombatPrepGear(CombatPrepCapture capture, bool originalSlots)
        {
            var owned = new HashSet<Equipment>(Character.inventory.GetConvertedEquips()
                .Concat(Character.inventory.GetConvertedInventory()).Select(x => x.equipment));
            foreach (var pair in capture.Revisions)
                if (!owned.Contains(pair.Key) || CombatPrepGearRevision(pair.Key) != pair.Value)
                    throw new InvalidOperationException("Cached equipment ownership or stats changed; preview again.");
            if (originalSlots)
            {
                var actual = CombatPrepEquipped();
                if (actual.Length != capture.OriginalGear.Length || actual.Where((x, i) => x.slot != capture.OriginalGear[i].slot
                    || !ReferenceEquals(x.equipment, capture.OriginalGear[i].equipment)).Any())
                    throw new InvalidOperationException("Equipped instances changed; preview again.");
            }
        }

        private static CombatPrepCapture CaptureCombatPrep(double backgroundEnergy, double backgroundMagic, CombatPrepCapture cached = null)
        {
            return CaptureCombatPrepCore(backgroundEnergy, backgroundMagic, cached);
        }

        private static CombatPrepCapture CaptureAutomaticCombatPrep(double adventureWeight, int horizon)
        {
            if (adventureWeight == 0) return CaptureCombatPrep(.03, .03);
            if (!LoadoutPresetPlanner.Finite(adventureWeight) || adventureWeight < 0 || adventureWeight > 1000000)
                throw new ArgumentException("Adventure weight must be finite and between zero and 1000000.");
            if (horizon < 1 || horizon > 86400) throw new ArgumentException("Combat prep selection horizon must be between 1 and 86400 seconds.");
            return CaptureCombatPrepCore(.03, .03, null, adventureWeight, horizon);
        }

        private static SupportEvaluation ObserveCombatPrepSupport(SupportContext context, SupportSelection selection = null)
        {
            var result = SupportProjection.Evaluate(context, selection ?? context.CurrentSelection, 1, false);
            // Observation is not permission to activate. Retain Feasible=false for overspending support,
            // accepting its values only after all structural/numeric projection checks have passed.
            if (!result.Feasible && !(result.NetGold < 0 && SupportProjection.Finite(result.NetGold)
                && result.GrossGold >= 0 && SupportProjection.Finite(result.GrossGold)
                && result.Drain >= 0 && SupportProjection.Finite(result.Drain) && SupportProjection.Finite(result.Budget)
                && new[] { result.BeardNGU, result.BeardW, result.BeardStat, result.BeardGold, result.BeardAdv, result.BeardLoot,
                    result.DiggerLoot, result.DiggerW, result.DiggerStat, result.DiggerAdv, result.DiggerEnergyNGU, result.DiggerMagicNGU,
                    result.DiggerEnergyBeard, result.DiggerMagicBeard, result.DiggerPP, result.DiggerDaycare, result.DiggerBlood, result.DiggerEXP }
                    .All(x => SupportProjection.Finite(x) && x >= 1)))
                throw new InvalidOperationException(result.Reason);
            return result;
        }

        private static CombatPrepCapture CaptureCombatPrepCore(double backgroundEnergy, double backgroundMagic,
            CombatPrepCapture cached = null, double adventureWeight = 0, int horizon = 3600)
        {
            RequireCombatPrepContext();
            var c = Character;
            var capture = new CombatPrepCapture {
                OriginalGear = CombatPrepEquipped(), Wandoos = c.wandoos98, Augments = c.augments, Training = c.advancedTraining,
                Inventory = c.inventory, InventoryController = c.inventoryController,
                Rebirth = c.stats.rebirthNumber, Origin = c.totalPlaytime.totalseconds - c.rebirthTime.totalseconds,
                Elapsed = c.rebirthTime.totalseconds, Total = c.totalPlaytime.totalseconds, Material = CombatPrepMaterialKey()
            };
            capture.SupportContext = CaptureSupportContext();
            capture.OriginalSupportState = CaptureSupportState();
            capture.SupportObjects = SupportObjects();
            capture.SupportMaterial = CombatPrepSupportMaterial(capture.SupportContext);
            capture.Gear.Add("current", capture.OriginalGear);
            capture.Support.Add("current", new CombatPrepSupport { ProfileGoal = "Current", BudgetFraction = 1,
                Evaluation = ObserveCombatPrepSupport(capture.SupportContext, capture.OriginalSupportState.Selection) });
            if (adventureWeight > 0)
                CaptureCombatPrepAdventureProfiles(capture, adventureWeight, horizon);
            else if (cached != null)
            {
                capture.UnavailableGearGoals.AddRange(cached.UnavailableGearGoals);
                foreach (var pair in cached.Gear.Where(x => x.Key != "current"))
                {
                    var pinned = cached.Support[pair.Key];
                    var evaluation = SupportProjection.Evaluate(capture.SupportContext, pinned.Evaluation.Selection, pinned.BudgetFraction, pinned.EnforceBudget);
                    if (!evaluation.Feasible) continue;
                    capture.Gear.Add(pair.Key, pair.Value);
                    capture.Support.Add(pair.Key, new CombatPrepSupport { ProfileGoal = pinned.ProfileGoal,
                        BudgetFraction = pinned.BudgetFraction, EnforceBudget = pinned.EnforceBudget, Evaluation = evaluation });
                }
            }
            else
            {
                foreach (var goal in new[] { GearOptimizationGoal.Augments, GearOptimizationGoal.AdvancedTraining,
                    GearOptimizationGoal.Wandoos, GearOptimizationGoal.EnergyNgu })
                {
                    var result = GearOptimizer.Optimize(goal);
                    if (!result.Success)
                    {
                        capture.UnavailableGearGoals.Add(goal.ToString());
                        continue;
                    }
                    var destinations = new List<ih>();
                    var weapon = -5; var accessory = 10000;
                    foreach (var item in result.Items)
                    {
                        var slot = item.equipment.type == part.Head ? -1 : item.equipment.type == part.Chest ? -2
                            : item.equipment.type == part.Legs ? -3 : item.equipment.type == part.Boots ? -4
                            : item.equipment.type == part.Weapon ? weapon-- : accessory++;
                        destinations.Add(new ih { equipment = item.equipment, id = item.id, name = item.name, slot = slot });
                    }
                    var final = LoadoutManager.ComposeExactLoadout(capture.OriginalGear, destinations.ToArray());
                    if (final.Where(x => x.id != 0).GroupBy(x => x.id).Any(x => x.Count() > 1))
                        throw new InvalidOperationException("Projected gear contains conflicting item IDs.");
                    var key = goal == GearOptimizationGoal.Augments ? "augments" : goal == GearOptimizationGoal.AdvancedTraining
                        ? "at" : goal == GearOptimizationGoal.Wandoos ? "wandoos" : "ngu";
                    var profileGoal = goal == GearOptimizationGoal.Augments ? "Augments" : goal == GearOptimizationGoal.AdvancedTraining
                        ? "AT" : goal == GearOptimizationGoal.Wandoos ? "Wandoos" : "NGU";
                    var policy = ActivityPolicy.Current;
                    var intent = policy.BuildSupport(profileGoal == "AT" ? "AT" : profileGoal == "Augments" ? "AUG"
                        : profileGoal == "Wandoos" ? "WAN" : "NGU", profileGoal == "Wandoos" ? "WAN" : "NGU", false, false, false);
                    var priorities = policy.CoreBeards().Concat(intent.Beards)
                        .Concat(backgroundMagic > 0 && profileGoal == "Augments" || (backgroundEnergy > 0 || backgroundMagic > 0) && profileGoal == "NGU"
                            ? new[] { 4 } : new int[0])
                        .Concat(capture.OriginalSupportState.Selection.Beards.OrderBy(x => x == 0 || x == 6 ? 0 : 1))
                        .Concat(policy.DefaultBeardPriority()).Distinct().ToArray();
                    var requests = intent.Diggers.Select(x => new DiggerBudgetRequest { Id = x.Id, Weight = x.Weight,
                        Maximum = capture.SupportContext.Diggers.Single(d => d.Id == x.Id).Maximum }).ToArray();
                    foreach (var planned in new[] { false, true })
                    {
                        var evaluation = planned ? SupportProjection.Plan(capture.SupportContext, priorities, requests, policy.DiggerBudgetFraction)
                            : SupportProjection.Evaluate(capture.SupportContext, capture.OriginalSupportState.Selection, 1, false);
                        if (!evaluation.Feasible) continue;
                        var variant = key + (planned ? "-planned" : "-current");
                        capture.Gear.Add(variant, final);
                        capture.Support.Add(variant, new CombatPrepSupport { ProfileGoal = profileGoal, BudgetFraction = planned ? policy.DiggerBudgetFraction : 1,
                            EnforceBudget = planned, Evaluation = evaluation });
                    }
                }
            }
            var boss = CombatPrepBossStats(capture.OriginalGear, raw: true);
            var s = new CombatPrepSnapshot {
                WandoosAvailable = true, DifficultyKey = c.settings.rebirthDifficulty.ToString(), CurrentOsId = (int) c.wandoos98.os,
                WandoosEnergy = new CombatPrepBar { Level = c.wandoos98.energyLevel, Progress = c.wandoos98.energyProgress },
                WandoosMagic = new CombatPrepBar { Level = c.wandoos98.magicLevel, Progress = c.wandoos98.magicProgress },
                WandoosBootupElapsedSeconds = Math.Max(0, c.wandoos98.bootupTime.totalseconds),
                WandoosBootupDurationSeconds = c.wandoos98Controller.wandoosBootupTime(),
                WandoosBootupCompletionFactor = c.inventory.itemList.wandoosComplete ? (double) 1.1f : 1,
                FreeAdvancedTraining = c.wishes.wishes[190].level > 0,
                WandoosEnergyAllocation = c.wandoos98.wandoosEnergy, WandoosMagicAllocation = c.wandoos98.wandoosMagic,
                Gold = Math.Max(0, c.realGold), NetGoldPerSecond = c.grossGoldPerSecond() - c.totalGPSDrain(),
                CurrentAugmentTotal = c.augmentsController.totalBonus(), CurrentWandoosBonus = c.wandoos98Controller.wandoosBonus(),
                BaselineBossAttack = boss.Item1, BaselineBossDefense = boss.Item2, PhaseSpecificSupport = true,
                BackgroundEnergyFraction = backgroundEnergy, BackgroundMagicFraction = backgroundMagic,
                AllocationRefreshSeconds = CombatPrepPlanner.AllocationRefreshSeconds,
                AtEnergy = CaptureCombatPrepAt(3, c.advancedTrainingController.wandoosEnergy),
                AtMagic = CaptureCombatPrepAt(4, c.advancedTrainingController.wandoosMagic),
                AugmentGlobalFactor = (1 + .25 * c.allChallenges.noAugsChallenge.completions()) * c.NGUController.augmentBonus()
                    / (c.settings.rebirthDifficulty == difficulty.sadistic ? c.augmentsController.sadisticNerfModifier() : 1),
                CaptureAssumptions = (capture.WeightedProfiles
                    ? "Each available phase uses one jointly weighted gear/support setup (primary 100 plus adventure "
                        + adventureWeight.ToString(CultureInfo.InvariantCulture) + "). Current is retained only for reporting/recovery. "
                        + "Setup ranking uses the nominal horizon, fixed captured factors and current reclaimable resources, with full-boot Wandoos and prep-controlled weighted NGU spill ignoring positive menu targets/auto-advance numerically only. "
                        + "This short-horizon/resource-selection approximation is not an executable preset allocation or a joint schedule optimum; the combat forecast still uses the real native bootup ramp. "
                    : "Current and deterministic phase-planned support are compared on the same owned gear; this bounded search is not global support optimization. ")
                    + "Support levels are fixed at capture; passive beard level growth is not forecast. "
                    + "Native bootup ramps on its own clock, including the completed-set bonus; potions and other non-gear factors stay fixed. "
                    + "Boss estimates use the original equipped loadout and current support as one fixed final setup, not optimized fight gear. "
                    + "Forecasts use each profile's current-available/cap pool after basic training reservation, with no future refill credit; only used loadouts deplete forecast pools. Execution uses observed live resources, then spills capped excess into weighted NGUs and Time Machine. "
                    + "Each profile uses its exact shared support net GPS, without future growth. Phase gear/support setup and polling consume the original wall-clock budget; late augment AND AT targets gate advancement. "
                    + "Allocations are held between 0.5-second refreshes, matching the planner cadence. "
                    + "Selected training menu targets are temporarily unbounded and restored only if still owned; no saved settings or plans change. "
                    + "The installed native upgrade amount overload ignores its argument; the existing read-only verified-IL helper substitutes sample energy into the actual training formula."
            };
            if (capture.UnavailableGearGoals.Count > 0)
                s.CaptureAssumptions += " Optional gear profiles unavailable: " + string.Join(", ", capture.UnavailableGearGoals.ToArray()) + ".";
            s.Augments = c.augmentsController.augments.Select((a, i) => {
                var model = c.augments.augs[i];
                return new CombatPrepAugmentTier { Id = i, Name = a.augName, BaseBoost = a.baseBoost, TierExponent = a.augTierBonus(),
                    Augment = CaptureCombatPrepAugLane(a, false, model.augLevel, model.augProgress),
                    Upgrade = CaptureCombatPrepAugLane(a, true, model.upgradeLevel, model.upgradeProgress) };
            }).ToArray();
            // Do not divide the clamped total to infer global factors. The engine independently checks getter parity.
            s.OperatingSystems = Enumerable.Range(0, 3).Select(id => CaptureCombatPrepOs(id, s.DifficultyKey)).ToArray();
            var currentOs = s.OperatingSystems.Single(x => x.Id == s.CurrentOsId);
            if (!CombatPrepNear(currentOs.EnergyBaseTime, c.wandoos98Controller.baseEnergyTime())
                || !CombatPrepNear(currentOs.MagicBaseTime, c.wandoos98Controller.baseMagicTime()))
                throw new InvalidOperationException("Wandoos base formula differs from inspected game IL.");
            var profiles = capture.Gear.Select(pair => CaptureCombatPrepProfile(pair.Key, pair.Value, capture.Support[pair.Key], s)).ToList();
            var basic = c.training.attackEnergy.Concat(c.training.defenseEnergy).Aggregate(0m, (sum, x) => sum + Math.Max(0, x));
            var e = (long) Math.Max(0m, (decimal) Math.Max(c.curEnergy, c.idleEnergy) - basic);
            var m = Math.Max(0, Math.Max(c.magic.curMagic, c.magic.idleMagic));
            foreach (var p in profiles.ToArray())
            {
                if (p.EnergyCap < basic)
                {
                    if (p.Key == "current") throw new InvalidOperationException("Current energy cap cannot hold the unrecallable basic training allocation.");
                    profiles.Remove(p); capture.Gear.Remove(p.Key); capture.Support.Remove(p.Key);
                    if (capture.WeightedProfiles)
                    {
                        capture.AdventureRoutes.Remove(p.Key); capture.AdventureSnapshots.Remove(p.Key);
                        capture.UnavailableGearGoals.Add(p.Goal + ": energy capacity is below retained basic training.");
                    }
                    continue;
                }
                p.EnergyTotal = Math.Min(e, (long) ((decimal) p.EnergyCap - basic));
                p.MagicTotal = Math.Min(m, p.MagicCap);
            }
            foreach (var item in capture.Gear.Values.SelectMany(x => x).Where(x => x.id != 0))
                capture.Revisions[item.equipment] = CombatPrepGearRevision(item.equipment);
            s.Current = profiles.Single(x => x.Key == "current");
            s.Profiles = profiles.Where(x => x.Key != "current").ToArray();
            s.RequirePhaseProfiles = capture.WeightedProfiles;
            if (capture.WeightedProfiles && s.Profiles.Length == 0)
                throw new InvalidOperationException("No usable weighted combat prep profiles: " + string.Join("; ", capture.UnavailableGearGoals.ToArray()));
            s.ReferenceWandoosProfileKey = capture.Gear.ContainsKey("wandoos-planned") ? "wandoos-planned"
                : capture.Gear.ContainsKey("wandoos-current") ? "wandoos-current" : "current";
            capture.Snapshot = s;
            return capture;
        }

        private static CombatPrepAtLane CaptureCombatPrepAt(int id, AdvancedTrainingController controller)
        {
            var c = Character;
            return new CombatPrepAtLane { Unlocked = c.advancedTrainingController.advancedTrainingUnlocked()
                    && (c.wishes.wishes[190].level > 0 || c.buttons.advancedTraining.interactable && c.advancedTraining.levelTarget[id] >= 0),
                Level = c.advancedTraining.level[id], Progress = c.advancedTraining.barProgress[id],
                TargetLevel = Math.Max(0, c.advancedTraining.levelTarget[id]), BaseTime = controller.baseTime, LevelFactor = controller.levelFactor };
        }

        private static CombatPrepAugmentLane CaptureCombatPrepAugLane(AugmentController controller, bool upgrade, long level, double progress)
        {
            // The native upgrade(long) IL reads totalCapEnergy(), NOT its argument. The existing helper matches
            // the actual upgrade() training getter's float/double formula while substituting amount read-only.
            long sample = 1;
            var rate = CombatPrepAugProgress(controller, upgrade, sample);
            while (rate < .0001 && sample <= long.MaxValue / 100)
            {
                sample *= 100;
                rate = CombatPrepAugProgress(controller, upgrade, sample);
            }
            if (rate >= float.MaxValue * .5 || double.IsNaN(rate) || double.IsInfinity(rate))
                throw new InvalidOperationException("Augment sample hit the game float clamp.");
            return new CombatPrepAugmentLane { Unlocked = Character.buttons.augmentation.interactable
                    && !(upgrade ? controller.upgradeLocked() : controller.augLocked()), Level = level, Progress = progress, Paid = progress > 0,
                ProgressCoefficient = rate / sample / Character.totalEnergyPower() * ((double) level + 1),
                CostCoefficient = (double) (upgrade ? controller.getBaseUpgradeCost() : controller.getBaseAugCost()) * Character.augDiscount(),
                LevelExponent = 1, CostExponent = upgrade ? 2 : 1 };
        }

        private static double CombatPrepAugProgress(AugmentController controller, bool upgrade, long amount)
        {
            return upgrade ? Extensions.getUpgradeProgressPerTick(controller, amount) : controller.getAugProgressPerTick(amount);
        }

        private static CombatPrepOs CaptureCombatPrepOs(int id, string difficultyKey)
        {
            // Assembly-CSharp IL: base{Energy,Magic}Time, OSLimit (constant 400), OSFactor and wandoosBonus.
            // No temporary OS change. All three OSes use the same OSFactor with the inspected fixed OSLimit.
            var normal = Character.settings.rebirthDifficulty == difficulty.normal;
            var bases = normal ? new[] { 1e9f, 1e12f, 1e15f } : new[] { 1e21f, 1e27f, 1e33f };
            var exponent = id == 0 ? .8 : id == 1 ? 1 : (double) 1.05f;
            var eOffset = id == 0 ? 100 : id == 1 ? 5 : 1d / 6;
            var mOffset = id == 0 ? 25 : id == 1 ? .5 : 1d / 40;
            if (Character.wandoos98Controller.OSLimit() != 400)
                throw new InvalidOperationException("Wandoos OS limit differs from inspected game IL.");
            return new CombatPrepOs { Id = id, Name = id == 0 ? "98" : id == 1 ? "MEH" : "XL", DifficultyKey = difficultyKey,
                Unlocked = id == 0 || id == 1 && Character.inventory.itemList.jakeComplete || id == 2 && Character.wandoos98.XLLevels > 0,
                EnergyBaseTime = bases[id], MagicBaseTime = bases[id], BonusOffset = 0,
                BonusCoefficient = Math.Pow(1 / eOffset, exponent) * Math.Pow(1 / mOffset, exponent),
                EnergyLevelOffset = eOffset, MagicLevelOffset = mOffset, EnergyExponent = exponent, MagicExponent = exponent };
        }

        private static Dictionary<specType, float> CombatPrepGearBonuses(ih[] gear)
        {
            var result = new Dictionary<specType, float>();
            foreach (specType type in Enum.GetValues(typeof(specType)))
            {
                // Match InventoryController.specBonus: accumulate raw points first, then convert once per type.
                Func<int, float> bonus = slot => {
                    var item = gear.FirstOrDefault(x => x.slot == slot);
                    return item == null || item.id == 0 ? 0f : Controller.equipSpecBonus(type, item.equipment);
                };
                var value = bonus(-5) + bonus(-1) + bonus(-2) + bonus(-3) + bonus(-4) + bonus(-6) * Controller.weapon2Factor();
                foreach (var item in gear.Where(x => x.slot >= 10000).OrderBy(x => x.slot)) value += bonus(item.slot);
                result[type] = Controller.getBonusFactor(value, type);
            }
            return result;
        }

        private static CombatPrepProfile CaptureCombatPrepProfile(string key, ih[] gear, CombatPrepSupport support, CombatPrepSnapshot snapshot)
        {
            var c = Character;
            var b = CombatPrepGearBonuses(gear);
            var resourceBonuses = ResourceProjectionCapture.GetBonuses(gear);
            Func<Dictionary<specType, float>, specType[], float> factor = (bonuses, types) => {
                var total = 1f; foreach (var type in types) total += bonuses[type]; return total;
            };
            var atTypes = new[] { specType.AdvTraining, specType.AdvTraining2 };
            var original = c.inventoryController.bonuses;
            var wEnergy = CombatPrepWandoosSpeedWithoutAtOrBootup(true, b, support.Evaluation);
            var wMagic = CombatPrepWandoosSpeedWithoutAtOrBootup(false, b, support.Evaluation);
            if (key == "current" && (!CombatPrepNear(wEnergy * (1 + snapshot.AtEnergy.LevelFactor * snapshot.AtEnergy.Level)
                    * c.wandoos98Controller.bootupSpeedFactor(), c.totalWandoosEnergySpeed())
                || !CombatPrepNear(wMagic * (1 + snapshot.AtMagic.LevelFactor * snapshot.AtMagic.Level)
                    * c.wandoos98Controller.bootupSpeedFactor(), c.totalWandoosMagicSpeed())))
                throw new InvalidOperationException("Read-only native Wandoos speed formula parity failed; preview refused.");
            var powerE = ResourceProjectionCapture.Create(c, true, false).Project(resourceBonuses);
            var powerM = ResourceProjectionCapture.Create(c, false, false).Project(resourceBonuses);
            var capE = CombatPrepAmount(ResourceProjectionCapture.Create(c, true, true).Project(resourceBonuses));
            var capM = CombatPrepAmount(ResourceProjectionCapture.Create(c, false, true).Project(resourceBonuses));
            if (key == "current" && (!CombatPrepNear(powerE, c.totalEnergyPower()) || !CombatPrepNear(powerM, c.totalMagicPower())
                || !CombatPrepNear(capE, c.totalCapEnergy()) || !CombatPrepNear(capM, c.totalCapMagic())))
                throw new InvalidOperationException("Read-only projected E/M formula parity failed; preview refused.");
            return new CombatPrepProfile { Key = key, Goal = support.ProfileGoal, SupportKey = support.Evaluation.Selection.StableKey,
                NetGoldPerSecond = support.Evaluation.NetGold,
                OwnedGearRefs = gear.Where(x => x.id != 0).Select(x => x.slot.ToString(CultureInfo.InvariantCulture) + "/" + x.id).ToArray(),
                EnergyCap = capE, MagicCap = capM, EnergyPower = powerE, MagicPower = powerM,
                AtSpeed = c.totalAdvancedTrainingSpeedBonus() / (double) factor(original, atTypes) * factor(b, atTypes),
                AugmentSpeedFactor = (1f + b[specType.Augs]) / (double) (1f + original[specType.Augs]),
                WandoosEnergySpeedWithoutAt = wEnergy, WandoosMagicSpeedWithoutAt = wMagic };
        }

        private static double CombatPrepWandoosSpeedWithoutAtOrBootup(bool energy, Dictionary<specType, float> bonuses, SupportEvaluation support)
        {
            // Character.totalWandoos{Energy,Magic}Speed IL, omitting ONLY AT and bootup.
            // Reconstruct instead of dividing the live speed: a fresh boot has speed zero.
            var c = Character;
            var speed = c.wandoos98Controller.OSFactor() * (double) (1f + bonuses[specType.Wandoos98] + bonuses[specType.Wandoos2]);
            speed *= c.allChallenges.wandoosBonus() * support.BeardW;
            if (c.settings.rebirthDifficulty >= difficulty.sadistic) speed /= c.wandoos98Controller.sadisticModifier();
            speed *= c.NGUController.wandoosBonus() * support.DiggerW;
            speed *= c.inventory.macguffinBonuses[energy ? 15 : 16];
            speed *= energy ? c.beastQuestPerkController.totalEnergyWandoosBonus() : c.beastQuestPerkController.totalMagicWandoosBonus();
            return speed * c.cardsController.getBonus(cardBonus.wandoosSpeed);
        }

        private static Tuple<double, double> CombatPrepBossStats(ih[] finalGear, bool raw, SupportSelection reportSupport = null)
        {
            var c = Character;
            SupportEvaluation support = null;
            if (reportSupport != null)
                support = ObserveCombatPrepSupport(CaptureSupportContext(), reportSupport);
            Func<bool, float> gearBonus = attack => {
                Func<int, float> bonus = slot => {
                    var item = finalGear.FirstOrDefault(x => x.slot == slot);
                    return item == null || item.id == 0 ? 0 : attack ? Controller.equipAttackBonus(item.equipment) : Controller.equipDefenseBonus(item.equipment);
                };
                var sum = bonus(-5) + bonus(-1) + bonus(-2) + bonus(-3) + bonus(-4) + bonus(-6) * Controller.weapon2Factor();
                foreach (var item in finalGear.Where(x => x.slot >= 10000).OrderBy(x => x.slot)) sum += bonus(item.slot);
                return sum;
            };
            // Character.totalAttack/totalDefense IL, substituting ONLY the fixed final gear's additive stat bonus.
            Func<bool, double> total = attack => {
                var value = 100 + (attack ? c.training.getTotalAttack() * c.attackMulti : c.training.getTotalDefense() * c.defenseMulti)
                    * c.adventureController.itopod.totalStatBonus() * (double) (1f + gearBonus(attack) / 100f) * (attack ? c.attackBoost : c.defenseBoost);
                value /= c.difficultyModifier();
                value *= c.augmentsController.totalBonus() * c.wandoos98Controller.wandoosBonus();
                value *= (1 + c.yggdrasil.totalStatBonus()) * c.NGUController.statBonus() * c.yggdrasilController.permStatBonus() * c.yggdrasilController.permStatBonus2();
                value *= support == null ? c.allBeards.statBonus() * c.allDiggers.totalStatBonus() : support.BeardStat * support.DiggerStat;
                value *= c.beastQuestPerkController.totalStatBonus(); value *= c.hacksController.totalStatBonus();
                value *= c.wishesController.totalStatBonus(); value *= c.cardsController.getBonus(cardBonus.atkDefStats);
                value *= c.inventory.macguffinBonuses[13];
                return raw ? value : Math.Max(100, value);
            };
            return Tuple.Create(total(true), total(false));
        }

        private static bool CombatPrepNear(double a, double b) { return Math.Abs(a - b) / Math.Max(1, Math.Abs(b)) <= 1e-6; }
    }
}
