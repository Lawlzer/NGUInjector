using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

// Only game/controller, gear preparation, settings, and publication boundaries are replaced.
// The sustain handler, weight snapshots, cap formulas, priority allocator, and plan CAS are real.
namespace NGUInjector
{
    internal enum difficulty { normal, evil, sadistic }
    internal enum specType { Augs }
    internal enum cardBonus { TMSpeed, augSpeed, energyNGUSpeed, magicNGUSpeed }

    internal sealed class FocusSettings
    {
        internal bool UseCustomRebirthPlan, AutoRebirth, CustomAdventureEnabled;
        internal bool ManageEnergy = true, ManageMagic = true;
        internal string CustomPlanFile = "active";
        internal int CustomAugmentsBloodEstimateSeconds = 300;
        internal void ApplyPatch(JSONNode patch) { CustomPlanFile = patch["_customPlanFile"].Value; }
    }

    internal partial class Main
    {
        internal static Main reference;
        internal static FocusSettings Settings;
        internal static CustomPlanStore CustomPlanStore;
        internal static FocusCharacter Character;
        internal static bool IronPillBloodReserved;
        private string _customStageKey;
        private bool _customStageGearDone;
        private const double CustomQuickResourceRefreshSeconds = 2d;
        internal bool Blocked;
        internal int Publications, Invalidations;
        internal JSONObject PublishedRuntime;
        internal CustomPlan LastLoadedPlan;
        internal static string Status;

        internal void Begin(CustomFocusGoal goal)
        {
            _customSustainedFocusGoal = _customRequestedFocusGoal = _customAppliedFocusGoal = goal;
            _customFocusStatus = "applied";
            var plan = LoadRuntimeCustomPlan();
            _customFocusEnergyNguWeights = goal == CustomFocusGoal.MagicNgu ? null : plan.EnergyNguWeights.ToArray();
            _customFocusMagicNguWeights = plan.MagicNguWeights.ToArray();
            _customFocusLastResourceRefresh = DateTime.UtcNow;
            _customSustainedFocusLastStatus = DateTime.UtcNow;
        }
        internal void Tick() { RunCustomSustainedFocusAllocation(false); }
        internal void Allocate(CustomFocusGoal goal) { AllocateCustomFocusResources(goal, false); }
        internal int[] AppliedEnergyWeights { get { return _customFocusEnergyNguWeights; } }
        internal int[] AppliedMagicWeights { get { return _customFocusMagicNguWeights; } }
        internal DateTime LastRefresh { get { return _customFocusLastResourceRefresh; } }
        internal void MarkJustRefreshed() { _customFocusLastResourceRefresh = DateTime.UtcNow; }
        internal void MarkRefreshDue() { _customFocusLastResourceRefresh = DateTime.MinValue; }
        internal void InvalidateCustomRuntimePlanCache(bool resetStageRuntime = true) { Invalidations++; if (resetStageRuntime) CustomStageAllocator.ResetStageRuntime(); }
        private CustomPlan LoadRuntimeCustomPlan() { return LastLoadedPlan = CustomPlanStore.Load(Settings.CustomPlanFile); }
        private bool CustomQuickResourceRefreshBlocked(CustomPlan plan) { return Blocked; }
        private static void PublishExternalUiRuntime()
        {
            reference.Publications++;
            var root = new JSONObject();
            // Publication is a boundary stub; built-assembly checks cover the real telemetry wrapper.
            JSONObject ngu;
            try
            {
                var plan = reference.LoadRuntimeCustomPlan();
                ngu = BlackBarAllocator.GetNguRuntime(plan.EnergyNguWeights, plan.MagicNguWeights);
                foreach (var energy in new[] {true, false})
                {
                    var applied = energy ? reference._customFocusEnergyNguWeights : reference._customFocusMagicNguWeights;
                    var configured = energy ? plan.EnergyNguWeights : plan.MagicNguWeights;
                    var lane = energy ? "energy" : "magic";
                    ngu[lane + "WeightsApplied"] = applied != null && applied.SequenceEqual(configured);
                    if (applied != null)
                    {
                        var weights = new JSONArray();
                        foreach (var weight in applied) weights.Add(weight);
                        ngu[lane + "AppliedWeights"] = weights;
                    }
                }
            }
            catch (Exception error)
            {
                ngu = new JSONObject();
                ngu["available"] = false;
                ngu["reason"] = error.Message;
            }
            root["nguAllocation"] = ngu;
            root["activeFocusGoal"] = reference.GetActiveCustomFocusGoal()?.ToString() ?? string.Empty;
            root["focusStatus"] = reference.GetCustomFocusStatus();
            reference.PublishedRuntime = root;
        }
        internal static void Log(string message) { }
        internal static string LogError(string context, Exception e) { return "ERROR in " + context + ": " + e.Message; }
        internal static void SetCustomAutomationStatus(string message) { Status = message; }
        private void StopBossPush() { }
        private void StartBossPush() { }
        private void ResetCustomPreRebirthPrep() { }
        private void RouteConfiguredAdventureGoal(string reason) { }
        private FightScoreMode GetActiveFightScoreMode() { return FightScoreMode.SqrtPowerToughness; }
    }

