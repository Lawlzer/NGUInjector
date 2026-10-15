using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal enum CustomFocusGoal
    {
        AugmentsBloodMagic,
        TimeMachine,
        Wandoos,
        EnergyNgu,
        MagicNgu,
        Ngu,
        AdvancedTraining,
        Adventure,
        Fight,
        ManualTitans
    }

    internal partial class Main
    {
        private const int DefaultFocusExpectedSeconds = 300;
        private const int FocusSettleMilliseconds = 30;

        private const int BeardFuManchu = 0;
        private const int BeardDropChance = 1;
        private const int BeardReverseHitler = 2;
        private const int BeardNgu = 3;
        private const int BeardLadybeard = 4;
        private const int BeardAdventure = 5;
        private const int BeardGold = 6;

        private const int DiggerDropChance = 0;
        private const int DiggerWandoos = 1;
        private const int DiggerStat = 2;
        private const int DiggerAdventure = 3;
        private const int DiggerEnergyNgu = 4;
        private const int DiggerMagicNgu = 5;
        private const int DiggerEnergyBeard = 6;
        private const int DiggerMagicBeard = 7;
        private const int DiggerPp = 8;
        private const int DiggerDaycare = 9;
        private const int DiggerBlood = 10;
        private const int DiggerExp = 11;

        private static readonly Random FocusRandom = new Random();
        private CustomFocusGoal? _customSustainedFocusGoal;
        private CustomFocusGoal? _customRequestedFocusGoal;
        private CustomFocusGoal? _customAppliedFocusGoal;
        private string _customFocusStatus = "idle";
        private int _customFocusAugmentIndex = -1;
        private bool _customFocusAugmentUsesUpgrade;
        private int _customFocusBloodRitualIndex = -1;
        private int[] _customFocusEnergyNguWeights;
        private int[] _customFocusMagicNguWeights;
        private DateTime _customSustainedFocusLastStatus = DateTime.MinValue;
        private DateTime _customFocusLastResourceRefresh = DateTime.MinValue;

        internal CustomFocusGoal? GetActiveCustomFocusGoal()
        {
            return _customRequestedFocusGoal;
        }

        internal string GetCustomFocusStatus() { return _customFocusStatus; }
        internal string GetAppliedCustomFocusGoal() { return _customAppliedFocusGoal?.ToString() ?? string.Empty; }

        internal string ToggleCustomFocusGoal(CustomFocusGoal goal)
        {
            if (_customRequestedFocusGoal == goal)
                return StopCustomFocusGoal();

            return ApplyCustomFocusGoal(goal);
        }

        internal string StopCustomFocusGoal()
        {
            if (!_customRequestedFocusGoal.HasValue)
                return "No sustained focus is active.";

            var goal = _customRequestedFocusGoal.Value;
            var label = FocusGoalLabel(goal);
            var releaseMessage = " Released focused resources.";
            try
            {
                if (SustainedFocusEnabled(goal)) RecallCustomSustainedFocusResources(goal);
                if (goal == CustomFocusGoal.Fight) StopBossPush();
            }
            catch (Exception e)
            {
                releaseMessage = " Resource recall failed; focus was still stopped.";
                LogError($"Stop custom focus {goal}", e);
            }

            ClearCustomSustainedFocus();
            var message = $"Stopped {label} focus.{releaseMessage}";
            SetCustomAutomationStatus(message);
            Log(message);
            PublishExternalUiRuntime();
            return message;
        }

        internal string ApplyCustomFocusGoal(CustomFocusGoal goal)
        {
            var messages = new List<string>();
            try
            {
                if (SustainedFocusEnabled(goal) && LoadReadyNguFocusPlan(goal) == null)
                    throw new InvalidOperationException("NGU focus requires loaded resource data and a readable active plan.");
                _customRequestedFocusGoal = goal;
                _customAppliedFocusGoal = null;
                _customSustainedFocusGoal = null;
                _customFocusStatus = "pending";
                ResetCustomSustainedFocusState();
                PauseCustomRebirthForManualFocus(messages);
                StopBossPush();
                PublishExternalUiRuntime();

                switch (goal)
                {
                    case CustomFocusGoal.AugmentsBloodMagic:
                        messages.Add(PrepareFocusGear(GearOptimizationGoal.Augments));
                        messages.Add(EquipFocusBeards(goal));
                        messages.Add(EquipFocusDiggers(goal));
                        break;
                    case CustomFocusGoal.TimeMachine:
                        messages.Add(PrepareFocusGear(GearOptimizationGoal.TimeMachine));
                        messages.Add(EquipFocusBeards(goal));
                        messages.Add(EquipFocusDiggers(goal));
                        break;
                    case CustomFocusGoal.Wandoos:
                        messages.Add(PrepareFocusGear(GearOptimizationGoal.Wandoos));
                        messages.Add(EquipFocusBeards(goal));
                        messages.Add(EquipFocusDiggers(goal));
                        break;
                    case CustomFocusGoal.EnergyNgu:
                        messages.Add(PrepareFocusGear(GearOptimizationGoal.EnergyNgu));
                        messages.Add(EquipFocusBeards(goal));
                        messages.Add(EquipFocusDiggers(goal));
                        break;
                    case CustomFocusGoal.MagicNgu:
                        messages.Add(PrepareFocusGear(GearOptimizationGoal.MagicNgu));
                        messages.Add(EquipFocusBeards(goal));
                        messages.Add(EquipFocusDiggers(goal));
                        break;
                    case CustomFocusGoal.Ngu:
                        messages.Add(PrepareFocusGear(GearOptimizationGoal.AllNgu));
                        messages.Add(EquipFocusBeards(goal));
                        messages.Add(EquipFocusDiggers(goal));
                        break;
                    case CustomFocusGoal.AdvancedTraining:
                        messages.Add(PrepareFocusGear(GearOptimizationGoal.AdvancedTraining));
                        messages.Add(EquipFocusBeards(goal));
                        messages.Add(EquipFocusDiggers(goal));
                        break;
                    case CustomFocusGoal.Adventure:
                        messages.Add(PrepareFocusGear(GearOptimizationGoal.Fight));
                        messages.Add(EquipFocusBeards(goal));
                        messages.Add(EquipFocusDiggers(goal));
                        break;
                    case CustomFocusGoal.Fight:
                        messages.Add(PrepareFocusGear(GearOptimizationGoal.Fight));
                        messages.Add(EquipFocusBeards(goal));
                        messages.Add(EquipFocusDiggers(goal));
                        StartBossPush();
                        messages.Add("started boss push");
                        break;
                    case CustomFocusGoal.ManualTitans:
                        messages.Add(PrepareFocusGear(GearOptimizationGoal.Fight));
                        messages.Add(EquipFocusBeards(goal));
                        messages.Add(EquipFocusDiggers(goal));
                        messages.Add("manual titan focus ready");
                        break;
                }

                if (SustainedFocusEnabled(goal))
                    messages.Add(AllocateCustomFocusResources(goal));

                if (goal == CustomFocusGoal.Adventure)
                {
                    Settings.CustomAdventureEnabled = true;
                    RouteConfiguredAdventureGoal("Manual Adventure focus");
                }

                SetSustainedFocus(goal, messages);
                _customRequestedFocusGoal = goal;
                _customAppliedFocusGoal = goal;
                _customFocusStatus = "applied";

                var summary = $"Focus {FocusGoalLabel(goal)}: {string.Join("; ", messages.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray())}";
                SetCustomAutomationStatus(summary);
                Log(summary);
                PublishExternalUiRuntime();
                return summary;
            }
            catch (Exception e)
            {
                ClearCustomSustainedFocus();
                _customRequestedFocusGoal = goal;
                _customFocusStatus = "failed";
                var error = LogError($"Apply custom focus {goal}", e);
                SetCustomAutomationStatus(error);
                PublishExternalUiRuntime();
                return error;
            }
        }

        private void SetSustainedFocus(CustomFocusGoal goal, List<string> messages)
        {
            if (SustainedFocusEnabled(goal))
            {
                _customSustainedFocusGoal = goal;
                messages.Add("sustained resource focus active");
                return;
            }

            ClearCustomSustainedFocus();
        }

        private static bool SustainedFocusEnabled(CustomFocusGoal goal)
        {
            switch (goal)
            {
                case CustomFocusGoal.TimeMachine:
                case CustomFocusGoal.AugmentsBloodMagic:
                case CustomFocusGoal.Wandoos:
                case CustomFocusGoal.EnergyNgu:
                case CustomFocusGoal.MagicNgu:
                case CustomFocusGoal.Ngu:
                case CustomFocusGoal.AdvancedTraining:
                case CustomFocusGoal.Adventure:
                    return true;
                default:
                    return false;
            }
        }

        private void RunCustomSustainedFocusAllocation()
        {
            RunCustomSustainedFocusAllocation(false);
        }

        private void RunCustomSustainedFocusAllocation(bool recallResources)
        {
            if (Settings == null || Character?.rebirthTime == null)
                return;

            if (Settings.UseCustomRebirthPlan)
            {
                ClearCustomSustainedFocus();
                return;
            }

            if (!_customSustainedFocusGoal.HasValue)
                return;

            if (CustomQuickResourceRefreshBlocked(null))
                return;

            try
            {
                var goal = _customSustainedFocusGoal.Value;
                var plan = LoadReadyNguFocusPlan(goal);
                if (plan == null)
                    return;
                var weightsChanged = goal != CustomFocusGoal.MagicNgu && (_customFocusEnergyNguWeights == null || !plan.EnergyNguWeights.SequenceEqual(_customFocusEnergyNguWeights))
                    || _customFocusMagicNguWeights == null || !plan.MagicNguWeights.SequenceEqual(_customFocusMagicNguWeights);
                if (!weightsChanged && !recallResources
                    && (DateTime.UtcNow - _customFocusLastResourceRefresh).TotalSeconds < CustomQuickResourceRefreshSeconds)
                    return;
                if (weightsChanged)
                {
                    _customFocusLastResourceRefresh = DateTime.MinValue;
                    _customSustainedFocusLastStatus = DateTime.MinValue;
                }

                // Even the idle-resource tick must rebalance to preempt lower priorities as caps grow.
                var message = AllocateCustomFocusResources(goal, recallResources);
                if (!string.IsNullOrEmpty(message) && ShouldUpdateCustomFocusStatus())
                    SetCustomAutomationStatus($"Sustaining {FocusGoalLabel(goal)}: {message}");
                if (weightsChanged)
                    PublishExternalUiRuntime();
            }
            catch (Exception e)
            {
                LogError($"Sustain custom focus {_customSustainedFocusGoal.Value}", e);
                ClearCustomSustainedFocus();
            }
        }

        private void ClearCustomSustainedFocus()
        {
            _customSustainedFocusGoal = null;
            _customRequestedFocusGoal = null;
            _customAppliedFocusGoal = null;
            _customFocusStatus = "idle";
            ResetCustomSustainedFocusState();
        }

        private void ResetCustomSustainedFocusState()
        {
            BlackBarAllocator.ResetExcessIdle();
            _customFocusAugmentIndex = -1;
            _customFocusAugmentUsesUpgrade = false;
            _customFocusBloodRitualIndex = -1;
            _customFocusEnergyNguWeights = null;
            _customFocusMagicNguWeights = null;
            _customSustainedFocusLastStatus = DateTime.MinValue;
            _customFocusLastResourceRefresh = DateTime.MinValue;
        }

        private static void RecallCustomSustainedFocusResources(CustomFocusGoal goal, bool settle = true)
        {
            BlackBarAllocator.ResetExcessIdle();
            switch (goal)
            {
                case CustomFocusGoal.TimeMachine:
                case CustomFocusGoal.AugmentsBloodMagic:
                case CustomFocusGoal.Wandoos:
                case CustomFocusGoal.EnergyNgu:
                case CustomFocusGoal.Ngu:
                case CustomFocusGoal.AdvancedTraining:
                case CustomFocusGoal.Adventure:
                    BlackBarAllocator.ClearNguAppliedWeights(true);
                    Character.removeMostEnergy();
                    if (settle) FocusSettle();
                    BlackBarAllocator.ClearNguAppliedWeights(false);
                    Character.removeMostMagic();
                    if (settle) FocusSettle();
                    break;
                case CustomFocusGoal.MagicNgu:
                    BlackBarAllocator.ClearNguAppliedWeights(false);
                    Character.removeMostMagic();
                    if (settle) FocusSettle();
                    break;
            }
        }

        private bool ShouldUpdateCustomFocusStatus()
        {
            var now = DateTime.UtcNow;
            if ((now - _customSustainedFocusLastStatus).TotalSeconds < 1d)
                return false;

            _customSustainedFocusLastStatus = now;
            return true;
        }

        private void RememberAugmentFocusTarget(AugmentOptimizerResult result)
        {
            if (result == null || !result.Success)
                return;

            _customFocusAugmentIndex = result.AugmentIndex;
            _customFocusAugmentUsesUpgrade = result.UsesUpgrade;
        }

        private void RememberBloodFocusTarget(BloodMagicOptimizerResult result)
        {
            if (result == null || !result.Success)
                return;

            _customFocusBloodRitualIndex = result.RitualIndex;
        }

        private string AllocateCustomFocusResources(CustomFocusGoal goal, bool settle = true)
        {
            var messages = new List<string>();
            var plan = LoadReadyNguFocusPlan(goal);
            if (plan == null)
                throw new InvalidOperationException("NGU focus requires loaded resource data and a readable active plan.");
            var energyWeights = plan.EnergyNguWeights.ToArray();
            var magicWeights = plan.MagicNguWeights.ToArray();
            RecallCustomSustainedFocusResources(goal, settle);
            foreach (var energy in new[] {true, false})
            {
                CustomStageFocus focus;
                switch (goal)
                {
                    case CustomFocusGoal.TimeMachine:
                        focus = CustomStageFocus.TimeMachine;
                        break;
                    case CustomFocusGoal.AugmentsBloodMagic:
                        focus = energy ? CustomStageFocus.Augments : CustomStageFocus.BloodMagic;
                        break;
                    case CustomFocusGoal.Wandoos:
                        focus = CustomStageFocus.Wandoos;
                        break;
                    case CustomFocusGoal.MagicNgu:
                        focus = energy ? CustomStageFocus.None : CustomStageFocus.MagicNgu;
                        break;
                    case CustomFocusGoal.EnergyNgu:
                    case CustomFocusGoal.Ngu:
                        focus = energy ? CustomStageFocus.EnergyNgu : CustomStageFocus.MagicNgu;
                        break;
                    case CustomFocusGoal.AdvancedTraining:
                    case CustomFocusGoal.Adventure:
                        focus = energy ? CustomStageFocus.AdvancedTraining : CustomStageFocus.BloodMagic;
                        break;
                    default:
                        continue;
                }

                if (focus == CustomStageFocus.None)
                    continue;

                var priorities = BlackBarAllocator.DefaultPriorities(focus, energy);
                var resolved = new List<string>();
                foreach (var token in priorities)
                {
                    if (energy && token == "AUG")
                    {
                        if (EnsureCustomFocusAugmentTarget())
                        {
                            resolved.Add("AUG-" + _customFocusAugmentIndex * 2);
                            if (_customFocusAugmentUsesUpgrade)
                                resolved.Add("AUG-" + (_customFocusAugmentIndex * 2 + 1));
                        }
                    }
                    else
                    {
                        resolved.Add(token);
                    }
                }

                var message = BlackBarAllocator.AllocatePriorities(resolved.ToArray(), energy,
                    BlackBarAllocator.DefaultLookaheadSeconds, energy ? energyWeights : magicWeights,
                    (available, goldBudget) => EnsureCustomFocusBloodTarget(available, goldBudget) ? _customFocusBloodRitualIndex : -1);
                if (!string.IsNullOrWhiteSpace(message))
                    messages.Add(message);
            }

            if (goal != CustomFocusGoal.MagicNgu)
                _customFocusEnergyNguWeights = energyWeights;
            _customFocusMagicNguWeights = magicWeights;
            _customFocusLastResourceRefresh = DateTime.UtcNow;
            return messages.Count == 0 ? null : string.Join("; ", messages.ToArray());
        }

        private bool EnsureCustomFocusAugmentTarget()
        {
            var result = _customFocusAugmentIndex >= 0
                ? AugmentOptimizer.OptimizeTarget(_customFocusAugmentIndex, _customFocusAugmentUsesUpgrade,
                    CustomAugmentsBloodEstimateSeconds(), false)
                : null;
            if (result == null || !result.Success)
            {
                _customFocusAugmentIndex = -1;
                _customFocusAugmentUsesUpgrade = false;
                result = AugmentOptimizer.OptimizeBest(CustomAugmentsBloodEstimateSeconds(), false);
            }

            RememberAugmentFocusTarget(result);
            return result.Success;
        }

        private static int CustomAugmentsBloodEstimateSeconds()
        {
            return Settings == null ? DefaultFocusExpectedSeconds : Settings.CustomAugmentsBloodEstimateSeconds;
        }

        private bool EnsureCustomFocusBloodTarget(long availableMagic, double goldBudget)
        {
            var result = _customFocusBloodRitualIndex >= 0
                ? BloodMagicOptimizer.OptimizeTarget(_customFocusBloodRitualIndex, availableMagic, goldBudget)
                : null;
            if (result == null || !result.Success)
            {
                _customFocusBloodRitualIndex = -1;
                result = BloodMagicOptimizer.OptimizeBest(availableMagic, goldBudget);
            }

            RememberBloodFocusTarget(result);
            return result.Success;
        }

        private static string FocusGoalLabel(CustomFocusGoal goal)
        {
            switch (goal)
            {
                case CustomFocusGoal.AugmentsBloodMagic:
                    return "Augments + Blood Magic";
                case CustomFocusGoal.TimeMachine:
                    return "Time Machine";
                case CustomFocusGoal.Wandoos:
                    return "Wandoos";
                case CustomFocusGoal.EnergyNgu:
                    return "Energy NGU";
                case CustomFocusGoal.MagicNgu:
                    return "Magic NGU";
                case CustomFocusGoal.Ngu:
                    return "All NGU";
                case CustomFocusGoal.AdvancedTraining:
                    return "Advanced Training";
                case CustomFocusGoal.Adventure:
                    return "Adventure";
                case CustomFocusGoal.Fight:
                    return "Fight";
                case CustomFocusGoal.ManualTitans:
                    return "Manual Titans";
                default:
                    return goal.ToString();
            }
        }

        private void PauseCustomRebirthForManualFocus(List<string> messages)
        {
            if (Settings.AutoRebirth)
            {
                Settings.AutoRebirth = false;
                messages.Add("auto-rebirth paused");
            }

            if (Settings.UseCustomRebirthPlan)
            {
                Settings.UseCustomRebirthPlan = false;
                messages.Add("custom rebirth plan paused");
            }

            ResetCustomPreRebirthPrep();
            _customStageKey = null;
            _customStageGearDone = false;
            CustomStageAllocator.ResetResourceTargets();
        }

        private string PrepareFocusGear(GearOptimizationGoal goal)
        {
            if (!LoadoutManager.CanSwap())
                return $"gear skipped: loadout lock {LoadoutManager.CurrentLock}";

            LoadoutManager.RemoveResourcesForGearSwap();
            FocusSettle();
            var result = GearOptimizer.OptimizeAndEquip(goal, GetActiveFightScoreMode(), false, true);
            FocusSettle();
            if (!result.Success)
                throw new InvalidOperationException(result.Message);
            return result.Message;
        }

        private static void FocusSettle()
        {
            Thread.Sleep(FocusSettleMilliseconds);
        }

        private string EquipFocusBeards(CustomFocusGoal goal)
        {
            var beards = new List<int> {BeardDropChance, BeardNgu, BeardAdventure, BeardGold};
            switch (goal)
            {
                case CustomFocusGoal.Wandoos:
                    beards.Add(BeardLadybeard);
                    break;
                case CustomFocusGoal.Fight:
                case CustomFocusGoal.ManualTitans:
                    beards.Add(BeardFuManchu);
                    break;
                default:
                    beards.Add(RandomOptionalBeard());
                    break;
            }

            return BeardManager.EquipBeards(beards);
        }

        private static int RandomOptionalBeard()
        {
            var optional = new[] {BeardReverseHitler, BeardLadybeard, BeardFuManchu};
            return optional[FocusRandom.Next(optional.Length)];
        }

        private string EquipFocusDiggers(CustomFocusGoal goal)
        {
            var diggers = new List<DiggerManager.DiggerRequest>
            {
                Digger(DiggerDropChance, 20, "drop"),
                Digger(DiggerAdventure, 20, "adventure"),
                Digger(DiggerEnergyBeard, 15, "energy beard"),
                Digger(DiggerMagicBeard, 15, "magic beard"),
                Digger(DiggerPp, 15, "PP"),
                Digger(DiggerDaycare, 15, "daycare")
            };

            switch (goal)
            {
                case CustomFocusGoal.AugmentsBloodMagic:
                    diggers.Add(Digger(DiggerBlood, 100, "blood"));
                    break;
                case CustomFocusGoal.Wandoos:
                    diggers.Add(Digger(DiggerWandoos, 100, "wandoos"));
                    break;
                case CustomFocusGoal.EnergyNgu:
                    diggers.Add(Digger(DiggerEnergyNgu, 100, "energy NGU"));
                    break;
                case CustomFocusGoal.MagicNgu:
                    diggers.Add(Digger(DiggerMagicNgu, 1900, "magic NGU"));
                    break;
                case CustomFocusGoal.Ngu:
                    diggers.Add(Digger(DiggerEnergyNgu, 100, "energy NGU"));
                    diggers.Add(Digger(DiggerMagicNgu, 1900, "magic NGU"));
                    break;
                case CustomFocusGoal.Fight:
                    diggers.Add(Digger(DiggerStat, 100, "stat"));
                    break;
                case CustomFocusGoal.ManualTitans:
                    diggers.Clear();
                    diggers.Add(Digger(DiggerAdventure, 10000, "adventure"));
                    diggers.Add(Digger(DiggerDropChance, 1000, "drop"));
                    break;
            }

            try
            {
                if (ZoneHelpers.TitansSpawningSoon().SpawningSoon)
                    diggers.Add(Digger(DiggerExp, 1000, "EXP titan"));
            }
            catch
            {
                // Titan timing is opportunistic; focus application should still proceed if it cannot be checked.
            }

            return DiggerManager.EquipWeightedDiggers(diggers, 0.75d);
        }

        private static DiggerManager.DiggerRequest Digger(int id, int weight, string name)
        {
            return new DiggerManager.DiggerRequest {Id = id, Weight = weight, Name = name};
        }

        private CustomPlan LoadReadyNguFocusPlan(CustomFocusGoal goal)
        {
            if (Settings == null || CustomPlanStore == null || Character?.rebirthTime == null || Character.NGU == null
                || goal != CustomFocusGoal.MagicNgu && (Character.NGU.skills == null || Character.NGU.skills.Count < 9)
                || Character.magic == null || Character.NGU.magicSkills == null || Character.NGU.magicSkills.Count < 7)
                return null;
            try
            {
                var plan = LoadRuntimeCustomPlan();
                return plan?.SourceJson == null ? null : plan;
            }
            catch
            {
                // A bad plan is not permission to recall resources or execute default weights.
                return null;
            }
        }
    }
}
