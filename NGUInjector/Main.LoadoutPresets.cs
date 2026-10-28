using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private sealed class LoadoutPresetCapture
        {
            internal int Horizon = 3600;
            internal CustomPlanStage Stage;
            internal AdvancedTraining NativeAt;
            internal long[] NativeAtTargets, NativeAtTargetArray;
            internal PresetSnapshot Snapshot;
            internal Dictionary<int, ih> Gear;
            internal DateTime CapturedAt = DateTime.UtcNow;
            internal List<string> Warnings = new List<string>();
            internal Dictionary<int, Equipment> Instances;
            internal object[] Epoch;
            internal string Material;
            internal CustomLoadoutPreset ExecutionPreset;
            internal bool ResourceSetupOnlyObservation;
            internal string AdventureTaskKey, AdventureTask;
            internal Func<bool> SelectedRouteAuthorized;
            internal long Rebirth;
            internal double Elapsed, Total;
        }
        private sealed class LoadoutPresetPreview
        {
            internal PresetPreviewIdentity Identity;
            internal LoadoutPresetCapture Capture;
            internal PresetPlan Plan;
            internal CustomLoadoutPreset Preset;
        }
        private LoadoutPresetPreview _loadoutPresetPreview;
        private JSONObject _loadoutPresetSnapshot;
        private static bool _presetResourcePresentationPending;
        private static ResourceMutationResult _presetResourceOutcome;
        private static Character _presetResourcePresentationCharacter;
        private ResourceApplication _presetResources;
        // Diagnostic work counters, not authority or acquired policy. Count captures at
        // their actual boundary so regression checks detect broad-capture regressions.
        private static long _presetPlanningCaptures, _presetExecutionObservations;
        private static long _presetRouteTableEnumerations, _presetFloorForecastRows;

        private static double LoadoutPresetClock => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        private static void LoadoutPresetArgs(JSONNode args, bool apply)
        {
            var allowed = apply ? new[] { "preset", "previewId", "candidateKey" } : new[] { "preset" };
            if (args == null || !args.IsObject || args.Linq.Any(x => !allowed.Contains(x.Key)))
                throw new FormatException("Unexpected loadout preset arguments.");
            if (apply && (!args["previewId"].IsString || !PresetPreviewIdentity.IsToken(args["previewId"].Value)
                || !args["candidateKey"].IsString || args["candidateKey"].Value.Length > 64))
                throw new FormatException("Apply requires a 32-character lowercase hexadecimal previewId and candidateKey.");
        }

        private static string LoadoutPresetCanonical(JSONNode node, int depth = 0)
        {
            if (depth > 64) throw new FormatException("Preset definition nesting exceeds 64 levels.");
            if (node.IsObject) return "{" + string.Join(",", node.Linq.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new JSONString(x.Key).ToString() + ":" + LoadoutPresetCanonical(x.Value, depth + 1)).ToArray()) + "}";
            if (node.IsArray) return "[" + string.Join(",", node.Children.Select(x => LoadoutPresetCanonical(x, depth + 1)).ToArray()) + "]";
            return node.ToString();
        }

        private static string LoadoutPresetDefinition(CustomLoadoutPreset preset)
        {
            var definition = LoadoutPresetCanonical(preset.ToJson());
            if (Encoding.UTF8.GetByteCount(definition) > 32 * 1024) throw new FormatException("Preset definition exceeds the 32 KiB preview cache bound.");
            return definition;
        }

        private static PresetRequest LoadoutPresetRequest(CustomLoadoutPreset p, int horizon = 3600)
        {
            CustomLoadoutPreset.Validate(p);
            if (p.AdventureDynamicWeight > 0) throw new InvalidOperationException("Resolve adventure intent before creating an execution request.");
            var request = new PresetRequest { Weights = p.Weights.ToArray(), Horizon = horizon, Swap = p.SwapSeconds,
                BloodPercent = p.MagicBloodPercent, Zone = p.Zone, PlannedSupport = p.SupportMode == "planned",
                DiggerPercent = p.DiggerBudgetPercent, MinimumPower = p.MinimumPower, MinimumToughness = p.MinimumToughness,
                EnergyPriorities = p.EnergyPriorities?.ToArray(), MagicPriorities = p.MagicPriorities?.ToArray(),
                ResourcePrioritiesReviewed = p.ResourcePrioritiesReviewed,
                BeardPriority = p.BeardPriority.ToArray(), BeardRotation = p.BeardRotation?.ToArray(), DiggerWeights = p.DiggerWeights.ToArray() };
            ConfigurePresetActivities(request, p);
            var owner = reference;
            var plan = Settings?.UseCustomRebirthPlan == true ? owner?.LoadRuntimeCustomPlan() : null;
            if (request.ResourcePrioritiesReviewed && (IronPillBloodReserved || CustomBloodGoalCatchUpRequested(plan)))
            {
                // A transient request, never a silent migration of an unreviewed saved preset.
                request.MagicPriorities = new[] { "RIT" }.Concat(request.MagicPriorities.Where(x => x != "RIT")).ToArray();
            }
            return request;
        }

        private static IEnumerable<PresetCandidate> LoadoutPresetSetupCandidates(IEnumerable<PresetCandidate> candidates, bool reviewed)
        {
            // Reviewed funding cannot select replacement gear, even when the best setup cannot be funded.
            return reviewed ? candidates.Where(x => x.SetupScore.HasValue).OrderByDescending(x => x.SetupScore.Value).Take(1)
                : candidates.Where(x => x.Feasible && x.Score.HasValue).OrderByDescending(x => x.Score.Value);
        }

        private static CustomLoadoutPreset ResolveAdventurePreset(CustomLoadoutPreset preset, out string key, out string label)
        {
            key = label = null;
            if (preset.AdventureDynamicWeight <= 0) return preset;
            ObserveAdventurePresetTask(out var mode, out var zone, out key, out label);
            if (mode == CustomAdventureModeFarmZone && ZoneHelpers.ZoneIsTitan(zone))
                throw new InvalidOperationException("Adventure-dynamic is deferred while a titan target is selected; use the bounded titan owner.");
            return preset.ResolveAdventure(mode, zone);
        }

        private static void ObserveAdventurePresetTask(out int mode, out int zone, out string key, out string label)
        {
            ResolveConfiguredAdventureGoal(out mode, out zone, out var ruleIndex);
            if (mode == CustomAdventureModeFarmZone && (zone == 1000 || zone < 0 || !CombatManager.IsZoneUnlocked(zone)))
                mode = CustomAdventureModeOptimalItopod;
            if (mode != CustomAdventureModeFarmZone && mode != CustomAdventureModeManualZone) zone = -1;
            key = mode + ":" + zone + ":" + ruleIndex + ":" + Settings.CustomAdventureMode + ":" + Settings.CustomAdventureZone
                + ":" + AdventureRule.ToJson(Settings.CustomAdventureRules).ToString();
            label = mode == CustomAdventureModeFarmBoosts ? "Boost farming"
                : mode == CustomAdventureModeFarmZone ? "Zone farming: " + ZoneName(zone)
                : "ITOPOD";
            if (ruleIndex >= 0) label = "Rule " + (ruleIndex + 1) + ": " + label;
        }

        private static void RequireLoadoutPresetContext()
        {
            var c = Character;
            if (c?.inventory == null || Controller == null || c.NGUController == null || c.allDiggers == null || c.allBeards == null
                || c.adventureController == null || c.adventure == null || c.NGU == null || c.magic == null || c.training == null || c.beards == null || c.diggers == null
                || c.stats == null || c.arbitrary == null || c.rebirthTime == null || c.totalPlaytime == null || c.inventory.disabled)
                throw new InvalidOperationException("Loadout preset game data or equipment is unavailable.");
            if (c.inventory.inventory.Count > 1024 || c.inventory.accs.Count > 64)
                throw new InvalidOperationException("Inventory exceeds the bounded preset preview capture (1024 inventory / 64 accessory slots).");
            foreach (var field in c.challenges.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var challenge = field.GetValue(c.challenges);
                var active = challenge?.GetType().GetField("inChallenge");
                if (active != null && (bool)active.GetValue(challenge))
                    throw new InvalidOperationException("Weighted preset forecasts are unavailable during challenges.");
            }
        }

        private static void LoadoutPresetDeadline(Stopwatch clock)
        { if (clock.ElapsedMilliseconds >= 3000) throw new InvalidOperationException("Preset operation exceeded its time budget; preview again."); }

        private static LoadoutPresetCapture CaptureLoadoutPreset(Stopwatch clock = null)
        {
            return CaptureLoadoutPresetDefinition(null, clock);
        }

        private static LoadoutPresetCapture CaptureLoadoutPresetDefinition(CustomLoadoutPreset definition, Stopwatch clock = null, CustomPlanStage stage = null, bool enforceDeadline = true)
        {
            return CaptureLoadoutPresetDefinitionCore(definition, clock, stage, false, enforceDeadline);
        }

        private static LoadoutPresetCapture CaptureCombatPrepLoadoutDefinition(CustomLoadoutPreset definition)
        {
            var capture = CaptureLoadoutPresetDefinitionCore(definition, null, null, true);
            PrepareCombatPrepSelectionSnapshot(capture);
            return capture;
        }

        private static LoadoutPresetCapture CaptureLoadoutPresetDefinitionCore(CustomLoadoutPreset definition, Stopwatch clock,
            CustomPlanStage stage, bool prepSelection, bool enforceDeadline = true)
        {
            return CaptureLoadoutPresetObservation(definition, clock, stage, prepSelection, enforceDeadline, false);
        }

        private static LoadoutPresetCapture CaptureLoadoutPresetExecution(CustomLoadoutPreset definition, Stopwatch clock,
            CustomPlanStage stage, bool enforceDeadline = true)
        {
            return CaptureLoadoutPresetRouteExecution(definition, clock, stage, null, enforceDeadline);
        }

        private static LoadoutPresetCapture CaptureLoadoutPresetRouteExecution(CustomLoadoutPreset definition, Stopwatch clock,
            CustomPlanStage stage, PresetCandidate acquiredRoute, bool enforceDeadline = true)
        {
            if (definition.Weights.Skip(3).Take(3).Any(x => x > 0) && acquiredRoute == null)
                throw new InvalidOperationException("Farming execution observation requires an acquired route; no replacement was searched.");
            return CaptureLoadoutPresetObservation(definition, clock, stage, false, enforceDeadline, true, acquiredRoute);
        }

        private static LoadoutPresetCapture CaptureLoadoutPresetObservation(CustomLoadoutPreset definition, Stopwatch clock,
            CustomPlanStage stage, bool prepSelection, bool enforceDeadline, bool executionOnly, PresetCandidate acquiredRoute = null)
        {
            clock = clock ?? Stopwatch.StartNew();
            RequireLoadoutPresetContext();
            // Native active-zone getters used by capture need these controllers
            // before selected-setup preflight can run. Safe/resource-only worlds
            // do not acquire a route dependency merely by observing their setup.
            if (Character.adventure.zone != -1 && (Character.adventureController.zoneSelector == null
                || Character.adventureController.playerController == null || Character.adventureController.enemyAI == null))
                throw new InvalidOperationException("Selected setup route retirement controllers are unavailable.");
            if (executionOnly) _presetExecutionObservations++; else _presetPlanningCaptures++;
            var energyWeights = Settings.EnergyNguWeights;
            var magicWeights = Settings.MagicNguWeights;
            var c = Character;
            var capture = new LoadoutPresetCapture { Snapshot = new PresetSnapshot(), Stage = CloneGlobalPresetStage(stage),
                ResourceSetupOnlyObservation = executionOnly && !definition.Weights.Skip(3).Take(3).Any(x => x > 0) };
            capture.ExecutionPreset = definition == null ? null : ResolveAdventurePreset(definition, out capture.AdventureTaskKey, out capture.AdventureTask);
            definition = capture.ExecutionPreset;
            capture.NativeAt = c.advancedTraining;
            capture.NativeAtTargetArray = c.advancedTraining?.levelTarget;
            capture.NativeAtTargets = capture.NativeAtTargetArray?.ToArray();
            var s = capture.Snapshot;
            s.ObservedCurrentPools = true;
            capture.Gear = executionOnly ? GearOptimizer.CaptureEquippedPresetGear(s) : GearOptimizer.CapturePresetGear(s);
            if (enforceDeadline) LoadoutPresetDeadline(clock);
            var basic = c.training.attackEnergy.Concat(c.training.defenseEnergy).Aggregate(0m, (sum, x) => sum + Math.Max(0, x));
            s.Energy = (long)Math.Max(0m, (decimal)Math.Max(c.curEnergy, c.idleEnergy) - basic);
            s.ReservedEnergy = (long)Math.Min(long.MaxValue, basic);
            s.Magic = Math.Max(c.magic.curMagic, c.magic.idleMagic);
            s.Energy = reference?.ExecutionResourceBudget(true, s.Energy) ?? s.Energy;
            s.Magic = reference?.ExecutionResourceBudget(false, s.Magic) ?? s.Magic;
            s.EnergyCap = c.totalCapEnergy(); s.MagicCap = c.totalCapMagic();
            s.Available[12] = true;
            s.Available[13] = c.magic.capMagic >= 10000; // Native ButtonShower magic-input unlock.
            s.Reasons[13] = "Magic is locked.";
            s.EnergyPower = c.totalEnergyPower(); s.MagicPower = c.totalMagicPower();
            s.EnergyCapacity = ResourceProjectionCapture.Create(c, true, true); s.MagicCapacity = ResourceProjectionCapture.Create(c, false, true);
            s.EnergyPowerProjection = ResourceProjectionCapture.Create(c, true, false); s.MagicPowerProjection = ResourceProjectionCapture.Create(c, false, false);
            var adventureStats = new[] { c.totalAdvAttack(), c.totalAdvDefense() };
            s.Power = adventureStats[0]; s.Toughness = adventureStats[1];
            s.Available[10] = s.Available[11] = c.buttons.adventure.interactable;
            s.Reasons[10] = s.Reasons[11] = "Adventure is locked.";
            s.BasePower = c.adventure.attack; s.BaseToughness = c.adventure.defense;
            s.CubePower = c.inventory.cubePower; s.CubeToughness = c.inventory.cubeToughness;
            s.PowerFactors = CaptureLoadoutPresetAdventure(true); s.ToughnessFactors = CaptureLoadoutPresetAdventure(false);
            s.NguFactors = new[] { c.inventory.itemList.numberComplete ? 1.1f : 1f, c.inventory.itemList.metaComplete ? 1.2f : 1f,
                c.inventory.itemList.schoolComplete ? 1.15f : 1f, c.adventureController.itopod.totalBothNGUSpeedBonus(), c.allChallenges.nguBonus(), c.allBeards.nguBonus() };
            s.BeastMode = c.adventure.beastModeOn;
            s.Drop = c.lootFactor(); // Includes the active loot charm already. Do not multiply it a second time.
            s.DropGearBase = 100 * (1 + Controller.cubeLootBonus());
            s.Respawn = c.adventureController.respawnTime(); s.AttackSeconds = c.adventure.attackSpeed;
            s.IdleModifier = c.idleAttackPower(); s.GrossGold = Math.Max(0, c.grossGoldPerSecond()); s.Gold = Math.Max(0, c.realGold);
            CaptureLoadoutPresetSupport(s);
            CaptureLoadoutPresetNgu(s, energyWeights, true);
            CaptureLoadoutPresetNgu(s, magicWeights, false);
            s.NguAutoAdvance = !prepSelection && c.NGU.autoAdvance;
            // Prep owns weighted NGU spill, not native menu transfers. Snapshot-only setup ranking.
            if (prepSelection)
                foreach (var row in s.EnergyNgu.Concat(s.MagicNgu)) if (row.Target > 0) row.Target = 0;
            // Cap-only setups can still fund reviewed NGU fallbacks with finite native targets.
            if (!LoadoutPresetPlanner.CapacityOnly(definition?.Weights) || definition?.ResourcePrioritiesReviewed == true
                && (definition.EnergyPriorities.Contains("NGU") || definition.MagicPriorities.Contains("NGU")))
                LoadoutPresetPlanner.RequireSupportedNguAdvance(s);
            var canLevel = c.canLevel();
            s.Available[0] = s.Available[1] = c.buttons.ngu.interactable && canLevel;
            s.Reasons[0] = s.Reasons[1] = canLevel ? "NGU locked." : "Native level gain is currently disabled.";
            s.Available[2] = c.buttons.bloodMagic.interactable;
            s.Reasons[2] = "Blood Magic locked.";
            if (s.Available[2])
            {
                var rituals = new List<PresetRitual>();
                var count = Math.Min(c.bloodMagicController.ritualsUnlocked(), Math.Min(c.bloodMagic.ritual.Count, c.bloodMagicController.bloodMagics.Length));
                for (var i = 0; i < count; i++)
                {
                    var ctrl = c.bloodMagicController.bloodMagics[i]; var model = c.bloodMagic.ritual[i];
                    var divider = c.settings.rebirthDifficulty == difficulty.normal ? c.bloodMagicController.normalSpeedDividers[i] * 50000d
                        : c.settings.rebirthDifficulty == difficulty.evil ? c.bloodMagicController.evilSpeedDividers[i] * 50000d
                        : c.bloodMagicController.sadisticSpeedDividers[i] * (double)ctrl.sadisticDivider();
                    rituals.Add(new PresetRitual { Id = i, Coefficient = ctrl.totalBloodMagicSpeedBonus() / divider, Blood = ctrl.bloodAdded(),
                        Cost = ctrl.currentCost(), Unlocked = c.bossID > model.boss, Paid = model.progress > 0 });
                }
                s.Rituals = rituals.ToArray();
            }
            s.LazyItopodPurchased = c.arbitrary.boughtLazyITOPOD; s.LazyItopodEnabled = c.arbitrary.lazyITOPODOn;
            if (s.LazyItopodActive) capture.Warnings.Add("Lazy ITOPOD shifter is purchased and enabled: ITOPOD forecasts are restricted to its native selected floor, which is recalculated after kills. Fixed multihit/boost floors that differ are not sustainable. No shifter preference is changed; later stat changes may move the native floor.");
            var unlocked = c.inventory.itemList.itopodKeyComplete || c.adventure.highestItopodLevel > 0 || c.adventure.itopod.enemiesKilled > 0;
            if (!executionOnly && unlocked)
            {
                var maximum = Math.Max(0, Math.Min(10000, Math.Min(c.adventureController.maxItopodLevel(), c.adventure.highestItopodLevel - 1)));
                // Native start-input validation clamps to highest reached minus one. Capture repeatable floors only.
                s.PpPerKill = Enumerable.Range(0, maximum + 1).Select(f => {
                    _presetFloorForecastRows++;
                    if (enforceDeadline && f % 64 == 0) LoadoutPresetDeadline(clock);
                    return (double)c.adventureController.itopod.progressGained(f);
                }).ToArray();
                var enemies = c.adventureController.itopodEnemyList;
                if (enemies != null && enemies.Count > 0 && enemies.All(e => e.maxHP > 0 && e.defense >= 0
                    && LoadoutPresetPlanner.Finite(e.maxHP) && LoadoutPresetPlanner.Finite(e.defense)))
                {
                    // powerUp uses float Pow(1.05f, floor) and independent [0.98,1.02] enemy rolls.
                    s.ItopodHp = Enumerable.Range(0, maximum + 1).Select(f => (double)(float)((float)(enemies.Max(e => e.maxHP)
                        * (float)Math.Pow(1.05f, f)) * 1.02f)).ToArray();
                    s.ItopodDefense = Enumerable.Range(0, maximum + 1).Select(f => (double)(float)((float)(enemies.Max(e => e.defense)
                        * (float)Math.Pow(1.05f, f)) * 1.02f)).ToArray();
                }
                else capture.Warnings.Add("Native ITOPOD enemy stats unavailable; floor comparison is limited to the existing one-hit heuristic range, with no invented multihit multipliers.");
                s.Available[3] = maximum >= 1;
                if (!s.Available[3]) unlocked = false;
            }
            s.Reasons[3] = "ITOPOD locked or no repeatable floor unlocked.";
            if (!executionOnly) CaptureLoadoutPresetFarming(capture, unlocked);
            else if (!capture.ResourceSetupOnlyObservation)
            {
                CaptureAcquiredPresetRoute(capture, acquiredRoute, unlocked);
                var repeatable = unlocked && Math.Min(c.adventureController.maxItopodLevel(), c.adventure.highestItopodLevel - 1) >= 1;
                s.Available[3] = repeatable && (acquiredRoute.Zone != 1000 || s.PpPerKill != null);
                s.Available[5] = PresetRouteRecommendation(acquiredRoute.RegularEligibilityZone) != null;
                s.Available[4] = s.FarmingEconomicsCaptured && (repeatable || PresetRouteRecommendation(acquiredRoute.BoostEligibilityZone) != null
                    && BoostFarmHelper.PresetZoneRow(acquiredRoute.BoostEligibilityZone) != null);
                // A lost acquired witness is not permission to search for another one.
                if (s.FarmingEconomicsCaptured) s.Reasons[4] = "Acquired boost eligibility is unavailable; acquire again.";
                s.Reasons[5] = "Acquired regular-zone eligibility is unavailable; acquire again.";
            }
            if (!c.buttons.adventure.interactable)
            {
                for (var i = 3; i < 6; i++) { s.Available[i] = false; s.Reasons[i] = "Adventure is locked."; }
                s.Zones = new PresetZone[0]; s.ItopodBoostPerKill = null;
            }
            if (s.BeastMode)
                for (var i = 3; i < 6; i++) { s.Available[i] = false; s.Reasons[i] = "Farming is unavailable while Beast Mode is on; turn it off and preview again."; }
            if (energyWeights.All(x => x == 0)) capture.Warnings.Add("All energy NGU targets excluded by zero weights.");
            if (magicWeights.All(x => x == 0)) capture.Warnings.Add("All magic NGU targets excluded by zero weights.");
            // Keep the acquired OS even when optional WAN capture is locked or leveling is paused.
            if (c.wandoos98 != null) s.WandoosOs = (int)c.wandoos98.os;
            if (definition != null) CapturePresetActivities(s, definition);
            if (capture.Stage != null)
            {
                ApplyPresetStageTargets(s, capture.Stage);
                if (c.advancedTraining.autoAdvance && s.RequiredATTargets.Select((target, i) => target > 0 && s.AdvancedTraining[i].Level < target).Any(x => x))
                    throw new InvalidOperationException("Disable native AT auto-advance before executing mandatory weighted AT targets; automatic transfers are not part of the acquired policy. No preference was changed.");
            }
            else if (!prepSelection) CaptureUnownedGlobalPresetTargets(s);
            var totals = LoadoutPresetPlanner.Totals(s, s.Current);
            var resourceBonuses = LoadoutPresetPlanner.ResourceBonuses(s, s.Current);
            var projected = new[] { s.EnergyCapacity.Project(resourceBonuses), s.MagicCapacity.Project(resourceBonuses),
                s.EnergyPowerProjection.Project(resourceBonuses), s.MagicPowerProjection.Project(resourceBonuses), LoadoutPresetPlanner.NguBonus(s, resourceBonuses),
                LoadoutPresetPlanner.AdventureStat(s.BasePower, s.CubePower, totals[0], s.PowerFactors, s.PowerFactors[4]),
                LoadoutPresetPlanner.AdventureStat(s.BaseToughness, s.CubeToughness, totals[1], s.ToughnessFactors, s.ToughnessFactors[4]),
                LoadoutPresetPlanner.GrossGold(s, s.CurrentBeards), LoadoutPresetPlanner.Drop(s, s.Current, s.CurrentBeards, s.DiggerEffective[0]) };
            // Materialize the Single return, not the desktop CLR's unrounded evaluation-stack value.
            var currentNguBonus = new[] { c.totalNGUSpeedBonus() };
            var actual = new[] { s.EnergyCap, s.MagicCap, s.EnergyPower, s.MagicPower, currentNguBonus[0], s.Power, s.Toughness, s.GrossGold, s.Drop };
            var names = new[] { "energy capacity", "magic capacity", "energy power", "magic power", "NGU bonus", "adventure power", "adventure toughness", "gross GPS", "drop" };
            var mismatches = Enumerable.Range(0, names.Length).Where(i => i == 5 || i == 6 ? !LoadoutPresetPlanner.AdventureParity(projected[i], actual[i]) : projected[i] != actual[i]).Select(i => names[i] + " (" + projected[i].ToString("R", CultureInfo.InvariantCulture)
                + " vs " + actual[i].ToString("R", CultureInfo.InvariantCulture) + ")").ToList();
            if (!resourceBonuses.SequenceEqual(ResourceProjectionCapture.GetBonuses(LoadoutManager.CaptureExactLoadout()))) mismatches.Add("resource spec normalization");
            if (mismatches.Count > 0)
                throw new InvalidOperationException("Forward preset projection does not match current game getters: " + string.Join(", ", mismatches.ToArray()) + "; preview unavailable rather than inverting clamped totals.");
            for (var i = 5; i <= 6; i++)
                if (projected[i] != actual[i]) capture.Warnings.Add("Native float rounding: " + names[i] + " forward " + projected[i].ToString("R", CultureInfo.InvariantCulture)
                    + " vs native " + actual[i].ToString("R", CultureInfo.InvariantCulture) + "; within the adventure-only seven-ULP acceptance cap. No total was inverted or used to rescale candidate factors.");
            if (!LoadoutPresetPlanner.Finite(s.EnergyPower) || !LoadoutPresetPlanner.Finite(s.MagicPower) || s.EnergyPower <= 0 || s.MagicPower <= 0
                || s.Energy < 0 || s.Magic < 0
                || s.BasePower + totals[0] <= 0 || s.BaseToughness + totals[1] <= 0 || !LoadoutPresetPlanner.Finite(s.Drop)
                || !LoadoutPresetPlanner.Finite(s.GrossGold) || !LoadoutPresetPlanner.Finite(s.Respawn) || s.Respawn < 0 || s.AttackSeconds <= 0 || s.IdleModifier <= 0
                || s.EnergyNgu.Concat(s.MagicNgu).Any(x => !LoadoutPresetPlanner.Finite(x.Coefficient) || x.Coefficient < 0
                    || !LoadoutPresetPlanner.Finite(x.HardCap) || x.HardCap <= 0 || x.HardCap != Math.Floor(x.HardCap)
                    || !(x.SpeedDivider > 0) || !(x.TickDivider > 0) || x.TickFactors.Any(f => !LoadoutPresetPlanner.Finite(f) || f < 0)))
                throw new InvalidOperationException("Invalid captured resource, adventure, or drop totals.");
            capture.Epoch = new object[] { c, Controller, c.inventory, c.NGU, c.magic, c.adventure, c.beards, c.diggers, c.stats };
            capture.Rebirth = c.stats.rebirthNumber; capture.Elapsed = c.rebirthTime.totalseconds; capture.Total = c.totalPlaytime.totalseconds;
            capture.Instances = executionOnly ? capture.Gear.ToDictionary(x => x.Key, x => x.Value.equipment) : LoadoutPresetInstances();
            if (!executionOnly) capture.Material = LoadoutPresetMaterial(capture);
            if (enforceDeadline) LoadoutPresetDeadline(clock);
            return capture;
        }

        private static double[] CaptureLoadoutPresetAdventure(bool power)
        {
            var c = Character;
            // Capture getter F results directly as doubles; a float[] would round composite
            // factors before Character's initial expression has finished multiplying them.
            return new double[] { 1f + (power ? c.advancedTrainingController.adventurePowerBonus(0) : c.advancedTrainingController.adventureToughnessBonus(0)),
                c.NGUController.adventureBonus(), c.NGUController.adventureBonus2(), c.allBeards.adventureBonus(), c.allDiggers.totalAdventureBonus(),
                c.allChallenges.adventureBonus(), c.adventureController.itopod.totalAdventureBonus(), power ? c.adventureController.beastModeBonus() : 1f,
                c.beastQuestPerkController.totalAdventureBonus(), c.inventory.macguffinBonuses[19], c.hacksController.totalAdventureBonus(),
                c.wishesController.totalAdventureBonus(), c.cardsController.getBonus(cardBonus.adventureStat), c.inventory.itemList.evilBonusAccComplete ? 1.2f : 1f };
        }

        private static Dictionary<int, Equipment> LoadoutPresetInstances()
        {
            var inv = Character.inventory;
            var result = new Dictionary<int, Equipment> { { -1, inv.head }, { -2, inv.chest }, { -3, inv.legs },
                { -4, inv.boots }, { -5, inv.weapon }, { -6, inv.weapon2 } };
            for (var i = 0; i < inv.accs.Count; i++) result.Add(10000 + i, inv.accs[i]);
            for (var i = 0; i < inv.inventory.Count; i++) result.Add(i, inv.inventory[i]);
            return result;
        }

        private static string LoadoutPresetPrimitiveKey(object item)
        {
            if (item == null) return "null";
            return string.Join(";", item.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string) || f.FieldType == typeof(decimal))
                .OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => {
                    var value = f.GetValue(item);
                    return f.Name + "=" + (value is double ? ((double)value).ToString("R", CultureInfo.InvariantCulture)
                        : value is float ? ((float)value).ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture));
                }).ToArray());
        }

        private static string LoadoutPresetMaterial(LoadoutPresetCapture capture)
        {
            var s = capture.Snapshot; var c = Character;
            var key = new StringBuilder();
            foreach (var pair in capture.Instances.OrderBy(x => x.Key)) key.Append(pair.Key).Append(':').Append(LoadoutPresetPrimitiveKey(pair.Value)).Append('|');
            foreach (var item in s.Items.OrderBy(x => x.SourceSlot)) key.Append(item.SourceSlot).Append(':').Append(LoadoutPresetNumbers(item.Stats).ToString())
                .Append(LoadoutPresetNumbers(item.ResourceSpecs.Select(x => (double)x)).ToString()).Append('|');
            key.Append(LoadoutPresetNumbers(s.Slots.Select(x => (double)x)).ToString()).Append(LoadoutPresetNumbers(s.Factors).ToString())
                .Append(Controller.curSpaces()).Append('|').Append(c.settings.rebirthDifficulty).Append('/').Append(c.settings.nguLevelTrack)
                .Append('|').Append(LoadoutPresetNumbers(new[] { (double)c.energyPower, c.magic.magicPower, c.capEnergy, c.magic.capMagic,
                    s.CubePower, s.CubeToughness, c.adventure.attack, c.adventure.defense }).ToString())
                .Append('|').Append(string.Join(",", s.Available.Select(x => x ? "1" : "0").ToArray()))
                .Append('|').Append(c.capEnergy.ToString(CultureInfo.InvariantCulture)).Append('/').Append(c.magic.capMagic.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(string.Join(",", c.NGU.skills.Select(x => (c.settings.nguLevelTrack == difficulty.normal ? x.target
                    : c.settings.nguLevelTrack == difficulty.evil ? x.evilTarget : x.sadisticTarget).ToString(CultureInfo.InvariantCulture)).ToArray()))
                .Append('|').Append(string.Join(",", c.NGU.magicSkills.Select(x => (c.settings.nguLevelTrack == difficulty.normal ? x.target
                    : c.settings.nguLevelTrack == difficulty.evil ? x.evilTarget : x.sadisticTarget).ToString(CultureInfo.InvariantCulture)).ToArray()))
                .Append('|').Append(s.DiggerSlots).Append('/').Append(s.BeardSlots).Append('/').Append(s.DiggerLevelBonus.ToString("R", CultureInfo.InvariantCulture));
            foreach (var projection in new[] { s.EnergyCapacity, s.MagicCapacity, s.EnergyPowerProjection, s.MagicPowerProjection }) key.Append('|').Append(LoadoutPresetPrimitiveKey(projection));
            key.Append('|').Append(LoadoutPresetNumbers(s.EnergyNgu.Concat(s.MagicNgu).Select(x => x.HardCap)).ToString())
                .Append('|').Append(LoadoutPresetNumbers(s.EnergyNgu.Concat(s.MagicNgu).Select(x => (double)x.Weight)).ToString());
            foreach (var d in s.Diggers) key.Append('|').Append(LoadoutPresetPrimitiveKey(d)).Append('/').Append(c.diggers.diggers[d.Id].active);
            // Fractional beard progress is passive; completed temporary/permanent levels and selection are material support stats.
            foreach (var b in c.beards.beards) key.Append('|').Append(b.active).Append('/').Append(b.beardLevel).Append('/').Append(b.permLevel).Append('/').Append(b.bankedLevel);
            key.Append('|').Append(LoadoutPresetNumbers(s.UnlockedBeards.Select(x => (double)x)).ToString());
            key.Append('|').Append(s.WandoosOs).Append('|').Append(s.AugmentGlobalFactor.ToString("R", CultureInfo.InvariantCulture));
            foreach (var bar in new[] { s.TimeMachineEnergy, s.TimeMachineMagic, s.WandoosEnergy, s.WandoosMagic }.Concat(s.AdvancedTraining))
                key.Append('|').Append(LoadoutPresetActivityMaterial(bar));
            foreach (var aug in s.Augments)
                key.Append('|').Append(aug.Id).Append('/').Append(aug.BaseBoost.ToString("R", CultureInfo.InvariantCulture))
                    .Append('/').Append(aug.TierExponent.ToString("R", CultureInfo.InvariantCulture))
                    .Append('/').Append(LoadoutPresetActivityMaterial(aug.Augment)).Append('/').Append(LoadoutPresetActivityMaterial(aug.Upgrade));
            foreach (var z in s.Zones) key.Append('|').Append(LoadoutPresetPrimitiveKey(z)).Append(LoadoutPresetNumbers(z.BoostValues).ToString())
                .Append(LoadoutPresetNumbers(z.Chances).ToString()).Append(LoadoutPresetNumbers(z.Caps).ToString());
            if (s.ItopodBoostPerKill != null) key.Append('|').Append(LoadoutPresetNumbers(s.ItopodBoostPerKill).ToString());
            if (s.ItopodHp != null) key.Append('|').Append(LoadoutPresetNumbers(s.ItopodHp).ToString()).Append(LoadoutPresetNumbers(s.ItopodDefense).ToString());
            foreach (var ritual in s.Rituals) key.Append('|').Append(ritual.Id).Append('/').Append(ritual.Unlocked).Append('/').Append(ritual.Cost.ToString("R", CultureInfo.InvariantCulture));
            key.Append('|').Append(s.PpPerKill == null ? -1 : s.PpPerKill.Length).Append('/').Append(c.buttons.adventure.interactable)
                .Append('/').Append(c.adventure.beastModeOn).Append('/').Append(c.inventory.disabled).Append('/').Append(s.NguAutoAdvance)
                .Append('/').Append(s.BeardsDisabled).Append('/').Append(s.LazyItopodPurchased).Append('/').Append(s.LazyItopodEnabled);
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key.ToString()))).Replace("-", "");
        }

        private static string LoadoutPresetActivityMaterial(PresetActivityBar bar)
        {
            if (bar == null) return "unavailable";
            return bar.Unlocked + ":" + LoadoutPresetNumbers(new[] { bar.Target, bar.Coefficient, bar.PowerExponent,
                bar.LevelExponent, bar.CostCoefficient, bar.CostExponent }).ToString();
        }

        private static void VerifyLoadoutPresetEpoch(LoadoutPresetCapture cached, LoadoutPresetCapture fresh)
        {
            if (cached.Epoch.Where((x, i) => !ReferenceEquals(x, fresh.Epoch[i])).Any()
                || cached.Instances.Count != fresh.Instances.Count || cached.Instances.Any(x => !fresh.Instances.ContainsKey(x.Key) || !ReferenceEquals(x.Value, fresh.Instances[x.Key])))
                throw new InvalidOperationException("Save objects or exact inventory instances changed; preview again.");
        }

        private static void CaptureLoadoutPresetNgu(PresetSnapshot s, int[] weights, bool energy)
        {
            var c = Character;
            var rows = new List<PresetNgu>();
            var factors = energy ? new double[] { c.totalNGUSpeedBonus(), c.adventureController.itopod.totalEnergyNGUBonus(),
                c.inventory.macguffinBonuses[4], c.NGUController.energyNGUBonus(), c.allDiggers.totalEnergyNGUBonus(),
                c.hacksController.totalEnergyNGUBonus(), c.beastQuestPerkController.totalEnergyNGUSpeed(), c.wishesController.totalEnergyNGUSpeed(),
                c.cardsController.getBonus(cardBonus.energyNGUSpeed), c.allChallenges.trollChallenge.sadisticCompletions() >= 1 ? 3 : 1 }
                : new double[] { c.totalNGUSpeedBonus(), c.adventureController.itopod.totalMagicNGUBonus(),
                    c.inventory.macguffinBonuses[5], c.NGUController.magicNGUBonus(), c.allDiggers.totalMagicNGUBonus(),
                    c.hacksController.totalMagicNGUBonus(), c.beastQuestPerkController.totalMagicNGUSpeed(), c.wishesController.totalMagicNGUSpeed(),
                    c.cardsController.getBonus(cardBonus.magicNGUSpeed), c.allChallenges.trollChallenge.completions() >= 1 ? 3 : 1 };
            for (var i = 0; i < weights.Length; i++)
            {
                var track = c.settings.nguLevelTrack;
                var level = energy ? (track == difficulty.normal ? c.NGU.skills[i].level : track == difficulty.evil ? c.NGU.skills[i].evilLevel : c.NGU.skills[i].sadisticLevel)
                    : track == difficulty.normal ? c.NGU.magicSkills[i].level : track == difficulty.evil ? c.NGU.magicSkills[i].evilLevel : c.NGU.magicSkills[i].sadisticLevel;
                var target = energy ? (track == difficulty.normal ? c.NGU.skills[i].target : track == difficulty.evil ? c.NGU.skills[i].evilTarget : c.NGU.skills[i].sadisticTarget)
                    : track == difficulty.normal ? c.NGU.magicSkills[i].target : track == difficulty.evil ? c.NGU.magicSkills[i].evilTarget : c.NGU.magicSkills[i].sadisticTarget;
                var speedDivider = energy ? c.NGUController.energySpeedDivider(i) : c.NGUController.magicSpeedDivider(i);
                var tickDivider = track < difficulty.sadistic ? 1f : energy ? c.NGUController.NGU[i].sadisticDivider() : c.NGUController.NGUMagic[i].sadisticDivider();
                // Sharing uses the algebraic coefficient; production keeps the game's rounded prefix and ordered factors.
                rows.Add(new PresetNgu { Level = level, Target = target, HardCap = c.NGUController.hardCapNormalLevel(), Weight = weights[i],
                    Coefficient = factors.Aggregate(1d, (a, factor) => a * factor) / speedDivider / tickDivider,
                    SpeedDivider = speedDivider, TickDivider = tickDivider, TickFactors = factors });
            }
            if (energy) s.EnergyNgu = rows.ToArray(); else s.MagicNgu = rows.ToArray();
        }

        private static void CaptureLoadoutPresetSupport(PresetSnapshot s)
        {
            s.CopySupportFrom(CaptureSupportContext());
            var c = Character;
            s.CubeLoot = Controller.cubeLootBonus();
            s.DropFactors = new[] { c.adventureController.itopod.totalDropChanceBonus(), c.inventory.macguffinBonuses[10], 1f,
                c.bloodMagicController.lootBonus(), c.yggdrasilController.luckBonus(), c.NGUController.lootBonus(), 1f, 1f,
                c.hacksController.totalDropChanceBonus(), c.cardsController.getBonus((cardBonus)10),
                c.inventory.itemList.twoDComplete ? 1.0743f : 1f, c.inventory.itemList.normalBonusAccComplete ? 1.25f : 1f,
                c.arbitrary.lootcharm1Time.totalseconds > 0 ? c.allArbitrary.potionModifier() : 1f };
        }

        private static object LoadoutPresetMember(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException("Required farming table field unavailable: " + name);
            return field.GetValue(target);
        }
        private static void CaptureAcquiredPresetRoute(LoadoutPresetCapture capture, PresetCandidate route, bool unlocked)
        {
            var c = Character; var s = capture.Snapshot;
            s.AcquiredRouteObservation = true; s.AcquiredRouteZone = route.Zone; s.AcquiredRouteFloor = route.Floor;
            if (route.Zone != 1000)
            {
                CaptureLoadoutPresetFarming(capture, false, route.Zone);
                return;
            }
            var maximum = Math.Max(0, Math.Min(10000, Math.Min(c.adventureController.maxItopodLevel(), c.adventure.highestItopodLevel - 1)));
            s.ObservedItopodFloor = route.Floor; s.ObservedItopodMaximum = maximum;
            if (!unlocked || route.Floor < 1 || route.Floor > maximum) return;
            s.PpPerKill = new[] { (double)c.adventureController.itopod.progressGained(route.Floor) };
            var enemies = c.adventureController.itopodEnemyList;
            if (enemies != null && enemies.Count > 0 && enemies.All(e => e.maxHP > 0 && e.defense >= 0
                && LoadoutPresetPlanner.Finite(e.maxHP) && LoadoutPresetPlanner.Finite(e.defense)))
            {
                s.ItopodHp = new[] { (double)(float)((float)(enemies.Max(e => e.maxHP) * (float)Math.Pow(1.05f, route.Floor)) * 1.02f) };
                s.ItopodDefense = new[] { (double)(float)((float)(enemies.Max(e => e.defense) * (float)Math.Pow(1.05f, route.Floor)) * 1.02f) };
            }
            else capture.Warnings.Add("Native ITOPOD enemy stats unavailable; acquired floor is limited to the existing one-hit heuristic range.");
            s.Available[3] = true;
            CaptureLoadoutPresetFarming(capture, true, 1000);
        }

        private static PresetZone PresetRouteRecommendation(int id)
        {
            ZoneStats row;
            if (id < 0 || id == 1000 || !(ZoneStatHelper.UserOverrides ?? ZoneStatHelper.Defaults).TryGetValue(id, out row)
                || !ZoneHelpers.IsZoneUnlocked(id) || ZoneHelpers.ZoneIsTitan(id)
                || row.IPower <= 0 || row.IToughness <= 0 || row.OPower <= 0
                || !LoadoutPresetPlanner.Finite(row.IPower + row.IToughness + row.OPower)) return null;
            return new PresetZone { Id = id, IdlePower = row.IPower, IdleToughness = row.IToughness, OneHitPower = row.OPower, NormalRatio = 1 };
        }

        private static void CaptureLoadoutPresetFarming(LoadoutPresetCapture capture, bool itopod, int? acquiredZone = null)
        {
            var s = capture.Snapshot;
            var recommendations = ZoneStatHelper.UserOverrides ?? ZoneStatHelper.Defaults;
            // Direct key lookup for execution, including unavailable routes. Never fall back to alternatives.
            var selected = acquiredZone.HasValue
                ? Enumerable.Empty<KeyValuePair<int, ZoneStats>>() : recommendations.AsEnumerable();
            if (acquiredZone.HasValue && recommendations.ContainsKey(acquiredZone.Value))
                selected = new[] { new KeyValuePair<int, ZoneStats>(acquiredZone.Value, recommendations[acquiredZone.Value]) };
            if (!acquiredZone.HasValue) _presetRouteTableEnumerations++;
            var zones = selected.Where(x => ZoneHelpers.IsZoneUnlocked(x.Key) && !ZoneHelpers.ZoneIsTitan(x.Key))
                .OrderBy(x => x.Key).Select(x => new PresetZone { Id = x.Key, IdlePower = x.Value.IPower, IdleToughness = x.Value.IToughness,
                    OneHitPower = x.Value.OPower, NormalRatio = 1 })
                .Where(z => z.IdlePower > 0 && z.IdleToughness > 0 && z.OneHitPower > 0
                    && LoadoutPresetPlanner.Finite(z.IdlePower + z.IdleToughness + z.OneHitPower)).ToArray();
            s.Zones = zones; s.Available[5] = zones.Length > 0;
            s.Reasons[5] = "No unlocked regular zone with safety recommendations captured.";
            try
            {
                // Numeric capture of the existing Almanac table and existing filter/recycling policy, once per pass.
                var field = typeof(BoostFarmHelper).GetField("Zones", BindingFlags.Static | BindingFlags.NonPublic);
                if (field == null) throw new InvalidOperationException("Boost Almanac table unavailable.");
                var c = Character;
                var raw = new[] { 1d, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 };
                var recycle = Math.Min(1, Math.Max(0, c.totalRecycleBonus()));
                var values = raw.Select((v, index) => Enumerable.Range(0, index + 1)
                    .Sum(i => raw[index - i] * Math.Pow(recycle, i)) * c.allItemList.boostBonus()).ToArray();
                var filters = Enumerable.Range(0, 13).Select(i => Enumerable.Range(0, 3).Count(type => {
                    var id = 1 + i + 13 * type;
                    if (c.arbitrary.lootFilter && c.inventory.itemList.itemFiltered[id]) return false;
                    return !c.purchases.hasFilter || !c.settings.filterOn || !(type == 0 ? c.settings.filterBoostAtk : type == 1 ? c.settings.filterBoostDef : c.settings.filterBoostSpec);
                }) / 3d).ToArray();
                var selectedRow = acquiredZone.HasValue ? BoostFarmHelper.PresetZoneRow(acquiredZone.Value) : null;
                var rows = acquiredZone.HasValue ? (IEnumerable)(selectedRow == null ? new object[0] : new[] { selectedRow }) : (IEnumerable)field.GetValue(null);
                foreach (var row in rows)
                {
                    var id = (int)LoadoutPresetMember(row, "Zone"); var zone = zones.FirstOrDefault(z => z.Id == id);
                    if (zone == null) continue;
                    zone.OneHitPower = (double)LoadoutPresetMember(row, "OneHitPower");
                    zone.NormalRatio = (double)LoadoutPresetMember(row, "NormalEnemyRatio");
                    var drops = ((IEnumerable)LoadoutPresetMember(row, "Drops")).Cast<object>().ToArray();
                    zone.BoostValues = drops.Select(d => { var index = (int)LoadoutPresetMember(d, "BoostIndex"); return values[index] * filters[index]; }).ToArray();
                    zone.Chances = drops.Select(d => (double)LoadoutPresetMember(d, "BaseDropChance")).ToArray();
                    zone.Caps = drops.Select(d => (double)LoadoutPresetMember(d, "MaxDropChance")).ToArray();
                }
                if (itopod)
                {
                    var thresholds = new[] { 0, 50, 100, 150, 200, 250, 300, 350, 400, 450, 700, 850, 1150 };
                    s.ItopodBoostPerKill = Enumerable.Range(0, s.PpPerKill.Length).Select(f => {
                        var floor = s.ObservedItopodFloor >= 0 ? s.ObservedItopodFloor : f;
                        var index = Enumerable.Range(0, thresholds.Length).Last(i => floor >= thresholds[i]);
                        return values[index] * filters[index] * .14;
                    }).ToArray();
                }
                s.Available[4] = zones.Any(z => z.BoostValues.Length > 0) || itopod;
                s.FarmingEconomicsCaptured = true;
            }
            catch (Exception error)
            {
                s.Available[4] = false; s.Reasons[4] = "Boost table capture failed: " + error.GetBaseException().Message;
                capture.Warnings.Add(s.Reasons[4]);
            }
            if (!s.Available[4] && s.Reasons[4] == null) s.Reasons[4] = "No accessible boost farming table rows.";
        }

        private void RequireLoadoutPresetPreviewSupport()
        {
            if ((Settings.ManualAdventureEnabled && (Settings.ManualAdventureBeardWeights.Any(x => x > 0)
                    || Settings.ManualAdventureDiggerWeights.Any(x => x > 0)))
                || _manualAdventureSupportApplied != null)
                throw new InvalidOperationException("Turn off Manual Adventure and wait for the support handoff before Preview + Apply, or use Equip.");
        }

        private JSONObject PreviewLoadoutPreset(JSONNode args)
        {
            LoadoutPresetArgs(args, false);
            var definition = LoadoutPresetDefinition(CustomLoadoutPreset.Parse(args["preset"]));
            var preset = CustomLoadoutPreset.Parse(JSON.Parse(definition));
            RequireLoadoutPresetPreviewSupport();
            InvalidateGlobalNguWeightPolicies();
            var e = Settings.EnergyNguWeights; var m = Settings.MagicNguWeights;
            _loadoutPresetPreview = null;
            var clock = Stopwatch.StartNew(); var capture = CaptureLoadoutPresetDefinition(preset, clock);
            var request = LoadoutPresetRequest(capture.ExecutionPreset); var plan = LoadoutPresetPlanner.Plan(capture.Snapshot, request, clock);
            plan.Milliseconds = clock.ElapsedMilliseconds;
            var bytes = new byte[16]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var id = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            var result = LoadoutPresetJson(preset, request, capture, plan, id);
            LoadoutPresetDeadline(clock);
            _loadoutPresetPreview = new LoadoutPresetPreview { Capture = capture, Plan = plan, Preset = preset,
                Identity = new PresetPreviewIdentity { Id = id, Definition = definition, EnergyWeights = e.ToArray(), MagicWeights = m.ToArray(),
                    Material = capture.Material, Created = LoadoutPresetClock, Rebirth = capture.Rebirth, Elapsed = capture.Elapsed, Total = capture.Total } };
            return result;
        }

        private string LoadoutPresetApplyBlocker(PresetCandidate intent = null)
        {
            if (_executionLifecycle != ExecutionLifecycle.Running) return "Execution is not accepting ordinary work.";
            if (CombatPrepActive) return "Combat prep owns resources.";
            if (Settings == null) return "Preset settings are unavailable.";
            // Selection-free callers defer lane admission until the exact intent is known.
            if (intent?.ControlsEnergy == true && !Settings.ManageEnergy) return "Selected preset requires managed Energy.";
            if (intent?.ControlsMagic == true && !Settings.ManageMagic) return "Selected preset requires managed Magic.";
            if (Controller.midDrag) return "Inventory drag is active.";
            if (!ExecutionSetupClaimsActive)
            {
                if (_manualRebirthPending || _customPreRebirthPrepRunning || CustomBossFightActive()) return "Boss fighting or rebirth preparation owns setup.";
                if (_customTitanGoldActive || _customTitanFightActive || _customAdventureGoldPushActive || (!Settings.ManualAdventureEnabled && _maxItopodPushRemainingDeaths > 0)
                    || ZoneHelpers.ZoneIsTitan(Character.adventure.zone)) return "Adventure/titan preparation owns equipment.";
            }
            // Wrappers decide takeover only after preflight. Scheduled application must preserve its owner.
            return null;
        }

        private string ApplyLoadoutPreset(JSONNode args, out JSONNode data)
        {
            return ApplyLoadoutPresetCore(args, out data);
        }

        private string ApplyLoadoutPresetCore(JSONNode args, out JSONNode data, string globalOwner = null)
        {
            data = null;
            LoadoutPresetArgs(args, true);
            RequireLoadoutPresetContext();
            var blocker = LoadoutPresetApplyBlocker();
            if (blocker != null) throw new InvalidOperationException("Preset apply blocked: " + blocker);
            if (_resourceSetupExecutor.Application.BlockedLanes != 0)
                throw new InvalidOperationException("Resource authority is unresolved; preview application was not consumed.");
            var preset = CustomLoadoutPreset.Parse(args["preset"]);
            InvalidateGlobalNguWeightPolicies();
            var e = Settings.EnergyNguWeights; var m = Settings.MagicNguWeights;
            var cached = _loadoutPresetPreview;
            if (cached == null) throw new InvalidOperationException("Preview is missing or already consumed; preview again.");
            var id = args["previewId"].Value; var definition = LoadoutPresetDefinition(preset);
            cached.Identity.Validate(id, definition, e, m, cached.Identity.Material, LoadoutPresetClock, cached.Capture.Rebirth, cached.Capture.Elapsed, cached.Capture.Total);
            var key = args["candidateKey"].Value;
            var policy = cached.Plan.Candidates.Concat(cached.Plan.Specialists).FirstOrDefault(x => x.Key == key);
            if (policy == null) throw new InvalidOperationException("Candidate key does not belong to this preview.");
            blocker = LoadoutPresetApplyBlocker(policy);
            if (blocker != null) throw new InvalidOperationException("Preset apply blocked: " + blocker);
            RequireLoadoutPresetPreviewSupport();
            var timer = Stopwatch.StartNew(); var capture = CaptureLoadoutPresetDefinition(preset, timer);
            if (cached.Capture.AdventureTaskKey != capture.AdventureTaskKey)
                throw new InvalidOperationException("Adventure activity changed since preview; preview again. No game state was changed.");
            var request = LoadoutPresetRequest(capture.ExecutionPreset);
            VerifyLoadoutPresetEpoch(cached.Capture, capture);
            if (cached.Capture.Snapshot.LazyItopodPurchased != capture.Snapshot.LazyItopodPurchased
                || cached.Capture.Snapshot.LazyItopodEnabled != capture.Snapshot.LazyItopodEnabled)
                throw new InvalidOperationException("Lazy ITOPOD shifter purchase or enabled preference changed since preview; preview again. No preference or game state was changed by Apply.");
            cached.Identity.Validate(id, definition, e, m, capture.Material, LoadoutPresetClock, capture.Rebirth, capture.Elapsed, capture.Total);
            // Replay cached policies ONLY. Natural NGU progress, gold and pools may change; never search a replacement.
            var plan = LoadoutPresetPlanner.ReplayExecution(capture.Snapshot, request, cached.Plan, timer);
            var candidate = plan.Candidates.Concat(plan.Specialists).Single(x => x.Key == key);
            var gear = PreflightLoadoutPreset(request, capture, candidate, globalOwner != null);
            capture.SelectedRouteAuthorized = () => true; // Explicit Preview/Apply takeover.
            RequireSelectedSetupRoute(gear, new SupportSelection(candidate.Support.Beards,
                candidate.Support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()), request.PlannedSupport, capture.SelectedRouteAuthorized, ManualAdventureOverlayActive);
            plan.Milliseconds = timer.ElapsedMilliseconds;
            LoadoutPresetDeadline(timer);
            cached.Identity.Validate(id, definition, e, m, capture.Material, LoadoutPresetClock, capture.Rebirth,
                Character.rebirthTime.totalseconds, Character.totalPlaytime.totalseconds);
            GlobalPresetPolicy acquired = null;
            if (globalOwner != null)
            {
                acquired = CreateGlobalPresetPolicy(preset, capture, plan, candidate);
                acquired.Owner = globalOwner;
                PersistProtectedPresetSelection(acquired, globalOwner);
            }
            _loadoutPresetPreview = null;
            _presetResources = _resourceSetupExecutor.Application;
            ReleaseManualAdventureOverlayForPresetAcquisition(true);
            if (acquired != null)
            {
                _globalPresetApplyingPolicy = acquired;
                _globalPresetApplyingEvent = globalOwner + ":" + preset.Id;
            }
            BeginPresetSupportAcquisition(globalOwner == null ? "one-shot:" + id : globalOwner + ":" + preset.Id,
                acquired?.Acquisition ?? id);
            var message = ApplyPreparedLoadoutPreset(cached, request, capture, plan, candidate, gear, id, timer,
                globalOwner != null, globalOwner == null, out data, out var execution, out var commit, globalOwner);
            if (globalOwner != null)
            {
                AcquireGlobalPreset(preset, capture, plan, candidate, execution, globalOwner, "preview-apply", "selected-preview", commit, acquired);
                message = "Applied global preset: " + preset.Name + " / " + candidate.Label;
            }
            return message;
        }

        private ih[] PreflightLoadoutPreset(PresetRequest request, LoadoutPresetCapture capture, PresetCandidate candidate, bool exactSupport = false)
        {
            if (!candidate.Feasible || !candidate.Score.HasValue)
                throw new InvalidOperationException("The exact selected policy is no longer feasible or has an unavailable weighted goal; preview again. "
                    + string.Join(" ", candidate.Warnings));
            var originalGear = LoadoutManager.CaptureExactLoadout();
            var gear = candidate.Items.Select((item, i) => {
                var wrapper = item.Id == 0 ? new ih { equipment = new Equipment(), id = 0 } : capture.Gear[item.SourceSlot];
                return new ih { id = wrapper.id, name = wrapper.name, equipment = wrapper.equipment, level = wrapper.level, slot = capture.Snapshot.Slots[i] };
            }).ToArray();
            gear = LoadoutManager.ComposeExactLoadout(originalGear, gear);
            var failure = GearOptimizer.CheckPresetEquip(gear);
            if (failure != null) throw new InvalidOperationException(failure);
            if (request.PlannedSupport || exactSupport) PreflightExactSupport(new SupportSelection(candidate.Support.Beards,
                candidate.Support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()), request.DiggerPercent / 100d, request.PlannedSupport);
            if (request.Weights.Skip(3).Take(3).Any(x => x > 0) && (!Character.buttons.adventure.interactable || candidate.Zone < 0))
                throw new InvalidOperationException("Selected farming route is unavailable; preview again.");
            // Structural admission is deliberately before persisted policy, preview
            // consumption, owner release, target retirement or any native write.
            var changesInputs = !LoadoutManager.ExactLoadoutMatches(gear) || request.PlannedSupport
                && CaptureSupportState().Selection.StableKey != new SupportSelection(candidate.Support.Beards,
                    candidate.Support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()).StableKey;
            if (Character?.adventure == null || Controller == null
                || changesInputs && Character.adventure.zone != -1 && (Character.adventureController?.zoneSelector == null
                    || Character.adventureController.playerController == null || Character.adventureController.enemyAI == null))
                throw new InvalidOperationException("Selected setup route retirement controllers are unavailable.");
            return gear;
        }

        private string ApplyPreparedLoadoutPreset(LoadoutPresetPreview cached, PresetRequest request, LoadoutPresetCapture capture,
            PresetPlan plan, PresetCandidate candidate, ih[] gear, string id, Stopwatch timer,
            bool manualTakeover, bool legacy, out JSONNode data, out PresetCandidate execution, out ExecutionGrant commit, string globalOwner = null)
        {
            data = null;
            execution = null;
            commit = null;
            var key = candidate.Key;
            // Gear/support can change both resource lanes. Existing lane uncertainty
            // permits independent allocation, not a new coupled setup acquisition.
            if (_resourceSetupExecutor.Application.BlockedLanes != 0)
                throw new FormatException("Resource authority is unresolved; coupled preset setup was not changed.");
            // The preparation deadline must not abandon a preflighted commit after native writes begin.
            if (!GlobalPresetRequiresNativeTargets(capture.Stage)) RetireGlobalPresetNativeTargetsForSuccessor();
            _loadoutPresetSnapshot = null;
            _globalPresetReceipt = null;
            if (manualTakeover || legacy) _globalPresetManualId = null;
            try
            {
                if (manualTakeover || legacy)
                {
                    ClearPersistedExecutionSelection("focus");
                    ClearPersistedExecutionSelection("manual");
                    InvalidateCustomEquipmentApplication();
                    ClearCustomSustainedFocus();
                    StopBossPush();
                }
                if (manualTakeover)
                {
                    Settings.UseCustomRebirthPlan = false;
                    Settings.AutoRebirth = false;
                    if (legacy) Settings.ManualAdventureEnabled = false;
                    _globalPresetManualId = cached.Preset.Id;
                }
                var baselineSetup = new SelectedSetupRequirements { Owner = globalOwner ?? "preview", Gear = gear,
                    Authorized = () => ReferenceEquals(reference, this) && !_unloaded
                        && (globalOwner == null || _globalPresetApplyingPolicy != null),
                    BoundaryAuthorized = () => !GlobalPresetRequiresNativeTargets(capture.Stage)
                        || ReferenceEquals(capture.NativeAt, Character.advancedTraining)
                            && ReferenceEquals(capture.NativeAtTargetArray, Character.advancedTraining.levelTarget)
                            && capture.NativeAtTargets.SequenceEqual(Character.advancedTraining.levelTarget),
                    Recall = (candidate.ControlsEnergy && Settings.ManageEnergy ? GearRecallFootprint.Energy : 0)
                        | (candidate.ControlsMagic && Settings.ManageMagic ? GearRecallFootprint.Magic : 0),
                    ChangeSupport = request.PlannedSupport, ControlsSupport = true,
                    OwnsManualRoute = ManualAdventureOverlayActive,
                    RouteRetirementAuthorized = capture.SelectedRouteAuthorized,
                    Budget = request.DiggerPercent / 100d, Support = new SupportSelection(candidate.Support.Beards,
                        candidate.Support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()) };
                var selected = globalOwner == null ? baselineSetup : ComposeManualAdventureClaims(baselineSetup, _globalPresetApplyingPolicy);
                ApplySelectedSetup(selected);
                baselineSetup.BoundaryAuthorized = () => GlobalPresetNativeTargetsMatch(capture.Stage, globalOwner)
                    && !GlobalPresetNativeTargetsYielded(globalOwner);
                RecordManualClaimApplication(selected);
                // Applying our gear can cross a rule threshold. Keep this acquisition's resolved intent frozen.
                var actualCapture = CaptureLoadoutPresetDefinition(capture.ExecutionPreset, timer, capture.Stage, enforceDeadline: false);
                actualCapture.AdventureTaskKey = capture.AdventureTaskKey;
                actualCapture.AdventureTask = capture.AdventureTask;
                var actual = actualCapture.Snapshot;
                var executionRequest = request.Copy();
                if (Settings.ManualAdventureEnabled) ConfigureManualAdventureResourceRequest(executionRequest);
                // Composition is already selected: observe it, never reselect/resize
                // baseline support while evaluating actual funding under the claim.
                if (ManualAdventureOverlayActive || ExecutionSetupClaimsActive) executionRequest.PlannedSupport = false;
                var appliedCandidate = candidate;
                if (Settings.ManualAdventureEnabled || ExecutionSetupClaimsActive || ExecutionRouteClaimActive)
                {
                    appliedCandidate = candidate.Copy();
                    // Manual combat suppresses farming prerequisites, not its acquired
                    // successor route. Match recurring resource-only replay semantics.
                    appliedCandidate.Zone = appliedCandidate.Floor = -1;
                    if (ManualAdventureOverlayActive || ExecutionSetupClaimsActive)
                    {
                        appliedCandidate.Support.Beards = actual.CurrentBeards;
                        appliedCandidate.Support.Diggers = actual.CurrentDiggers;
                    }
                }
                var fresh = LoadoutPresetPlanner.EvaluateExecution(actual, executionRequest, actual.Current, LoadoutPresetPlanner.NormalizeWeights(executionRequest.Weights),
                    plan.Normalization, key, candidate.Label, appliedCandidate);
                if (!fresh.Feasible || !fresh.Score.HasValue) throw new InvalidOperationException("Exact policy became infeasible after equipment/support application; allocations stopped. "
                    + string.Join(" ", fresh.Warnings));
                if (Settings.ManualAdventureEnabled || ExecutionSetupClaimsActive || ExecutionRouteClaimActive) { fresh.Zone = candidate.Zone; fresh.Floor = candidate.Floor; }
                ApplyGlobalPresetNativeTargets(capture, globalOwner, fresh);
                var allocation = ComposePresetResourceDemands(actual, fresh);
                ApplyLoadoutPresetAllocations(allocation);
                VerifyLoadoutPresetAllocations(allocation);
                RequireGlobalPresetNativeTargets(capture.Stage, globalOwner, "Native AT targets changed during application; receipt rejected.");
                if (!Settings.ManualAdventureEnabled && !ExecutionRouteClaimActive && request.Weights.Skip(3).Take(3).Any(x => x > 0)) ApplyLoadoutPresetRoute(fresh, actual);
                commit = CapturePreparedPresetCommit(allocation, globalOwner);
                RequirePreparedPresetCommit(commit, globalOwner);
                _loadoutPresetSnapshot = new JSONObject();
                _loadoutPresetSnapshot["id"] = cached.Preset.Id; _loadoutPresetSnapshot["name"] = cached.Preset.Name;
                _loadoutPresetSnapshot["candidateKey"] = key; _loadoutPresetSnapshot["preset"] = cached.Preset.ToJson();
                _loadoutPresetSnapshot["effectiveWeights"] = LoadoutPresetNumbers(request.Weights);
                if (capture.AdventureTaskKey != null)
                {
                    _loadoutPresetSnapshot["adventureTaskKey"] = capture.AdventureTaskKey;
                    _loadoutPresetSnapshot["adventureTask"] = capture.AdventureTask;
                }
                _loadoutPresetSnapshot["energyNguWeights"] = LoadoutPresetNumbers(actual.EnergyNgu.Select(x => (double)x.Weight));
                _loadoutPresetSnapshot["magicNguWeights"] = LoadoutPresetNumbers(actual.MagicNgu.Select(x => (double)x.Weight));
                _loadoutPresetSnapshot["status"] = "applied"; _loadoutPresetSnapshot["appliedAt"] = DateTime.UtcNow.ToString("o");
                _loadoutPresetSnapshot["requiresReapply"] = true; _loadoutPresetSnapshot["active"] = false; _loadoutPresetSnapshot["mode"] = "applied-snapshot";
                _loadoutPresetSnapshot["reason"] = "Applied once, not sustained or reserved. Custom plan, auto-rebirth and manual-adventure settings were unchanged; Quick focus and idle boss push were cleared. Other owners may replace it. Edits require preview and explicit reapply.";
                JSONNode result = null;
                var disposition = MutationDisposition.AppliedVerified;
                try
                {
                    // Only the observed applied candidate is fresh; semantic imports
                    // intentionally do not contain historical production observations.
                    var baseline = cached.Plan.Candidates[0].Copy(); baseline.Key = "run-baseline"; baseline.Label = "Original run baseline";
                    var observedPlan = new PresetPlan { Normalization = plan.Normalization, Candidates = new[] { baseline, fresh },
                        ReferenceKeys = cached.Plan.ReferenceKeys, ReferenceProduction = cached.Plan.ReferenceProduction,
                        Specialists = new PresetCandidate[0], Evaluated = plan.Evaluated, Milliseconds = timer.ElapsedMilliseconds, Budget = plan.Budget };
                    result = LoadoutPresetJson(cached.Preset, request, actualCapture, observedPlan, id);
                    if (!legacy)
                    {
                        result["execution"] = manualTakeover ? "Dashboard ownership; plan and auto-rebirth paused after preflight."
                            : "Scheduled ownership; plan and auto-rebirth preserved.";
                        result["warnings"] = LoadoutPresetArray(actualCapture.Warnings.Concat(new[] {
                            "Heuristic policy, not a guaranteed global optimum. Acquired decisions replay without periodic optimization." }));
                        result["assumptions"] = LoadoutPresetArray(result["assumptions"].Children.Select(x => x.Value)
                            .Where(x => !x.Contains("One-shot") && !x.Contains("settings remain unchanged"))
                            .Where(x => !string.IsNullOrEmpty(id) || !x.StartsWith("PreviewId binds", StringComparison.Ordinal))
                            .Select(x => x.Replace("no periodic allocator is promised by one-shot apply", "global ownership refreshes allocations without re-search")
                                .Replace("no sustained owner or preference override is installed", "the native preference is never overridden"))
                            .Concat(new[] { "Global equip acquires the selected policy until displaced. AutoUpdate searches only on re-equip events; allocation refresh replays immutable decisions. Explicit Preview + Apply seeds the exact selected candidate." }));
                    }
                    RequirePreparedPresetCommit(commit, globalOwner);
                }
                catch (Exception error)
                {
                    // A read-only result error cannot erase a still-verified native
                    // commit. Missing grant/context/map proof remains uncertain.
                    RequirePreparedPresetCommit(commit, globalOwner, error);
                    disposition = MutationDisposition.ModelAppliedRenderFailed;
                    result = null;
                    _loadoutPresetSnapshot["resultError"] = error.Message;
                    LogError("Preset result projection after verified commit", error);
                }
                _loadoutPresetSnapshot["resultDisposition"] = disposition.ToString();
                data = new JSONObject(); data["result"] = result ?? JsonFiles.ParseObject(_loadoutPresetSnapshot.ToString());
                execution = fresh;
            }
            catch (Exception error)
            {
                if (ManualAdventureOverlayActive) RecordManualAdventureSetupFailure(error);
                var supportError = error as SupportApplicationException;
                var resourceError = error as ResourceMutationException;
                var setupError = error as SelectedSetupException;
                if (supportError != null && (supportError.Result.Disposition == MutationDisposition.KnownPartial
                    || supportError.Result.Disposition == MutationDisposition.RejectedUnchanged)
                    || resourceError?.Result.Disposition == MutationDisposition.RejectedUnchanged
                    || setupError?.Outcome.Disposition == MutationDisposition.KnownPartial
                    || setupError?.Outcome.Disposition == MutationDisposition.RejectedUnchanged)
                    throw new InvalidOperationException("Preset configuration is waiting to finish its acquired desired state: " + error.Message, error);
                ReleaseGlobalPresetNativeTargets();
                _loadoutPresetSnapshot = null;
                if (globalOwner != null)
                {
                    BlockPresetExecution(globalOwner + ":" + cached.Preset.Id,
                        error is ResourceMutationException ? "resources" : "gear/support/native-targets/route", error);
                    SetResourceExecution(ResourceExecutionState.NeedsUser, globalOwner, _globalPresetFailure);
                }
                throw new InvalidOperationException("Preset was partially applied; "
                    + (!string.IsNullOrEmpty(id) ? "preview token consumed. " : "automatic retries stopped. ") + error.Message, error);
            }
            return "Applied preset once: " + cached.Preset.Name + " / " + candidate.Label + ". Requires explicit reapply; sequence is preview-only.";
        }

        private ExecutionGrant CapturePreparedPresetCommit(PresetCandidate allocation, string owner)
        {
            var setup = _selectedSetup; var resources = _resourceSetupExecutor.Current; var model = Character;
            var gear = LoadoutManager.CaptureExactLoadout(); var material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var support = CaptureSupportState().StableKey; var accounting = ResourceWriter.AllocatedResourceKey();
            var settings = Settings; var preferences = DelegatedManualRouteSettingsKey(); var native = ExecutionRebindNativeKey();
            var epoch = new LoadoutManager.SnapshotEpoch(); var scheduler = _protectedScheduler; var generation = _protectedGeneration;
            var entry = scheduler?.Entry; var cycle = scheduler?.Cycle;
            var objects = ResourceWriter.CaptureResourceContext(model).Concat(CombatPrepAdventureRouteObjects()).Concat(SupportObjects()).ToArray();
            long?[] pools() => new long?[] { Character.curEnergy, Character.idleEnergy, Character.magic.curMagic, Character.magic.idleMagic,
                Character.res3?.curRes3, Character.res3?.idleRes3 };
            var currentPools = pools();
            var domains = ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.NativeSettings
                | (resources.Energy ? ExecutionDomain.Energy : 0) | (resources.Magic ? ExecutionDomain.Magic : 0) | (resources.Res3 ? ExecutionDomain.Res3 : 0);
            // This fences committed model evidence, not full readiness. Existing
            // scoped native-setting yields still withhold ordinary receipt validity.
            return ResolveExecutionGrant(owner ?? setup.Owner, setup.Acquisition, domains, () => {
                var current = ResourceWriter.CaptureResourceContext(Character).Concat(CombatPrepAdventureRouteObjects()).Concat(SupportObjects()).ToArray();
                return ReferenceEquals(model, Character) && epoch.Matches() && ReferenceEquals(Settings, settings)
                    && preferences == DelegatedManualRouteSettingsKey() && native == ExecutionRebindNativeKey()
                    && ReferenceEquals(scheduler, _protectedScheduler) && generation == _protectedGeneration && entry == _protectedScheduler?.Entry && cycle == _protectedScheduler?.Cycle
                    && ReferenceEquals(setup, _selectedSetup) && setup.Authorized?.Invoke() == true
                    && (setup.MaterialValid == null || setup.MaterialValid()) && LoadoutManager.ExactLoadoutMatches(setup.Gear)
                    && CaptureSupportState().Selection.StableKey == setup.Support.StableKey
                    && ReferenceEquals(resources, _resourceSetupExecutor.Current) && resources.Authorized()
                    && current.Length == objects.Length && !current.Where((x, i) => !ReferenceEquals(x, objects[i])).Any()
                    && LoadoutManager.ExactLoadoutMatches(gear) && !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any()
                    && CaptureSupportState().StableKey == support && accounting == ResourceWriter.AllocatedResourceKey() && currentPools.SequenceEqual(pools())
                    && LoadoutPresetAllocationsMatch(allocation);
            }, resolvedClaims: _executionClaims.Where(x => !x.Ended && (x.Domains & domains) != 0).ToArray());
        }

        private void RequirePreparedPresetCommit(ExecutionGrant commit, string owner, Exception cause = null)
        {
            if (commit != null && VerifyExecutionPrerequisites(commit, commit.Domains, () => true)) return;
            const string reason = "Prepared preset application authority changed; review the acquired selection before continuing.";
            if (ReferenceEquals(reference, this) && !_unloaded)
            {
                if (owner != null && owner == ProtectedPresetOwner)
                { _protectedSelectedPolicyBlockedEntry = AutomaticRecoveryEntry; _protectedSelectedPolicyIssue = reason; }
                else if (owner == "dashboard") _standaloneSelectionIssue = reason;
                SuspendProtectedStageProgress();
            }
            throw new InvalidOperationException(reason, cause);
        }

        private static void ApplyLoadoutPresetRoute(PresetCandidate candidate, PresetSnapshot snapshot, Func<bool> authorized = null,
            ExecutionGrant ownerGrant = null)
        {
            var coordinator = reference;
            if (coordinator == null) throw new InvalidOperationException("Preset route has no current execution authority.");
            if (coordinator.ExecutionRouteClaimActive) return;
            var observedGear = LoadoutManager.CaptureExactLoadout();
            var material = observedGear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var support = CaptureSupportState().StableKey;
            var initialZone = Character.adventure.zone;
            var initialStart = Character.adventure.itopodStart; var initialEnd = Character.adventure.itopodEnd;
            var expectedAttack = Character.adventure.autoattacking;
            var expectedStartText = Character.adventureController.itopodStartInput?.text;
            var expectedEndText = Character.adventureController.itopodEndInput?.text;
            var lazyPurchased = Character.arbitrary.boughtLazyITOPOD; var lazyEnabled = Character.arbitrary.lazyITOPODOn;
            var policy = coordinator._globalPresetApplyingPolicy; var receipt = coordinator._globalPresetReceipt;
            var baseline = coordinator._executionBaselineResourceIntent;
            var footprint = ExecutionDomain.Route | ExecutionDomain.NativeSettings | ExecutionDomain.Gear | ExecutionDomain.Support
                | (ownerGrant == null ? ExecutionDomain.None : ExecutionDomain.Energy | ExecutionDomain.Magic);
            Func<bool> current = () => !coordinator.ExecutionRouteClaimActive && (authorized == null || authorized())
                    && ReferenceEquals(policy, coordinator._globalPresetApplyingPolicy) && ReferenceEquals(receipt, coordinator._globalPresetReceipt)
                    && LoadoutManager.ExactLoadoutMatches(observedGear)
                    && !observedGear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any()
                    && CaptureSupportState().StableKey == support
                    && (Character.adventure.zone == initialZone || Character.adventure.zone == candidate.Zone)
                    && Character.adventure.autoattacking == expectedAttack
                    && Character.arbitrary.boughtLazyITOPOD == lazyPurchased && Character.arbitrary.lazyITOPODOn == lazyEnabled
                    && (candidate.Zone != 1000 || ((Character.adventure.itopodStart == initialStart && Character.adventure.itopodEnd == initialEnd
                            || Character.adventure.itopodStart == candidate.Floor && Character.adventure.itopodEnd == candidate.Floor)
                        && Character.adventureController.itopodStartInput?.text == expectedStartText
                        && Character.adventureController.itopodEndInput?.text == expectedEndText))
                    && Character.bossController != null && !Character.bossController.isFighting && !Character.bossController.nukeBoss;
            // The actual owner supplies its coupled proof. Ordinary presets still
            // require their CURRENT baseline; a missing baseline never authorizes routing.
            var grant = ownerGrant ?? coordinator.ResolveExecutionGrant(receipt?.Owner ?? "preset-route", policy?.Acquisition ?? receipt?.Policy.Acquisition,
                footprint, () => current() && ReferenceEquals(baseline, coordinator._executionBaselineResourceIntent)
                    && coordinator.ExecutionBaselineResourceMapMatches(baseline),
                resolvedClaims: coordinator._executionClaims.Where(x => !x.Ended && (x.Domains & footprint) != 0).ToArray());
            Action require = () => {
                if (!coordinator.VerifyExecutionPrerequisites(grant, footprint, current))
                    throw new InvalidOperationException("Selected farming route authority changed; activation stopped.");
                if (Character?.stats != null && SelectedSetupConflicts.TryGetValue(Character, out var conflict)
                    && conflict.Rebirth == Character.stats.rebirthNumber && conflict.Route != null)
                    throw new SelectedSetupAuthorityException("route", conflict.Route);
            };
            require();
            var c = Character; var controller = c.adventureController;
            if (c.adventure.beastModeOn) throw new InvalidOperationException("Farming with Beast Mode on is not supported; routing stopped.");
            if (!c.buttons.adventure.interactable || candidate.Zone < 0 || !ZoneHelpers.IsZoneUnlocked(candidate.Zone))
                throw new InvalidOperationException("Selected farming route is no longer available; gear/resources were already applied.");
            var routeChanged = c.adventure.zone != candidate.Zone;
            if (candidate.Zone == 1000)
            {
                var floor = candidate.Floor;
                if (floor < 1 || snapshot.PpPerKill == null
                    || (snapshot.ObservedItopodFloor >= 0 ? snapshot.ObservedItopodFloor != floor : floor >= snapshot.PpPerKill.Length))
                    throw new InvalidOperationException("Exact preview ITOPOD floor is unavailable; routing stopped.");
                if (c.arbitrary.boughtLazyITOPOD && c.arbitrary.lazyITOPODOn
                    && floor != Math.Min(controller.maxItopodLevel(), Math.Min(c.calculateBestItopodLevel(), c.adventure.highestItopodLevel - 1)))
                    throw new InvalidOperationException("Lazy ITOPOD shifter would replace the exact preview floor after a kill; routing stopped without changing the shifter preference.");
                if (c.adventure.itopodStart != floor || c.adventure.itopodEnd != floor)
                {
                    routeChanged = true;
                    expectedStartText = floor.ToString(CultureInfo.InvariantCulture);
                    controller.itopodStartInput.text = expectedStartText;
                    require();
                    expectedEndText = floor.ToString(CultureInfo.InvariantCulture);
                    controller.itopodEndInput.text = expectedEndText;
                    require();
                    controller.verifyItopodInputs();
                    require();
                }
                if (c.adventure.itopodStart != floor || c.adventure.itopodEnd != floor)
                    throw new InvalidOperationException("Game rejected exact preview ITOPOD floor; routing stopped.");
            }
            else
            {
                var zone = snapshot.Zones.FirstOrDefault(z => z.Id == candidate.Zone);
                if (zone == null || c.totalAdvAttack() < zone.IdlePower || c.totalAdvDefense() < zone.IdleToughness)
                    throw new InvalidOperationException("Actual equipped stats fail the selected zone recommendation; gear/resources were already applied, routing stopped.");
            }
            require();
            if (!c.adventure.autoattacking) { expectedAttack = true; controller.idleAttackMove.setToggle(); require(); }
            if (!c.adventure.autoattacking) throw new InvalidOperationException("Native idle attack setting did not verify; farming route was not activated.");
            // Preserve captured beast mode rather than importing an unrelated adventure policy's toggle.
            if (routeChanged) controller.zoneSelector.changeZone(candidate.Zone);
            require();
            if (!c.adventure.autoattacking) throw new InvalidOperationException("Native idle attack setting changed during farming route activation.");
            if (c.adventure.zone != candidate.Zone)
                throw new InvalidOperationException("Game rejected selected farming route; preset was only partially applied.");
        }

        private static void ApplyLoadoutPresetAllocations(PresetCandidate candidate)
        {
            ResourceWriter.Execute(() =>
            {
                var c = Character;
                var energy = candidate.EnergyShares ?? new long[0];
                var magic = candidate.MagicShares ?? new long[0];
                if (energy.Any(x => x < 0) || magic.Any(x => x < 0)
                    || candidate.Blood < 0 || candidate.Ritual < -1 || candidate.Ritual < 0 && candidate.Blood > 0)
                    throw new InvalidOperationException("Invalid exact preset resource allocation.");
                ValidatePresetActivities(candidate, long.MaxValue, long.MaxValue);
                if (!c.buttons.ngu.interactable && (energy.Any(x => x > 0) || magic.Any(x => x > 0)))
                    throw new InvalidOperationException("Exact preset NGU targets are locked.");
                if (c.buttons.ngu.interactable)
                {
                    if (energy.Length > c.NGU.skills.Count || energy.Length > Settings.EnergyNguWeights.Length
                        || magic.Length > c.NGU.magicSkills.Count || magic.Length > Settings.MagicNguWeights.Length)
                        throw new InvalidOperationException("Exact preset NGU targets are unavailable.");
                    if (energy.Where((amount, i) => amount > 0 && Settings.EnergyNguWeights[i] == 0).Any()
                        || magic.Where((amount, i) => amount > 0 && Settings.MagicNguWeights[i] == 0).Any())
                        throw new InvalidOperationException("Global NGU exclusions changed; re-equip the preset before allocating.");
                }
                if (candidate.Ritual >= 0 && candidate.Blood > 0)
                {
                    if (!c.buttons.bloodMagic.interactable || candidate.Ritual >= c.bloodMagicController.ritualsUnlocked()
                        || candidate.Ritual >= c.bloodMagic.ritual.Count || candidate.Ritual >= c.bloodMagicController.bloodMagics.Length)
                        throw new InvalidOperationException("Selected blood ritual is locked.");
                }
                var desired = ResourceWriter.EmptyDesiredMap(true, true);
                Array.Copy(energy, desired[ResourceTarget.EnergyNgu], energy.Length);
                Array.Copy(magic, desired[ResourceTarget.MagicNgu], magic.Length);
                if (candidate.Ritual >= 0 && candidate.Blood > 0)
                    desired[ResourceTarget.BloodRitual][candidate.Ritual] = candidate.Blood;
                var a = candidate.Activities;
                if (a != null)
                {
                    desired[ResourceTarget.TimeMachineEnergy][0] = a.TimeMachineEnergy;
                    desired[ResourceTarget.TimeMachineMagic][0] = a.TimeMachineMagic;
                    desired[ResourceTarget.WandoosEnergy][0] = a.WandoosEnergy;
                    desired[ResourceTarget.WandoosMagic][0] = a.WandoosMagic;
                    Array.Copy(a.AT, desired[ResourceTarget.AdvancedTraining], a.AT.Length);
                    if (a.AugmentId >= 0)
                    {
                        desired[ResourceTarget.Augment][a.AugmentId] = a.AugmentEnergy;
                        desired[ResourceTarget.AugmentUpgrade][a.AugmentId] = a.UpgradeEnergy;
                    }
                }
                var epoch = new LoadoutManager.SnapshotEpoch();
                if (!ReferenceEquals(c, _presetResourcePresentationCharacter)) _presetResourcePresentationPending = false;
                var coordinator = reference;
                var applying = coordinator._globalPresetApplyingPolicy;
                var applyingOwner = applying?.Owner;
                var fightEntry = coordinator._stageFightSchedule?.Entry;
                var receipt = coordinator._globalPresetReceipt;
                var application = coordinator._presetResources = coordinator._resourceSetupExecutor.Application;
                _presetResourceOutcome = coordinator.ReconcileResourceSetup(new ResourceSetupIntent {
                    Owner = "preset:" + (receipt?.Owner ?? coordinator._globalPresetApplyingEvent ?? "preview"),
                    Desired = desired, Energy = candidate.ControlsEnergy && Settings.ManageEnergy
                        && (receipt == null || !coordinator.GlobalPresetNativeTargetsYielded(receipt.Owner)),
                    Magic = candidate.ControlsMagic && Settings.ManageMagic,
                    Render = () => {
                        if (candidate.Ritual >= 0 && candidate.Blood > 0) c.bloodMagicController.bloodMagics[candidate.Ritual].refresh();
                        if (c.buttons.ngu.interactable) c.NGUController.refreshMenu();
                        if (a != null) { c.timeMachineController.updateMenu(); c.wandoos98Controller.updateText(); c.advancedTrainingController.refresh(); }
                    },
                    // Receipt publication changes the observation object, not the
                    // acquisition. Only this exact policy/owner handoff preserves its grant.
                    Authorized = () => epoch.Matches()
                        && (ReferenceEquals(applying, coordinator._globalPresetApplyingPolicy) && ReferenceEquals(receipt, coordinator._globalPresetReceipt)
                            || applying != null && coordinator._globalPresetReceipt?.Owner == applyingOwner
                                && ReferenceEquals(applying, coordinator._globalPresetReceipt.Policy) && GlobalPresetEpochMatches(applying))
                        && !Controller.midDrag });
                _presetResourcePresentationPending = application.PresentationPending;
                _presetResourcePresentationCharacter = _presetResourcePresentationPending ? c : null;
                if (!_presetResourceOutcome.Verified) throw new ResourceMutationException(_presetResourceOutcome);
                if (candidate.ControlsEnergy && Settings.ManageEnergy
                    && fightEntry == coordinator._stageFightSchedule?.Entry
                    && (receipt?.Owner == coordinator.ProtectedPresetOwner || applyingOwner == coordinator.ProtectedPresetOwner)
                    && coordinator.ExecutionBaselineResourceMapMatches(new ResourceSetupIntent { Desired = desired, Energy = true }))
                    coordinator.ObserveStageFightAugmentFunding(desired);
                if (receipt != null && candidate.ControlsMagic && Settings.ManageMagic)
                {
                    var temporary = candidate.Blood > 0 && !GlobalPresetHasMagicObjective(receipt.Policy);
                    receipt.TemporaryMagicContext = temporary ? ResourceWriter.CaptureResourceContext(c) : null;
                    receipt.TemporaryMagicAllocations = temporary ? GlobalPresetLaneState(false) : null;
                }
                BlackBarAllocator.ClearNguAppliedWeights(true); BlackBarAllocator.ClearNguAppliedWeights(false);
            });
        }

        private static void VerifyLoadoutPresetAllocations(PresetCandidate candidate)
        {
            if (!LoadoutPresetAllocationsMatch(candidate))
                throw new InvalidOperationException("Observed resource allocations do not match the selected preset policy; application receipt rejected.");
        }

        private static bool LoadoutPresetAllocationsMatch(PresetCandidate candidate)
        {
            if (candidate?.EnergyShares == null || candidate.MagicShares == null
                || candidate.EnergyShares.Length > Character.NGU.skills.Count || candidate.MagicShares.Length > Character.NGU.magicSkills.Count
                || candidate.EnergyShares.Any(x => x < 0) || candidate.MagicShares.Any(x => x < 0)
                || candidate.Ritual >= Character.bloodMagic.ritual.Count || candidate.Blood < 0) return false;
            if (candidate.Activities != null && (candidate.Activities.AT == null
                || candidate.Activities.AT.Length > Character.advancedTraining.energy.Length
                || candidate.Activities.AugmentId >= Character.augments.augs.Length)) return false;
            var energy = candidate.ControlsEnergy && Settings.ManageEnergy;
            var magic = candidate.ControlsMagic && Settings.ManageMagic;
            var desired = ResourceWriter.EmptyDesiredMap(energy, magic);
            if (energy) Array.Copy(candidate.EnergyShares, desired[ResourceTarget.EnergyNgu], candidate.EnergyShares.Length);
            if (magic)
            {
                Array.Copy(candidate.MagicShares, desired[ResourceTarget.MagicNgu], candidate.MagicShares.Length);
                if (candidate.Ritual >= 0 && candidate.Blood > 0) desired[ResourceTarget.BloodRitual][candidate.Ritual] = candidate.Blood;
            }
            var a = candidate.Activities;
            if (a != null)
            {
                if (energy)
                {
                    desired[ResourceTarget.TimeMachineEnergy][0] = a.TimeMachineEnergy; desired[ResourceTarget.WandoosEnergy][0] = a.WandoosEnergy;
                    Array.Copy(a.AT, desired[ResourceTarget.AdvancedTraining], a.AT.Length);
                    if (a.AugmentId >= 0) { desired[ResourceTarget.Augment][a.AugmentId] = a.AugmentEnergy; desired[ResourceTarget.AugmentUpgrade][a.AugmentId] = a.UpgradeEnergy; }
                }
                if (magic)
                { desired[ResourceTarget.TimeMachineMagic][0] = a.TimeMachineMagic; desired[ResourceTarget.WandoosMagic][0] = a.WandoosMagic; }
            }
            var intent = reference.ComposeExecutionResources(new ResourceSetupIntent { Owner = "preset-observation", Desired = desired,
                Energy = energy, Magic = magic });
            var observed = ResourceWriter.CaptureAllocations(Character);
            return intent.Desired.All(x => x.Value != null && observed[x.Key] != null && x.Value.SequenceEqual(observed[x.Key]));
        }

        private JSONObject GetLoadoutPresetRuntime()
        {
            if (_loadoutPresetSnapshot == null) return null;
            var result = JSON.Parse(_loadoutPresetSnapshot.ToString()).AsObject;
            if (_globalPresetReceipt != null)
            {
                var active = GlobalPresetReceiptValid(_globalPresetReceipt.Owner);
                var acquired = _globalPresetReceipt.Policy.Preset;
                var preset = CustomPlanStore?.PresetLibrary.Get(_globalPresetReceipt.Id);
                var savedChanges = preset == null || LoadoutPresetDefinition(preset) != LoadoutPresetDefinition(acquired);
                result["active"] = active;
                result["requiresReapply"] = !active || savedChanges;
                result["savedChangesRequireReequip"] = savedChanges;
                result["appliedName"] = acquired.Name;
                result["appliedAutoUpdate"] = acquired.AutoUpdate;
                result["currentOwner"] = active ? _globalPresetReceipt.Owner : "displaced";
                result["status"] = active ? "applied" : "historical";
                var issue = GetGlobalPresetApplicationIssue(_globalPresetReceipt.Owner);
                result["applicationIssue"] = issue.ToString();
                result["reason"] = issue == PresetApplicationIssue.None ? "Applied composed resource policy."
                    : "Preset allocation requires reconciliation: " + issue;
                if (issue == PresetApplicationIssue.WeightsChanged)
                {
                    result["reason"] = "Misc global NGU weights changed; the next resource pass composes the selected policy with current ratios.";
                    result["globalWeightsRequireReequip"] = false;
                    result["currentOwner"] = _globalPresetReceipt.Owner;
                    result["status"] = "weights-changed";
                }
                if (acquired.SupportMode == "planned" && acquired.BeardRotation != null)
                {
                    var rotation = new JSONObject();
                    rotation["lastRotatedAt"] = _globalPresetReceipt.LastBeardRotation.ToString("o");
                    rotation["intervalSeconds"] = 60;
                    rotation["beards"] = LoadoutPresetNumbers(_globalPresetReceipt.Execution.Support.Beards.Select(x => (double)x));
                    rotation["diggers"] = LoadoutPresetArray(_globalPresetReceipt.Execution.Support.Diggers.Select(x => x.Id + ":" + x.Level));
                    rotation["observed"] = active;
                    result["rotation"] = rotation;
                }
                if (preset != null)
                {
                    result["name"] = preset.Name;
                    result["autoUpdate"] = preset.AutoUpdate;
                    result["AutoUpdate"] = preset.AutoUpdate;
                }
            }
            else if (result["owner"].IsString)
            {
                result["status"] = "historical";
                result["active"] = false;
                result["requiresReapply"] = true;
                result["currentOwner"] = "none";
            }
            return result;
        }

        private static JSONNode LoadoutPresetNumber(double? number)
        { return number.HasValue && LoadoutPresetPlanner.Finite(number.Value) ? (JSONNode)new JSONNumber(number.Value) : JSONNull.CreateOrGet(); }
        private static JSONArray LoadoutPresetArray(IEnumerable<string> values)
        { var array = new JSONArray(); foreach (var value in values) array.Add(value ?? ""); return array; }
        private static JSONArray LoadoutPresetNumbers(IEnumerable<double> values)
        { var array = new JSONArray(); foreach (var value in values) array.Add(LoadoutPresetNumber(value)); return array; }

        private static JSONArray LoadoutPresetMetrics(double?[] metrics, PresetSnapshot s, PresetPlan plan, double?[] baseline, double[] weights)
        {
            var array = new JSONArray();
            for (var i = 0; i < LoadoutPresetPlanner.Keys.Length; i++)
            {
                var row = new JSONObject(); row["key"] = LoadoutPresetPlanner.Keys[i]; row["label"] = LoadoutPresetPlanner.Labels[i];
                row["value"] = LoadoutPresetNumber(metrics[i]); row["unit"] = LoadoutPresetPlanner.Units[i];
                row["quality"] = !metrics[i].HasValue ? "unavailable" : i >= LoadoutPresetPlanner.ProductionGoalCount ? "projected" : i == 2 ? "estimated" : "proxy";
                if (!metrics[i].HasValue) row["reason"] = s.Available[i] ? "Projection could not be certified within the bounded numeric work budget." : s.Reasons[i] ?? "Not captured.";
                var normalized = metrics[i].HasValue ? (double?)LoadoutPresetPlanner.ScoringRatio(metrics[i].Value, plan.Normalization[i]) : null;
                row["normalized"] = LoadoutPresetNumber(normalized);
                var reference = plan.ReferenceProduction[i];
                var retained = reference.HasValue && reference.Value > 0 ? metrics[i] / reference : null;
                row["specialistReferenceKey"] = plan.ReferenceKeys[i] == null ? (JSONNode)JSONNull.CreateOrGet() : new JSONString(plan.ReferenceKeys[i]);
                row["specialistProduction"] = LoadoutPresetNumber(reference);
                row["retainedProduction"] = LoadoutPresetNumber(retained);
                row["retainedProductionPercent"] = LoadoutPresetNumber(retained * 100);
                row["weightedContribution"] = LoadoutPresetNumber(weights[i] * normalized);
                row["relativeToCurrent"] = LoadoutPresetNumber(baseline[i].HasValue && baseline[i].Value > 0 ? metrics[i] / baseline[i] : null);
                array.Add(row);
            }
            return array;
        }

        private static JSONObject LoadoutPresetCandidateJson(PresetCandidate c, PresetSnapshot s, PresetRequest request, PresetPlan plan)
        {
            var row = new JSONObject(); row["key"] = c.Key; row["label"] = c.Label;
            row["itemIds"] = LoadoutPresetNumbers(c.Items.Where(x => x.Id != 0).Select(x => (double)x.Id));
            var items = new JSONArray();
            for (var i = 0; i < c.Items.Length; i++)
            {
                var item = c.Items[i]; if (item.Id == 0) continue;
                var entry = new JSONObject(); entry["id"] = item.Id; entry["name"] = item.Name;
                entry["slot"] = s.Slots[i]; entry["sourceSlot"] = item.SourceSlot; entry["level"] = item.Level; entry["factor"] = s.Factors[i]; items.Add(entry);
            }
            row["items"] = items; row["metrics"] = LoadoutPresetMetrics(c.Metrics, s, plan, plan.Candidates[0].Metrics, LoadoutPresetPlanner.NormalizeWeights(request.Weights));
            row["score"] = LoadoutPresetNumber(c.Score); row["magicBloodPercent"] = c.BloodPercent;
            if (c.Activities != null)
            {
                var a = c.Activities; var activities = new JSONObject();
                activities["timeMachineEnergy"] = a.TimeMachineEnergy.ToString(CultureInfo.InvariantCulture);
                activities["timeMachineMagic"] = a.TimeMachineMagic.ToString(CultureInfo.InvariantCulture);
                activities["wandoosEnergy"] = a.WandoosEnergy.ToString(CultureInfo.InvariantCulture);
                activities["wandoosMagic"] = a.WandoosMagic.ToString(CultureInfo.InvariantCulture);
                activities["augmentId"] = a.AugmentId;
                activities["augmentEnergy"] = a.AugmentEnergy.ToString(CultureInfo.InvariantCulture);
                activities["upgradeEnergy"] = a.UpgradeEnergy.ToString(CultureInfo.InvariantCulture);
                activities["advancedTraining"] = LoadoutPresetArray(a.AT.Select(x => x.ToString(CultureInfo.InvariantCulture)));
                row["activities"] = activities;
            }
            row["energyAllocated"] = LoadoutPresetNumber(c.Energy); row["magicNguAllocated"] = LoadoutPresetNumber(c.MagicNgu);
            row["bloodAllocated"] = LoadoutPresetNumber(c.Blood); row["bloodRitual"] = c.Ritual >= 0 ? (JSONNode)new JSONNumber(c.Ritual) : JSONNull.CreateOrGet();
            row["zone"] = c.Zone >= 0 ? (JSONNode)new JSONNumber(c.Zone) : JSONNull.CreateOrGet();
            row["itopodFloor"] = c.Floor >= 0 ? (JSONNode)new JSONNumber(c.Floor) : JSONNull.CreateOrGet();
            row["expectedHits"] = c.Hits > 0 ? (JSONNode)new JSONNumber(c.Hits) : JSONNull.CreateOrGet(); row["feasible"] = c.Feasible;
            var support = new JSONObject(); support["mode"] = request.PlannedSupport ? "planned" : "current";
            support["beards"] = LoadoutPresetNumbers(c.Support.Beards.Select(x => (double)x));
            support["diggerWeights"] = LoadoutPresetNumbers(c.Support.DiggerWeights);
            var diggers = new JSONArray();
            foreach (var d in c.Support.Diggers)
            {
                var entry = new JSONObject(); entry["id"] = d.Id; entry["name"] = s.Diggers.Single(x => x.Id == d.Id).Name; entry["level"] = LoadoutPresetNumber(d.Level);
                entry["weight"] = LoadoutPresetNumber(d.Id >= 0 && d.Id < c.Support.DiggerWeights.Length ? (double?)c.Support.DiggerWeights[d.Id] : null);
                entry["drain"] = LoadoutPresetNumber(d.Drain(d.Level)); diggers.Add(entry);
            }
            support["diggers"] = diggers; support["goldBudget"] = LoadoutPresetNumber(c.Support.Budget);
            support["goldDrain"] = LoadoutPresetNumber(c.Support.Drain); support["goldRemaining"] = LoadoutPresetNumber(c.Support.Remaining);
            support["notes"] = LoadoutPresetArray(c.Support.Notes); row["support"] = support;
            row["warnings"] = LoadoutPresetArray(c.Warnings); return row;
        }

        private static JSONObject LoadoutPresetJson(CustomLoadoutPreset preset, PresetRequest request, LoadoutPresetCapture capture, PresetPlan plan, string previewId)
        {
            var s = capture.Snapshot; var root = new JSONObject();
            root["version"] = 1; root["presetId"] = preset.Id; root["presetName"] = preset.Name;
            root["previewId"] = previewId;
            root["capturedAt"] = capture.CapturedAt.ToString("o"); root["horizonSeconds"] = request.Horizon;
            root["objectiveKeys"] = LoadoutPresetArray(LoadoutPresetPlanner.Keys); root["baselineKey"] = plan.Candidates[0].Key;
            var candidates = new JSONArray(); foreach (var c in plan.Candidates) candidates.Add(LoadoutPresetCandidateJson(c, s, request, plan)); root["candidates"] = candidates;
            var specialists = new JSONArray(); foreach (var c in plan.Specialists) specialists.Add(LoadoutPresetCandidateJson(c, s, request, plan)); root["specialists"] = specialists;
            root["normalization"] = LoadoutPresetNumbers(plan.Normalization);
            root["lazyItopodShifterActive"] = s.LazyItopodActive;
            root["normalizedWeights"] = LoadoutPresetNumbers(LoadoutPresetPlanner.NormalizeWeights(request.Weights));
            root["effectiveWeights"] = LoadoutPresetNumbers(request.Weights);
            if (capture.AdventureTaskKey != null)
            {
                root["adventureTaskKey"] = capture.AdventureTaskKey;
                root["adventureTask"] = capture.AdventureTask;
            }
            var references = new JSONArray();
            for (var i = 0; i < LoadoutPresetPlanner.Keys.Length; i++)
            {
                var reference = new JSONObject(); reference["key"] = LoadoutPresetPlanner.Keys[i];
                reference["candidateKey"] = plan.ReferenceKeys[i] == null ? (JSONNode)JSONNull.CreateOrGet() : new JSONString(plan.ReferenceKeys[i]);
                reference["production"] = LoadoutPresetNumber(plan.ReferenceProduction[i]); references.Add(reference);
            }
            root["productionReferences"] = references;
            root["scoring"] = "Sum of normalized goal weights times output retained versus fixed best-found feasible references. Positive specialist references are retained; missing positive references use bounded original-policy gear/support seeds before mixed search. Production goals compare rates; adventure power/toughness and energy/magic cap compare immediate projected native totals. References stay frozen through replay/apply; ratios may exceed 1. Without a positive reference, zero output earns zero and positive output earns one unit of scoring utility, with no defined retained percentage.";
            root["warnings"] = LoadoutPresetArray(capture.Warnings.Concat(new[] {
                "Heuristic recommendations, not an exact global optimum or guaranteed combat safety. Apply sets a farming route once only when farming has positive weight; sequence is preview-only.",
                "One-shot application only. Other automation can immediately replace gear, support or allocations; requires explicit reapply." }));
            root["assumptions"] = LoadoutPresetArray(new[] {
                "Execution time belongs to the plan stage, not the preset. Scheduled acquisition forecasts its remaining timed budget; untimed stages use the remaining plan horizon, or a one-hour comparison when that horizon is exhausted. Standalone presets use a one-hour forecast without an expiry. The forecast context freezes during allocation refresh and is reconsidered only on re-equip.",
                "PreviewId binds one server-owned capture and exact policies for 120 seconds. Apply requires unchanged canonical preset and E/M arrays, inventory instances/positions/stats, slots, unlocks, targets and support material, with matching save/rebirth epoch and no clock rollback. A new preview replaces the old token; successful preflight consumes it before any focus clearing or recall.",
                "Current baseline means current gear under the named current/planned support policy, not observed current production. Fresh apply replays the same exact items, split, ritual, support levels, digger weights and route; it may recalculate numeric allocations for natural NGU progress, gold and pools, never re-search a replacement. Custom plan, auto-rebirth and manual-adventure settings remain unchanged. After complete preflight, Quick focus and idle boss push are cleared and equipment application receipts are invalidated without resetting stage entry or fight-on-entry identity; other automation may immediately replace the one-shot setup.",
                "Actual reclaimable E/M pools exclude basic training; candidate caps can shrink pools. No refill, bar growth, potion expiry or compounding bonus forecast. Both lanes are eligible in all phases.",
                "NGU coefficients match BlackBarAllocator. Each setup allocates once using five-second target/hardcap-aware integer capped sharing, fixed until the next phase/reapply. Bounded tick bands use actual float accumulation costs and certify conservative completed levels within 0.5 percent (small gains exact); uncertifiable projections are unavailable. Captured fractional bars are ignored. Aggregate levels are not unlike bonus utilities; no periodic allocator is promised by one-shot apply.",
                "Auto magic/blood split searches 10-percent increments. Blood uses 50/ceil(1/progressPerTick), unlocked/start-affordable rituals and strictly positive GPS after shared digger drain; captured partial bar timing is ignored.",
                "PP uses native progressGained(floor), not PP/hour. Repeatable floors compare joint PP/boost rates using captured native HP/defense upper rolls, float 1.05f scaling, minimum 0.8 idle damage rolls and floored damage, capped at four hits. Without enemy data only the existing one-hit range is compared. Neutral/reset combat is assumed; regen, paralysis, health/deaths and frame timing are not simulated. Planned PP digger changes rescale captured progress, approximating native ordered rounding. Boosts use the NGU 1.220 Almanac table, filters, recycling and capped/rooted loot. Loot already includes charm.",
                "Lazy ITOPOD purchase and enabled flags are captured and bound to preview identity. When both are true, ITOPOD forecasts use only the native shifter floor for candidate power, not a competing fixed multihit/boost floor. Replay rejects an incompatible pinned floor before mutation. The native shifter remains enabled and may move the floor after later stat changes; no sustained owner or preference override is installed.",
                "Farming objectives share ONE route: regular zones earn boost/zone indices but zero ITOPOD PP, while ITOPOD earns PP/boosts but zero regular-zone index. Zone auto maximizes drop/kill index, not difficulty or exact item rates. Beast Mode farming fails closed without toggling it. Adventure power/toughness instead maximize immediate native stat projections without selecting a route or allocating training. They are not reduced by swap overhead and do not offer two-phase sequences. Safety and hit thresholds are heuristics, not enemy simulation.",
                "Resource getters are projected forward from raw base stats and captured factors, with post-clamp power potions and capacity rounding. Resource/NGU/GPS/drop parity stays exact. Adventure preserves native F-prefix factor precision and cube comparison before float-local stores and clamps; its current getter check permits at most seven binary32 ULPs for fourteen multiplier rounding boundaries, never across clamp endpoints. This is a rounding allowance, not bit-identical combat prediction or inverted totals. Unchanged adventure support uses captured native factors; changed support retains shared fixed-level projection rounding. Respawn uses its real spec and 80-percent cap, not cooldown. Gear apply also recalls R3.",
                "Keep current preserves observed beards and diggers and ignores stored beard/digger planning settings, including BeardRotation. Those settings remain saved for switching back to planned support. Planned support without BeardRotation retains legacy beard priority/auto semantics. In planned support with BeardRotation present, enabled BeardPriority IDs lead in order and only the explicit pool fills spare slots; both arrays empty means no beards. Preview/Apply use the first eligible pool IDs, not RNG or beard search. Planned global ownership rerolls optional slots each minute, preserving required IDs and immutable acquired definitions. Rotation rechecks pinned policy feasibility and allocations through exact support; diggers may be re-funded only within acquired IDs/weights/level ceilings. Applied snapshots are historical; runtime rotation is separate and future rolls/growth are not forecast. Planned support compares at most 64 legal policies; explicit digger weights preserve eligibility/ranking/proportional funding, and automatic portfolios compare immediate effects and reduced budget alternatives. Gold activation precedes digger/blood affordability.",
                "Deterministic bounded multi-start coordinate search alternates full support portfolios and a top-three support beam during gear swaps, with route/split rescored for each trial. Current and specialist focus seeds cross some threshold plateaus, not all. Normalization and best-found reference output/keys freeze before mixed search; replay/apply never renormalizes. Ratios above 1 are possible, not clamped.",
                "Changed gear/support pays initial SwapSeconds and sequence pays any internal gear/support change; allocation-only changes cost zero. Two phases at 25/50/75-percent splits advance cloned NGU levels/remaining targets and keep cap-shrunk pools, recapping once at phase two. Bonus feedback/refill are frozen; other objective rates stay frozen except blood feasibility after shared pool/gold changes." });
            var search = new JSONObject(); search["optimality"] = "heuristic"; search["evaluated"] = plan.Evaluated;
            search["elapsedMilliseconds"] = plan.Milliseconds; search["termination"] = plan.Budget ? "budget" : "complete"; root["search"] = search;
            root["sequence"] = JSONNull.CreateOrGet();
            if (plan.Sequence != null)
            {
                var sequence = new JSONObject(); var q = plan.Sequence;
                sequence["firstKey"] = q.First; sequence["secondKey"] = q.Second;
                sequence["firstSeconds"] = q.FirstSeconds; sequence["secondSeconds"] = q.SecondSeconds; sequence["swapSeconds"] = q.SwapSeconds;
                sequence["metrics"] = LoadoutPresetMetrics(q.Metrics, s, plan, plan.Candidates[0].Metrics, LoadoutPresetPlanner.NormalizeWeights(request.Weights));
                sequence["score"] = LoadoutPresetNumber(q.Score); sequence["improvementPercent"] = LoadoutPresetNumber(q.Improvement); sequence["quality"] = "proxy";
                sequence["assumptions"] = LoadoutPresetArray(new[] { "Two-phase numeric NGU level/target progression with both lanes eligible and no refill/bonus feedback; phase two recaps once after target completion and cap shrink. One shared adventure route per phase. Initial/internal gear-support swaps charged once; allocation-only changes cost zero. Other rates frozen, blood rechecks shared gold/pools. Improvement is against the best displayed single one-shot setup; sequence is not executed." });
                root["sequence"] = sequence;
            }
            if (Encoding.UTF8.GetByteCount(root.ToString()) > 127 * 1024) throw new InvalidOperationException("Preset result exceeds the bounded 127 KiB payload.");
            return root;
        }
    }
}