    internal sealed class FocusCharacter
    {
        internal long idleEnergy;
        internal Magic magic = new Magic();
        internal NguData NGU = new NguData();
        internal Clock rebirthTime = new Clock();
        internal GameSettings settings = new GameSettings();
        internal Buttons buttons = new Buttons();
        internal Machine machine = new Machine();
        internal Wandoos wandoos98 = new Wandoos();
        internal WandoosController wandoos98Controller = new WandoosController();
        internal MachineController timeMachineController = new MachineController();
        internal Challenges challenges = new Challenges(), allChallenges = new Challenges();
        internal NguController NGUController = new NguController();
        internal AdventureController adventureController = new AdventureController();
        internal Inventory inventory = new Inventory();
        internal InventoryController inventoryController = new InventoryController();
        internal BonusController hacksController = new BonusController(), cardsController = new BonusController(),
            wishesController = new BonusController(), allDiggers = new BonusController(), beastQuestPerkController = new BonusController();
        internal AugmentController augmentsController = new AugmentController();
        internal AugmentData augments = new AugmentData();
        internal BloodData bloodMagic = new BloodData();
        internal BloodController bloodMagicController = new BloodController();
        internal Training advancedTraining = new Training();
        internal TrainingController advancedTrainingController = new TrainingController();
        internal Wishes wishes = new Wishes();
        internal EnergyMagicPanel energyMagicPanel = new EnergyMagicPanel();
        internal int bossID = 100, EnergyRecalls, MagicRecalls;
        internal double realGold = 1e9;
        internal double GoldPerSecond = 1;
        internal float totalEnergyPower() { return 1; }
        internal float totalMagicPower() { return 1; }
        internal float totalNGUSpeedBonus() { return 1; }
        internal float totalWandoosEnergySpeed() { return 1; }
        internal float totalWandoosMagicSpeed() { return 1; }
        internal float totalAdvancedTrainingSpeedBonus() { return 1; }
        internal double grossGoldPerSecond() { return GoldPerSecond; }
        internal void removeMostEnergy()
        {
            EnergyRecalls++;
            foreach (var skill in NGU.skills) { idleEnergy += skill.energy; skill.energy = 0; }
        }
        internal void removeMostMagic()
        {
            MagicRecalls++;
            foreach (var skill in NGU.magicSkills) { magic.idleMagic += skill.magic; skill.magic = 0; }
            foreach (var ritual in bloodMagic.ritual) { magic.idleMagic += ritual.magic; ritual.magic = 0; }
            magic.idleMagic += wandoos98.wandoosMagic;
            wandoos98.wandoosMagic = 0;
        }
    }
    internal sealed class Clock { internal double totalseconds; }
    internal sealed class Magic { internal long idleMagic, curMagic; }
    internal sealed class ResourceInput { internal string text; }
    internal sealed class EnergyMagicPanel { internal ResourceInput energyRequested = new ResourceInput(); internal void validateInput() { } }
    internal sealed class GameSettings { internal difficulty nguLevelTrack, rebirthDifficulty; }
    internal sealed class Button { internal bool interactable; }
    internal sealed class Buttons
    {
        internal Button ngu = new Button {interactable = true}, wandoos = new Button(), brokenTimeMachine = new Button(),
            augmentation = new Button(), advancedTraining = new Button(), bloodMagic = new Button();
    }
    internal sealed class Skill { internal long energy, magic, level, evilLevel, sadisticLevel, target, evilTarget, sadisticTarget; }
    internal sealed class NguData
    {
        internal List<Skill> skills = Enumerable.Range(0, 9).Select(_ => new Skill()).ToList();
        internal List<Skill> magicSkills = Enumerable.Range(0, 7).Select(_ => new Skill()).ToList();
    }
    internal sealed class NguBar
    {
        internal bool ThrowOnUpdate;
        internal float sadisticDivider() { return 1; }
        internal void updateText() { if (ThrowOnUpdate) throw new InvalidOperationException("NGU UI unavailable"); }
    }
    internal sealed class NguController
    {
        internal NguBar[] NGU = Enumerable.Range(0, 9).Select(_ => new NguBar()).ToArray();
        internal NguBar[] NGUMagic = Enumerable.Range(0, 7).Select(_ => new NguBar()).ToArray();
        internal float energyNGUBonus() { return 1; }
        internal float magicNGUBonus() { return 1; }
        internal float energySpeedDivider(int index) { return 1000; }
        internal float magicSpeedDivider(int index) { return 1000; }
    }
    internal sealed class BonusController
    {
        internal float totalTMSpeedBonus() { return 1; }
        internal float totalAugSpeedBonus() { return 1; }
        internal float totalEnergyNGUBonus() { return 1; }
        internal float totalMagicNGUBonus() { return 1; }
        internal float totalEnergyNGUSpeed() { return 1; }
        internal float totalMagicNGUSpeed() { return 1; }
        internal float getBonus(cardBonus bonus) { return 1; }
    }
    internal sealed class AdventureController { internal BonusController itopod = new BonusController(); }
    internal sealed class Inventory { internal double[] macguffinBonuses = Enumerable.Repeat(1d, 13).ToArray(); }
    internal sealed class InventoryController { internal Dictionary<specType, float> bonuses = new Dictionary<specType, float> {{specType.Augs, 0}}; }
    internal sealed class Challenge
    {
        internal bool inChallenge;
        internal int maxCompletions = 100;
        internal int completions() { return 0; }
        internal int evilCompletions() { return 0; }
        internal int sadisticCompletions() { return 0; }
        internal float TMSpeedBonus() { return 1; }
    }
    internal sealed class Challenges { internal Challenge timeMachineChallenge = new Challenge(), noAugsChallenge = new Challenge(), trollChallenge = new Challenge(); }
    internal sealed class Machine { internal long speedTarget, multiTarget, levelSpeed, levelGoldMulti, speedEnergy, goldMultiMagic; }
    internal sealed class MachineController
    {
        internal float baseSpeedDivider() { return 1; }
        internal float baseGoldMultiDivider() { return 1; }
        internal float sadisticDivider() { return 1; }
        internal void updateSpeedText() { }
        internal void updateGoldMultiText() { }
    }
    internal sealed class Wandoos { internal bool disabled; internal long wandoosEnergy, wandoosMagic; }
    internal sealed class WandoosController
    {
        internal float baseEnergyTime() { return 1; }
        internal float baseMagicTime() { return 1; }
        internal void updateText() { }
    }
    internal sealed class AugmentBar
    {
        internal bool augLocked() { return false; }
        internal bool upgradeLocked() { return false; }
        internal bool hitAugmentTarget() { return false; }
        internal bool hitUpgradeTarget() { return false; }
        internal float getAugCost() { return 1; }
        internal float getUpgradeCost() { return 1; }
        internal float sadisticDivider() { return 1; }
        internal void updateAugTexts() { }
        internal void updateUpgradeTexts() { }
    }
    internal sealed class AugmentController
    {
        internal AugmentBar[] augments = Enumerable.Range(0, 7).Select(_ => new AugmentBar()).ToArray();
        internal float[] normalUpgradeSpeedDividers = new float[7], evilUpgradeSpeedDividers = new float[7], sadisticUpgradeSpeedDividers = new float[7],
            normalAugSpeedDividers = new float[7], evilAugSpeedDividers = new float[7], sadisticAugSpeedDividers = new float[7];
    }
    internal sealed class AugmentLevel { internal float augProgress, upgradeProgress; internal long augLevel, upgradeLevel, augmentTarget, upgradeTarget, augEnergy, upgradeEnergy; }
    internal sealed class AugmentData { internal AugmentLevel[] augs = Enumerable.Range(0, 7).Select(_ => new AugmentLevel()).ToArray(); }
    internal sealed class Ritual { internal int boss; internal float progress; internal long magic; }
    internal sealed class BloodData { internal List<Ritual> ritual = Enumerable.Range(0, 8).Select(_ => new Ritual()).ToList(); }
    internal sealed class RitualController
    {
        internal float currentCost() { return 1; }
        internal float bloodAdded() { return 1; }
        internal void add() { throw new Exception("Full ritual controller add is not used by priority allocation."); }
        internal float totalBloodMagicSpeedBonus() { return 1; }
        internal float sadisticDivider() { return 1; }
        internal void updateBloodMagicText() { }
    }
    internal sealed class BloodController
    {
        internal RitualController[] bloodMagics = Enumerable.Range(0, 8).Select(_ => new RitualController()).ToArray();
        internal float[] normalSpeedDividers = new float[8], evilSpeedDividers = new float[8], sadisticSpeedDividers = new float[8];
        internal int ritualsUnlocked() { return 8; }
        internal void updateMenu() { }
    }
    internal sealed class Training { internal long[] energy = new long[5], level = new long[5], levelTarget = new long[5]; }
    internal sealed class TrainingController
    {
        internal Managers.AdvancedTrainingController defense = new Managers.AdvancedTrainingController(), attack = new Managers.AdvancedTrainingController(),
            block = new Managers.AdvancedTrainingController(), wandoosEnergy = new Managers.AdvancedTrainingController(), wandoosMagic = new Managers.AdvancedTrainingController();
    }
    internal sealed class Wish { internal int level; }
    internal sealed class Wishes { internal Wish[] wishes = Enumerable.Range(0, 191).Select(_ => new Wish()).ToArray(); }
}

