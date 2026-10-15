using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NGUInjector.AllocationProfiles.RebirthStuff;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private const int ProbeFuManchuBeardId = 0;
        private const int StartupCloudLoadTimeoutSeconds = 45;
        private const int StartupMainMenuFinishTimeoutSeconds = 10;
        private const float StartupMainMenuVisiblePositionThreshold = 1000f;

        internal void RunStartupAutomationRequests()
        {
            StartCoroutine(RunStartupAutomationRequestsRoutine());
        }

        private IEnumerator RunStartupAutomationRequestsRoutine()
        {
            var cloudLoadRequested = ConsumeStartupCloudLoadRequest();
            if (cloudLoadRequested)
            {
                yield return StartCoroutine(TryStartupCloudLoadRoutine());
            }

            RunStartupProbeIfRequested();
        }

        private bool ConsumeStartupCloudLoadRequest()
        {
            var path = GetCloudLoadAutorunPath();
            if (!File.Exists(path))
                return false;

            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                LogError("delete cloud-load autorun flag", e);
            }

            return true;
        }

        private IEnumerator TryStartupCloudLoadRoutine()
        {
            Log("Startup cloud-load requested");
            var deadline = DateTime.Now.AddSeconds(StartupCloudLoadTimeoutSeconds);
            while (DateTime.Now < deadline)
            {
                var result = TryStartupCloudLoadOnce();
                if (result == StartupCloudLoadResult.Loaded)
                {
                    Log("Startup cloud-load invoked; waiting for game state to settle");
                    yield return new UnityEngine.WaitForSeconds(3f);
                    yield return StartCoroutine(TryFinishStartupMainMenuRoutine());
                    yield break;
                }

                if (result == StartupCloudLoadResult.Unavailable)
                {
                    Log("Startup cloud-load skipped: cloud save is unavailable");
                    yield break;
                }

                yield return new UnityEngine.WaitForSeconds(1f);
            }

            Log("Startup cloud-load timed out waiting for main menu/cloud save readiness");
        }

        private IEnumerator TryFinishStartupMainMenuRoutine()
        {
            var deadline = DateTime.Now.AddSeconds(StartupMainMenuFinishTimeoutSeconds);
            while (DateTime.Now < deadline)
            {
                var result = TryFinishStartupMainMenuOnce();
                if (result == StartupMainMenuFinishResult.Finished)
                {
                    yield return new UnityEngine.WaitForSeconds(1f);
                    yield break;
                }

                if (result == StartupMainMenuFinishResult.Done)
                    yield break;

                yield return new UnityEngine.WaitForSeconds(0.5f);
            }

            Log("Startup main-menu finish skipped: no visible continue prompt appeared after cloud load");
        }

        private StartupMainMenuFinishResult TryFinishStartupMainMenuOnce()
        {
            try
            {
                if (TryInvokeStartupContinueButton())
                    return StartupMainMenuFinishResult.Finished;

                var menu = Character?.mainMenu;
                var menuObject = menu?.mainMenu;
                if (menu == null || menuObject == null)
                    return StartupMainMenuFinishResult.Waiting;

                var position = menuObject.transform.localPosition;
                var menuIsVisible = Math.Abs(position.x) < StartupMainMenuVisiblePositionThreshold
                    && Math.Abs(position.y) < StartupMainMenuVisiblePositionThreshold;
                if (!menuIsVisible)
                    return StartupMainMenuFinishResult.Waiting;

                menu.finishMainMenu();
                Log("Startup main-menu finish invoked");
                return StartupMainMenuFinishResult.Finished;
            }
            catch (Exception e)
            {
                LogError("startup main-menu finish", e);
                return StartupMainMenuFinishResult.Done;
            }
        }

        private bool TryInvokeStartupContinueButton()
        {
            var buttons = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>();
            foreach (var button in buttons)
            {
                if (button == null || !button.IsActive() || !button.interactable)
                    continue;

                var text = button.GetComponentInChildren<UnityEngine.UI.Text>();
                if (NormalizeStartupButtonText(text?.text) != "ontothegame")
                    continue;

                button.onClick.Invoke();
                Log($"Startup continue button invoked: {text.text}");
                return true;
            }

            return false;
        }

        private static string NormalizeStartupButtonText(string text)
        {
            return new string((text ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private StartupCloudLoadResult TryStartupCloudLoadOnce()
        {
            try
            {
                var menu = Character?.mainMenu;
                if (menu == null)
                    return StartupCloudLoadResult.Waiting;

                if (menu.doneInitialLoad && !menu.getCloudSaveValidity())
                    return StartupCloudLoadResult.Unavailable;

                if (!menu.getCloudSaveValidity())
                {
                    menu.cloudSaveCheck();
                    return StartupCloudLoadResult.Waiting;
                }

                menu.loadCloudSave();
                return StartupCloudLoadResult.Loaded;
            }
            catch (Exception e)
            {
                LogError("startup cloud-load", e);
                return StartupCloudLoadResult.Unavailable;
            }
        }

        internal void RunStartupProbeIfRequested()
        {
            var path = GetProbeAutorunPath();
            if (!File.Exists(path))
                return;

            try
            {
                var mode = File.ReadAllText(path).Trim().ToLowerInvariant();
                File.Delete(path);

                string result;
                if (mode == "commands" || mode == "command" || mode == "probe")
                {
                    result = RunProbeCommandFile();
                }
                else
                {
                    result = RunSafeCustomProbe();
                }

                settingsForm?.ReportCustomStatus(result);
                Log($"Startup probe result: {result}");
            }
            catch (Exception e)
            {
                LogError("startup probe", e);
            }
        }

        internal string RunSafeCustomProbe()
        {
            var lines = new List<string>();
            lines.Add("NGUInjector custom safe probe");
            lines.Add($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add("Mode: read-only diagnostics; no gear, digger, fruit, beard, boss, or rebirth actions are executed.");
            lines.Add(string.Empty);

            AddProbeStep(lines, "core state", () =>
            {
                lines.Add($"Character found: {Character != null}");
                lines.Add($"Inventory controller found: {Controller != null}");
                lines.Add($"Settings loaded: {Settings != null}");
                lines.Add($"Custom plan store loaded: {CustomPlanStore != null}");
                lines.Add($"Legacy global enabled: {Settings?.GlobalEnabled}");
                lines.Add($"Custom auto-rebirth enabled: {Settings?.AutoRebirth}");
                lines.Add($"Custom plan running: {Settings?.UseCustomRebirthPlan}");
                if (Character != null && Character.adventure != null)
                {
                    lines.Add($"Adventure idle autoattack: {Character.adventure.autoattacking}");
                    lines.Add($"Adventure current zone: {ZoneName(Character.adventure.zone)}");
                }

                if (Settings != null)
                {
                    lines.Add($"Adventure goal enabled: {Settings.CustomAdventureEnabled}");
                    lines.Add($"Adventure goal mode: {CustomAdventureModeLabel(Settings.CustomAdventureMode)}");
                    lines.Add($"Adventure farm zone: {ZoneName(Settings.CustomAdventureZone)}");
                    lines.Add($"Adventure gold push: {Settings.CustomAdventureGoldPush}");
                    lines.Add($"Manual adventure enabled: {Settings.ManualAdventureEnabled}");
                    lines.Add($"Manual adventure zone: {ZoneName(Settings.ManualAdventureZone)}");
                    lines.Add($"Manual adventure boss-only: {Settings.ManualAdventureBossOnly}");
                }
            });

            AddProbeStep(lines, "adventure routing", () => AddAdventureRoutingProbe(lines));

            AddProbeStep(lines, "active custom plan", () =>
            {
                var plan = LoadActivePlanForProbe();
                lines.Add($"Plan file: {Settings.CustomPlanFile}");
                lines.Add($"Plan display name: {plan.Name}");
                lines.Add($"Plan version: {plan.Version}");
                lines.Add($"Generated equipment goal: {GoalLabel(plan.GearGoal)}");
                lines.Add($"Fight scoring: {FightScoreModeLabel((FightScoreMode) plan.FightScoreMode)}");
                lines.Add($"Plan auto-rebirth enabled: {plan.AutoRebirthEnabled}");
                lines.Add($"Auto activate fruits: {plan.AutoActivateFruits}");
                lines.Add($"Fruit activation eligible now: {CustomFruitActivationEligible(plan)}");
                lines.Add($"Planned rebirth seconds for fruit gate: {CustomPlannedRebirthSeconds(plan)}");
                lines.Add($"Auto boost enabled: {Settings.CustomAutoBoostEnabled}");
                lines.Add($"Auto boost cube ratio: {Settings.CustomAutoBoostCubePowerRatio}:1 Power:Toughness");
                lines.Add($"Auto Money Pit enabled: {Settings.CustomAutoMoneyPitEnabled}");
                lines.Add($"Auto blood spells enabled: {Settings.CustomAutoBloodSpellsEnabled}");
                lines.Add($"Blood Spaghetti goal: {Settings.CustomBloodSpaghettiGoalPercent}%");
                lines.Add($"Counterfeit Gold goal: {Settings.CustomBloodCounterfeitGoalPercent}%");
                lines.Add($"Augments + Blood estimate: {Settings.CustomAugmentsBloodEstimateSeconds}s");
                lines.Add($"Energy NGU weights (indices 0..8): {string.Join(",", plan.EnergyNguWeights)}");
                lines.Add($"Magic NGU weights (indices 0..6): {string.Join(",", plan.MagicNguWeights)}");
                lines.Add($"NUMBER rebirth confirmation delay: {CustomPlanRebirth.NumberTriggerConfirmSeconds:0}s");
                lines.Add($"Pre-rebirth prep: always on; only maxed Fruit of Power alpha: {plan.PrepRequireMaxedFruit}");
            });

            AddProbeStep(lines, "gear optimizer dry run", () =>
            {
                var goal = ActiveProbeGoal();
                var fightScoreMode = ActiveProbeFightScoreMode();
                var result = GearOptimizer.Optimize(goal, fightScoreMode);
                lines.Add($"Active goal: {GoalLabel((int) goal)}");
                if (goal == GearOptimizationGoal.Fight)
                    lines.Add($"Fight scoring: {FightScoreModeLabel(fightScoreMode)}");
                lines.Add($"Success: {result.Success}");
                lines.Add($"Message: {result.Message}");
                lines.Add($"Score: {result.Score:0.###}");
                lines.Add($"Checked combinations: {result.EvaluatedCombinations:n0}");
                lines.Add($"Selected item IDs: {string.Join(",", result.ItemIds.Select(x => x.ToString()).ToArray())}");
            });

            AddProbeStep(lines, "fight gear dry run", () =>
            {
                var fightScoreMode = ActiveProbeFightScoreMode();
                var result = GearOptimizer.Optimize(GearOptimizationGoal.Fight, fightScoreMode);
                lines.Add($"Fight scoring: {FightScoreModeLabel(fightScoreMode)}");
                lines.Add($"Success: {result.Success}");
                lines.Add($"Message: {result.Message}");
                lines.Add($"Score: {result.Score:0.###}");
                lines.Add($"Selected item IDs: {string.Join(",", result.ItemIds.Select(x => x.ToString()).ToArray())}");
            });

            AddProbeStep(lines, "augment optimizer dry run", () => AddAugmentPreviewProbe(lines));

            AddProbeStep(lines, "Yggdrasil fruits", () => AddFruitProbe(lines));
            AddProbeStep(lines, "Fu Manchu beard", () => AddBeardProbe(lines));
            AddProbeStep(lines, "stat digger", () => AddStatDiggerProbe(lines));
            AddProbeStep(lines, "boss push state", () => AddBossProbe(lines));
            AddProbeStep(lines, "Money Pit / Daily Spin", () => AddPitSpinProbe(lines));
            AddProbeStep(lines, "manual rebirth availability", () =>
            {
                lines.Add($"Manual rebirth available: {new ManualRebirth().RebirthAvailable()}");
                lines.Add($"Rebirth time: {Character.rebirthTime.totalseconds:0.0}s");
                lines.Add($"Minimum rebirth time: {Character.rebirth.minRebirthTime():0.0}s");
            });

            return FinishProbe(lines, "safe-probe");
        }

        private void AddAdventureRoutingProbe(List<string> lines)
        {
            if (Character == null || Settings == null)
            {
                lines.Add("Adventure routing unavailable: missing character or settings.");
                return;
            }

            var effectiveBoss = ZoneHelpers.CurrentRebirthEffectiveBossId();
            var highestNormal = ZoneHelpers.GetMaxReachableZone(false);
            var highestIncludingTitans = ZoneHelpers.GetMaxReachableZone(true);
            var farmUnlocked = ZoneHelpers.IsZoneUnlocked(Settings.CustomAdventureZone);
            var manualUnlocked = ZoneHelpers.IsZoneUnlocked(Settings.ManualAdventureZone);
            var goldPushEligible = CustomAdventureGoldPushAllowed(out var goldPushUnavailableReason) && highestNormal >= 0;
            var boostFarmTarget = BoostFarmHelper.GetBestTarget();

            lines.Add($"Boss ID: {Character.bossID}");
            lines.Add($"Effective boss ID: {effectiveBoss}");
            lines.Add($"Highest unlocked non-titan zone: {ZoneName(highestNormal)}; requires boss {ZoneHelpers.ZoneUnlockEffectiveBossId(highestNormal)}");
            lines.Add($"Highest unlocked zone including titans: {ZoneName(highestIncludingTitans)}; requires boss {ZoneHelpers.ZoneUnlockEffectiveBossId(highestIncludingTitans)}");
            lines.Add($"Configured farm zone unlocked: {farmUnlocked}");
            lines.Add($"Manual adventure target unlocked: {manualUnlocked}");
            lines.Add(Settings.ManualAdventureEnabled && !manualUnlocked && Settings.ManualAdventureAllowFallback && highestNormal >= 0
                ? $"Manual adventure fallback now: {ZoneName(highestNormal)}"
                : "Manual adventure fallback now: none");
            lines.Add($"Boost farm target now: {BoostFarmHelper.FormatTarget(boostFarmTarget)}");
            lines.Add($"Gold push eligible now: {goldPushEligible}");
            if (!goldPushEligible && !string.IsNullOrWhiteSpace(goldPushUnavailableReason))
                lines.Add($"Gold push unavailable reason: {goldPushUnavailableReason}");
            lines.Add(goldPushEligible
                ? $"Gold push target now: {ZoneName(highestNormal)}"
                : "Gold push target now: none");
            lines.Add($"Gold push active: {_customAdventureGoldPushActive}");
            lines.Add($"Gold push active zone: {_customAdventureGoldPushZone}");
            lines.Add($"Gold push attempted zone this rebirth: {_customAdventureAttemptedGoldPushZone}");
        }

        internal string RunProbeCommandFile()
        {
            var commandPath = GetProbeCommandPath();
            if (!File.Exists(commandPath))
            {
                File.WriteAllText(commandPath, ProbeTemplate());
                return $"Created probe command template: {commandPath}";
            }

            var lines = new List<string>();
            lines.Add("NGUInjector custom command probe");
            lines.Add($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"Command file: {commandPath}");
            lines.Add("Mode: explicit commands. Some commands can change game state.");
            lines.Add(string.Empty);

            var commands = File.ReadAllLines(commandPath);
            for (var i = 0; i < commands.Length; i++)
            {
                var command = commands[i].Trim();
                if (command.Length == 0 || command.StartsWith("#"))
                    continue;

                var lineNumber = i + 1;
                AddProbeStep(lines, $"command line {lineNumber}: {command}", () => RunProbeCommand(lines, command));
            }

            return FinishProbe(lines, "command-probe");
        }

        private static void AddProbeStep(List<string> lines, string name, Action action)
        {
            lines.Add($"[{name}]");
            try
            {
                action();
            }
            catch (Exception e)
            {
                lines.Add(LogError($"probe step '{name}'", e));
                lines.Add(e.ToString());
            }

            lines.Add(string.Empty);
        }

        private void RunProbeCommand(List<string> lines, string command)
        {
            var parts = command.Split(new[] {' '}, 2, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts[0].Trim().ToLowerInvariant();
            var arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            switch (verb)
            {
                case "help":
                    lines.Add(ProbeTemplate());
                    return;
                case "status":
                    AddFruitProbe(lines);
                    AddBeardProbe(lines);
                    AddStatDiggerProbe(lines);
                    AddBossProbe(lines);
                    AddPitSpinProbe(lines);
                    return;
                case "optimize":
                    ProbeOptimize(lines, arg, false);
                    return;
                case "gear-audit":
                    foreach (var item in Character.inventory.GetConvertedEquips().Concat(Character.inventory.GetConvertedInventory())
                        .Where(x => x.id >= 40 && x.equipment.isEquipment()))
                        AddGearItemProbe(lines, item);
                    return;
                case "augment-preview":
                    AddAugmentPreviewProbe(lines);
                    return;
                case "equip":
                    ProbeOptimize(lines, arg, true);
                    return;
                case "fight":
                case "fight-prep":
                    lines.Add(RunManualFightPrep());
                    return;
                case "rebirth":
                    lines.Add(RunManualRebirth());
                    return;
                case "fruit-alpha":
                    lines.Add(YggdrasilManager.ConsumeFruitOfPowerAlpha(false)
                        ? "Consumed Fruit of Power alpha."
                        : "Fruit of Power alpha was not ready or unavailable.");
                    return;
                case "fu-manchu":
                    lines.Add(BeardManager.EnableFuManchu(true) ? "Enabled Fu Manchu." : "Fu Manchu unavailable.");
                    return;
                case "stat-digger":
                    DiggerManager.EquipDiggers(new[] {StatDiggerId});
                    lines.Add("Equipped stat digger.");
                    return;
                case "clear-diggers":
                    DiggerManager.ClearDiggers();
                    lines.Add("Cleared active diggers.");
                    return;
                case "boss-push":
                    StartBossPush();
                    lines.Add("Started boss push.");
                    return;
                case "stop-boss-push":
                    StopBossPush();
                    lines.Add("Stopped boss push.");
                    return;
                case "money-pit":
                    lines.Add(MoneyPitManager.CheckMoneyPit());
                    return;
                case "daily-spin":
                    lines.Add(MoneyPitManager.DoDailySpin());
                    return;
                default:
                    lines.Add($"Unknown probe command '{verb}'. Use 'help' for supported commands.");
                    return;
            }
        }

        private void ProbeOptimize(List<string> lines, string goalText, bool equip)
        {
            ParseProbeOptimizeArgs(goalText, out var goal, out var fightScoreMode);
            var result = equip ? GearOptimizer.OptimizeAndEquip(goal, fightScoreMode) : GearOptimizer.Optimize(goal, fightScoreMode);
            lines.Add($"Goal: {GoalLabel((int) goal)}");
            if (goal == GearOptimizationGoal.Fight)
                lines.Add($"Fight scoring: {FightScoreModeLabel(fightScoreMode)}");
            lines.Add($"Equip: {equip}");
            lines.Add($"Success: {result.Success}");
            lines.Add($"Message: {result.Message}");
            lines.Add($"Search: {result.EvaluatedCombinations:n0} complete scores, {result.VisitedNodes:n0} nodes, {result.PrunedBranches:n0} pruned branches, {result.ElapsedMilliseconds:n0} ms");
            lines.Add($"Score: {result.Score:0.###}");
            lines.Add($"Selected item IDs: {string.Join(",", result.ItemIds.Select(x => x.ToString()).ToArray())}");
            foreach (var item in result.Items)
                AddGearItemProbe(lines, item);
            if (equip)
                lines.Add($"Actually equipped item IDs: {string.Join(",", Character.inventory.GetConvertedEquips().Select(x => x.id.ToString()).ToArray())}");
        }

        private static void AddGearItemProbe(List<string> lines, ih item)
        {
            var equipment = item.equipment;
            lines.Add($"Item: {item.name} ({item.id}), {equipment.type}, source slot {item.slot}");
            lines.Add($"  Raw specs: {equipment.spec1Type}={equipment.spec1Cur:0.###}, {equipment.spec2Type}={equipment.spec2Cur:0.###}, {equipment.spec3Type}={equipment.spec3Cur:0.###}");
            foreach (var type in new[] {equipment.spec1Type, equipment.spec2Type, equipment.spec3Type}.Distinct().Where(x => x != specType.None))
                lines.Add($"  Effective {type}: {Controller.getBonusFactor(Controller.equipSpecBonus(type, equipment), type) * 100f:0.######}%");
        }

        private static void AddFruitProbe(List<string> lines)
        {
            var controller = Character.yggdrasilController;
            var interactable = Character.buttons.yggdrasil.interactable;
            var hasFruitController = controller?.fruits != null && controller.fruits.Length > 0 && controller.fruits[0] != null;
            lines.Add($"Yggdrasil interactable: {interactable}");
            lines.Add($"Fruit controller available: {hasFruitController}");
            if (!hasFruitController)
                return;

            var fruit = controller.fruits[0];
            AddFruitState(lines, YggdrasilManager.FruitOfPowerAlphaId);
            AddFruitState(lines, YggdrasilManager.FruitOfPowerBetaId);

            var fruits = Character.yggdrasil?.fruits;
            if (fruits == null)
                return;

            lines.Add("All unlocked fruits:");
            for (var i = 0; i < fruits.Count; i++)
            {
                var model = fruits[i];
                if (model.maxTier == 0L)
                    continue;

                lines.Add(FruitStateSummary(i));
            }
        }

        private static void AddFruitState(List<string> lines, int fruitId)
        {
            lines.Add(FruitStateSummary(fruitId));
        }

        private static string FruitStateSummary(int fruitId)
        {
            var controller = Character.yggdrasilController;
            var fruits = Character.yggdrasil?.fruits;
            if (fruits == null || fruitId < 0 || fruitId >= fruits.Count || controller?.fruits == null || controller.fruits.Length == 0 || controller.fruits[0] == null)
                return $"{YggdrasilManager.FruitName(fruitId)} ({fruitId}): unavailable";

            var model = fruits[fruitId];
            var fruit = controller.fruits[0];
            var resource = controller.usesEnergy[fruitId] ? "energy" : "magic";
            var currentResource = controller.usesEnergy[fruitId]
                ? Math.Max(Character.curEnergy, Character.idleEnergy)
                : Math.Max(Character.magic.curMagic, Character.magic.idleMagic);
            var affordable = currentResource >= controller.activationCost[fruitId];
            return $"{YggdrasilManager.FruitName(fruitId)} ({fruitId}): maxTier {model.maxTier}, active {model.activated}, permanent {model.permCostPaid}, harvest tier {fruit.harvestTier(fruitId)}, maxed {fruit.fruitMaxxed(fruitId)}, activation {controller.activationCost[fruitId]:n0} {resource}, affordable {affordable}";
        }

        private static void AddBeardProbe(List<string> lines)
        {
            var beards = Character.beards;
            var allBeards = Character.allBeards;
            lines.Add($"Beards interactable: {Character.buttons.beards.interactable}");
            lines.Add($"Beards model available: {beards != null}");
            lines.Add($"AllBeards controller available: {allBeards != null}");
            if (beards == null || allBeards == null)
                return;

            lines.Add($"Beards disabled: {beards.disabled}");
            lines.Add($"Fu Manchu index valid: {ProbeFuManchuBeardId < beards.beards.Count}");
            if (ProbeFuManchuBeardId < beards.beards.Count)
            {
                lines.Add($"Fu Manchu active: {beards.beards[ProbeFuManchuBeardId].active}");
            }
            lines.Add($"Active beard slots: {beards.activeBeards.Count}/{allBeards.capBeards()}");
        }

        private static void AddAugmentPreviewProbe(List<string> lines)
        {
            var expectedSeconds = Settings == null ? 300 : Settings.CustomAugmentsBloodEstimateSeconds;
            var result = AugmentOptimizer.OptimizeBest(expectedSeconds);
            lines.Add($"Success: {result.Success}");
            lines.Add($"Estimate seconds: {expectedSeconds}");
            lines.Add($"Message: {result.Message}");
            if (!result.Success)
                return;

            lines.Add($"Augment pair: {result.AugmentIndex}");
            lines.Add($"Uses upgrade: {result.UsesUpgrade}");
            lines.Add($"Energy to augment: {result.EnergyToAugment:0}");
            lines.Add($"Energy to upgrade: {result.EnergyToUpgrade:0}");
            lines.Add($"Expected seconds: {result.ExpectedSeconds:0.#}");
            lines.Add($"Seconds to next: {result.Seconds:0.#}");
            lines.Add($"Progress fraction: {result.ProgressFraction * 100:0.#}%");
            lines.Add($"Energy power: {result.EnergyPower:0.###}");
            lines.Add($"Augment multiplier: {result.AugmentSpeedMultiplier:0.###}");
            lines.Add($"Projected work: {result.ProjectedWork:0.###}");
            lines.Add($"Expected value: {result.ExpectedValue:0.###}");
            lines.Add($"Value per second: {result.ValuePerSecond:0.###}");
            lines.Add($"Projected gold cost: {result.ProjectedGoldCost:0.###}");
            lines.Add($"Projected gold budget: {result.ProjectedGoldBudget:0.###}");
            lines.Add($"Net gold per second: {result.GoldPerSecond:0.###}");
        }

        private static void AddStatDiggerProbe(List<string> lines)
        {
            var diggers = Character.diggers?.diggers;
            lines.Add($"Digger list available: {diggers != null}");
            if (diggers == null)
                return;

            lines.Add($"Stat digger ID: {StatDiggerId}");
            lines.Add($"Stat digger index valid: {StatDiggerId < diggers.Count}");
            if (StatDiggerId < diggers.Count)
            {
                lines.Add($"Stat digger max level: {diggers[StatDiggerId].maxLevel}");
                lines.Add($"Stat digger current level: {diggers[StatDiggerId].curLevel}");
                lines.Add($"Stat digger active: {diggers[StatDiggerId].active}");
                lines.Add($"Stat digger upgrade cost: {Character.allDiggers.upgradeCost(StatDiggerId):0}");
            }
            lines.Add($"Active digger slots: {Character.diggers.activeDiggers.Count}/{Character.allDiggers.maxDiggerSlots()}");
            lines.Add($"Digger swapping available: {DiggerManager.CanSwap()}");
        }

        private static void AddBossProbe(List<string> lines)
        {
            var bc = Character.bossController;
            lines.Add($"Boss ID: {Character.bossID}");
            lines.Add($"Current HP: {Character.curHP:0}");
            lines.Add($"Boss fighting: {bc.isFighting}");
            lines.Add($"Boss nuking: {bc.nukeBoss}");
            lines.Add($"Character attack: {bc.character.attack:0}");
            lines.Add($"Character defense: {bc.character.defense:0}");
            lines.Add($"Boss attack: {bc.character.bossAttack:0}");
            lines.Add($"Boss defense: {bc.character.bossDefense:0}");
        }

        private static void AddPitSpinProbe(List<string> lines)
        {
            lines.Add($"Money Pit ready: {Character.pit.pitTime.totalseconds >= Character.pitController.currentPitTime()}");
            lines.Add($"Money Pit timer: {Character.pit.pitTime.totalseconds:0.0}s / {Character.pitController.currentPitTime():0.0}s");
            lines.Add($"Current gold: {Character.realGold:0}");
            lines.Add($"Rebirth time: {Character.rebirthTime.totalseconds:0.0}s");
            lines.Add($"Daily Spin ready: {Character.daily.spinTime.totalseconds >= Character.dailyController.targetSpinTime()}");
            lines.Add($"Daily Spin timer: {Character.daily.spinTime.totalseconds:0.0}s / {Character.dailyController.targetSpinTime():0.0}s");
        }

        private CustomPlan LoadActivePlanForProbe()
        {
            if (CustomPlanStore == null)
                throw new InvalidOperationException("Custom plan store is not loaded.");

            return CustomPlanStore.Load(Settings.CustomPlanFile);
        }

        private GearOptimizationGoal ActiveProbeGoal()
        {
            var plan = LoadActivePlanForProbe();
            return (GearOptimizationGoal) plan.GearGoal;
        }

        private FightScoreMode ActiveProbeFightScoreMode()
        {
            var plan = LoadActivePlanForProbe();
            return GearOptimizer.FightScoreModeLabels.ContainsKey(plan.FightScoreMode)
                ? (FightScoreMode) plan.FightScoreMode
                : FightScoreMode.SqrtPowerToughness;
        }

        private void ParseProbeOptimizeArgs(string text, out GearOptimizationGoal goal, out FightScoreMode fightScoreMode)
        {
            goal = ActiveProbeGoal();
            fightScoreMode = ActiveProbeFightScoreMode();
            if (string.IsNullOrWhiteSpace(text))
                return;

            var remaining = text.Trim();
            foreach (var entry in GearOptimizer.GoalLabels.OrderByDescending(x => x.Value.Length))
            {
                if (TryConsumeProbeToken(remaining, entry.Value, out var rest) ||
                    TryConsumeProbeToken(remaining, ((GearOptimizationGoal) entry.Key).ToString(), out rest))
                {
                    goal = (GearOptimizationGoal) entry.Key;
                    remaining = rest;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(remaining))
            {
                fightScoreMode = ParseFightScoreMode(remaining);
            }
        }

        private static bool TryConsumeProbeToken(string text, string token, out string remaining)
        {
            text = text.Trim();
            token = token.Trim();
            remaining = text;
            if (text.Length < token.Length || !text.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                return false;

            if (text.Length > token.Length && !char.IsWhiteSpace(text[token.Length]))
                return false;

            remaining = text.Substring(token.Length).Trim();
            return true;
        }

        private static GearOptimizationGoal ParseProbeGoal(string text)
        {
            var normalized = text.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
            foreach (var goal in GearOptimizer.GoalLabels)
            {
                var label = goal.Value.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
                if (string.Equals(label, normalized, StringComparison.OrdinalIgnoreCase))
                    return (GearOptimizationGoal) goal.Key;

                if (string.Equals(((GearOptimizationGoal) goal.Key).ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                    return (GearOptimizationGoal) goal.Key;
            }

            throw new ArgumentException($"Unknown gear goal '{text}'. Valid goals: {string.Join(", ", GearOptimizer.GoalLabels.Values.ToArray())}");
        }

        private static FightScoreMode ParseFightScoreMode(string text)
        {
            var raw = text.Trim().ToLowerInvariant();
            var normalized = new string(raw.Where(char.IsLetterOrDigit).ToArray());
            if (raw.Contains("+") || normalized == "sum" || normalized == "powerplustoughness" || normalized == "attackplusdefense")
                return FightScoreMode.PowerPlusToughness;

            if (raw.Contains("*") || raw.Contains(" x ") || normalized == "product" || normalized == "powertimestoughness" ||
                normalized == "powerxtoughness" || normalized == "attacktimesdefense" || normalized == "attackxdefense")
                return FightScoreMode.PowerTimesToughness;

            if (normalized.StartsWith("sqrt") || normalized == "balanced" || normalized == "geomean" || normalized == "geometricmean")
                return FightScoreMode.SqrtPowerToughness;

            throw new ArgumentException($"Unknown fight scoring mode '{text}'. Valid modes: sqrt, product, sum.");
        }

        private static string GoalLabel(int goal)
        {
            return GearOptimizer.GoalLabels.ContainsKey(goal) ? GearOptimizer.GoalLabels[goal] : $"Unknown ({goal})";
        }

        private static string FightScoreModeLabel(FightScoreMode mode)
        {
            return GearOptimizer.FightScoreModeLabel(mode);
        }

        private static string CustomAdventureModeLabel(int mode)
        {
            if (mode == 1)
                return "Farm zone";

            if (mode == 2)
                return "Farm boosts";

            return "Optimal ITOPOD";
        }

        private string FinishProbe(List<string> lines, string prefix)
        {
            var logDir = Path.Combine(GetBaseDir(), "logs");
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllLines(path, lines.ToArray());

            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    Log(line);
            }

            return $"Probe complete: {path}";
        }

        private static string GetProbeCommandPath()
        {
            return Path.Combine(GetBaseDir(), "probe.txt");
        }

        private static string GetProbeAutorunPath()
        {
            return Path.Combine(GetBaseDir(), "probe.autorun");
        }

        private static string GetCloudLoadAutorunPath()
        {
            return Path.Combine(GetBaseDir(), "cloud-load.autorun");
        }

        private enum StartupCloudLoadResult
        {
            Waiting,
            Loaded,
            Unavailable
        }

        private enum StartupMainMenuFinishResult
        {
            Waiting,
            Finished,
            Done
        }

        private static string ProbeTemplate()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "# NGUInjector probe commands. One command per line. Lines starting with # are ignored.",
                "# Safe/read-only:",
                "#   status",
                "#   augment-preview",
                "#   optimize <goal> [sqrt|product|sum]",
                "#   gear-audit (owned gear raw specs and effective percentages)",
                "# Live actions:",
                "#   equip <goal> [sqrt|product|sum]",
                "#   fight-prep",
                "#   fruit-alpha",
                "#   fu-manchu",
                "#   stat-digger",
                "#   clear-diggers",
                "#   money-pit",
                "#   daily-spin",
                "#   boss-push",
                "#   stop-boss-push",
                "#   rebirth",
                "# Goals: " + string.Join(", ", GearOptimizer.GoalLabels.Values.ToArray()),
                "status",
                "optimize Fight"
            });
        }
    }
}