namespace NGUInjector.Managers
{
    internal enum GearOptimizationGoal { EnergyCap, MagicCap, EnergyNgu, MagicNgu, AllNgu, TimeMachine, Blood, Gold, Drop, Fight, Augments, AdvancedTraining, Wandoos, ManualAdventure }
    internal enum FightScoreMode { SqrtPowerToughness, PowerTimesToughness, PowerPlusToughness }
    internal sealed class GearResult { internal bool Success = true; internal string Message = "gear"; }
    internal static class GearOptimizer
    {
        internal static Dictionary<int, string> GoalLabels = Enum.GetValues(typeof(GearOptimizationGoal)).Cast<GearOptimizationGoal>().ToDictionary(x => (int)x, x => x.ToString());
        internal static Dictionary<int, string> FightScoreModeLabels = Enum.GetValues(typeof(FightScoreMode)).Cast<FightScoreMode>().ToDictionary(x => (int)x, x => x.ToString());
        internal static GearResult OptimizeAndEquip(GearOptimizationGoal goal, FightScoreMode mode, bool save, bool skip) { throw new Exception("Sustain must not equip gear."); }
    }
    internal sealed class AugmentOptimizerResult { internal bool Success, UsesUpgrade; internal int AugmentIndex; internal string Message; }
    internal static class AugmentOptimizer
    {
        internal static AugmentOptimizerResult OptimizeBest(int seconds, bool preview)
        {
            if (!Main.Character.buttons.augmentation.interactable) return new AugmentOptimizerResult();
            throw new Exception("NGU sustain must not optimize unlocked augments.");
        }
        internal static AugmentOptimizerResult OptimizeTarget(int index, bool upgrade, int seconds, bool preview) { throw new Exception("NGU sustain must not optimize augments."); }
    }
    internal sealed class AdvancedTrainingController { internal float baseTime = 1; internal void updateText() { } }
    internal enum LockType { None }
    internal static class LoadoutManager
    {
        internal static LockType CurrentLock;
        internal static bool CanSwap() { return true; }
        internal static void RemoveResourcesForGearSwap() { throw new Exception("Sustain must not swap gear."); }
    }
    internal static class BeardManager { internal static string EquipBeards(IEnumerable<int> ids) { throw new Exception("Sustain must not equip beards."); } }
    internal static class DiggerManager
    {
        internal sealed class DiggerRequest { internal int Id, Weight; internal string Name; }
        internal static string EquipWeightedDiggers(IEnumerable<DiggerRequest> diggers, double budget) { throw new Exception("Sustain must not equip diggers."); }
    }
    internal sealed class TitanWindow { internal bool SpawningSoon; }
    internal static class ZoneHelpers { internal static TitanWindow TitansSpawningSoon() { return new TitanWindow(); } }
}
