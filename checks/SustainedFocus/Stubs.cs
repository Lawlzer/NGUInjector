using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

// Only game/controller, gear preparation, settings, and publication boundaries are replaced.
// The sustain handler, weight snapshots, cap formulas, allocator, scheduler core and plan CAS are real.
// The adapter below excludes production epoch validation, persistence and automation-loop wiring.
namespace NGUInjector
{
    internal enum difficulty { normal, evil, sadistic }
    internal enum specType { Augs }
    internal enum cardBonus { TMSpeed, augSpeed, energyNGUSpeed, magicNGUSpeed }

    internal sealed class FocusSettings
    {
        internal bool UseCustomRebirthPlan, AutoRebirth, CustomAdventureEnabled, ManualAdventureEnabled;
        internal bool ManageEnergy = true, ManageMagic = true, ManageR3 = true;
        internal bool CustomAutoBloodSpellsEnabled;
        internal int CustomBloodSpaghettiGoalPercent, CustomBloodCounterfeitGoalPercent;
        internal int CustomIronPillSaveSeconds;
        internal double CustomIronPillMinWorth;
        internal string CustomPlanFile = "active";
        internal int CustomAugmentsBloodEstimateSeconds = 300;
        internal int[] EnergyNguWeights = {0, 0, 0, 0, 1, 0, 1, 0, 0};
        internal int[] MagicNguWeights = {1, 0, 0, 0, 0, 0, 0};
        internal void ApplyPatch(JSONNode patch) { CustomPlanFile = patch["_customPlanFile"].Value; }
    }

    internal partial class Main
    {
        internal static Main reference;
        internal static FocusSettings Settings;
        internal static CustomPlanStore CustomPlanStore;
        internal static FocusCharacter Character;
        internal static InventoryController Controller => Character?.inventoryController;
        internal static bool IronPillBloodReserved;
        private string _customStageKey;
        // The scheduler adapter below is not the production inline executor.
        private void InvalidateInlineStageApplication() { }
        private bool RefreshInlineStageSupport(CustomPlan plan) { return false; }
        private const double CustomQuickResourceRefreshSeconds = 2d;
        internal bool Blocked;
        internal int Publications, Invalidations;
        internal JSONObject PublishedRuntime;
        internal CustomPlan LastLoadedPlan;
        internal static string Status;
        internal bool _bossPushRunning, _manualRebirthPending, _customPreRebirthPrepRunning,
            _customTitanGoldActive, _customTitanFightActive;
        internal bool RebirthReady;
        private bool _unloaded;
        private bool _manualTitanActive;
        internal bool BossPushRunning => _bossPushRunning;
        internal bool CombatPrepForChecks;
        private bool CombatPrepActive => CombatPrepForChecks;
        private bool GlobalPresetOwnsActivity(CustomPlan plan) => false;
        // Overlay policy is compiled in checks/ManualAdventureOverlay, not this beard/focus harness.
        internal bool ManualAdventureOverlayActive = false;
        internal bool ManualAdventureSupportOverridesActive = false;
        internal int ManualAdventureOverlayResets;
        private void ResetManualAdventureOverlay() { ManualAdventureOverlayResets++; }
        private string StopManualGlobalPreset() => null;
        private void TickCombatPrep() { }
        // Automatic recovery is exercised against compiled production in CombatPrepIntegration.
        private void ServiceAutomaticRecovery() { }
        private void RequireCombatPrepIdle() { }
        // Combat/readiness are external boundaries here; their real coordination
        // is exercised by the compiled StageSupportChecks, not these beard checks.
        private static bool CustomBossFightActive()
        {
            var boss = Character?.bossController;
            return boss != null && (boss.isFighting || boss.nukeBoss);
        }
        private bool CustomRebirthReady(CustomPlan plan) { return RebirthReady; }
        private ProtectedStageScheduler _checkScheduler;
        private string _checkPlanFile;
        private double _checkHeartbeat;
        internal string SchedulerStateForChecks => _checkScheduler?.ExportState().ToString();

        internal void SetTimingForChecks(double elapsed, double heartbeat)
        {
            Character.rebirthTime.totalseconds = elapsed;
            _checkHeartbeat = heartbeat;
        }

        internal CustomStageRuntime GetProtectedStage(CustomPlan plan)
        {
            if (Character?.rebirthTime == null || Settings == null) return null;
            if (_checkScheduler == null || _checkPlanFile != Settings.CustomPlanFile)
            {
                _checkScheduler = new ProtectedStageScheduler();
                _checkPlanFile = Settings.CustomPlanFile;
            }
            var elapsed = Character.rebirthTime.totalseconds;
            var previous = _checkScheduler.StageIndex;
            var canCredit = !Blocked && _checkScheduler.Matches(plan) && previous >= 0
                && CustomStageAllocator.StageApplicationEligible(plan, plan.Stages[previous], 0, 0);
            _checkScheduler.Update(plan, elapsed, _checkHeartbeat, canCredit,
                index => CustomStageAllocator.AdvancedTrainingStatsReached(plan.Stages[index]));
            var index = _checkScheduler.StageIndex;
            if (index < 0) return null;
            var duration = _checkScheduler.Durations[index];
            return new CustomStageRuntime {
                Stage = plan.Stages[index], StageIndex = index,
                StageStartSeconds = TimingSeconds(_checkScheduler.EnteredAt),
                StageEndSeconds = TimingSeconds(_checkScheduler.ExpectedEndSeconds),
                StageElapsedSeconds = _checkScheduler.StageElapsedSeconds,
                EffectiveDurationSeconds = TimingSeconds(duration),
                RemainingSeconds = TimingSeconds(duration - _checkScheduler.StageElapsedSeconds),
                EntryIdentity = _checkScheduler.Entry.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        internal string ApplyStageForChecks(CustomPlan plan, CustomStageRuntime runtime)
        {
            if (runtime == null || _checkScheduler == null || !_checkScheduler.Matches(plan)
                || _checkPlanFile != Settings.CustomPlanFile || runtime.StageIndex != _checkScheduler.StageIndex
                || runtime.EntryIdentity != _checkScheduler.Entry.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Application requires the current scheduler selection.");
            _checkScheduler.Suspend();
            var message = CustomStageAllocator.ApplyStage(plan, runtime.Stage, runtime.RemainingSeconds, 0,
                runtime.EntryIdentity, runtime.EffectiveDurationSeconds);
            if (!Blocked && CustomStageAllocator.LastStageApplicationSucceeded)
                _checkScheduler.MarkApplied(Character.rebirthTime.totalseconds, _checkHeartbeat);
            return message;
        }

        internal CustomPlanSchedulerSnapshot GetProtectedSchedulerSnapshot(CustomPlan plan, double elapsed)
        {
            var valid = _checkScheduler != null && _checkPlanFile == Settings?.CustomPlanFile && _checkScheduler.Matches(plan);
            var index = valid ? _checkScheduler.StageIndex : -1;
            var durations = valid ? _checkScheduler.Durations.Select(TimingSeconds).ToArray() : CustomStageAllocator.EffectiveStageDurations(plan);
            double? progress = index < 0 || plan.Stages[index].CompletionMode == 1 ? (double?) null
                : durations[index] == 0 ? 1d : Math.Min(1d, _checkScheduler.StageElapsedSeconds / durations[index]);
            var earned = valid ? _checkScheduler.EarnedSeconds : new double[plan.Stages.Length];
            var completed = valid ? _checkScheduler.Completed : new bool[plan.Stages.Length];
            var remaining = 0d;
            for (var i = 0; i < plan.Stages.Length; i++)
                if (plan.Stages[i].CompletionMode == 0 && plan.Stages[i].DurationSeconds > 0 && !completed[i])
                    remaining += Math.Max(0, plan.Stages[i].DurationSeconds - earned[i]);
            return new CustomPlanSchedulerSnapshot("stages", index, progress, Math.Max(0, elapsed),
                valid ? _checkScheduler.StageElapsedSeconds : 0, valid ? _checkScheduler.Cycle : 0, durations) {
                ProtectedWorkComplete = HasCompletedProtectedStageWork(plan),
                ProtectedSecondsRemaining = remaining, ScheduleComplete = valid && _checkScheduler.Finished,
                TimingReason = valid ? _checkScheduler.Reason : "Waiting to initialize stage timing."
            };
        }

        internal bool HasCompletedProtectedStageWork(CustomPlan plan)
        {
            return _checkScheduler != null && _checkPlanFile == Settings?.CustomPlanFile
                && _checkScheduler.Matches(plan) && _checkScheduler.ProtectedWorkComplete;
        }

        private static int TimingSeconds(double value) { return (int) Math.Min(int.MaxValue, Math.Max(0, Math.Ceiling(value))); }

        internal void Begin(CustomFocusGoal goal)
        {
            _customSustainedFocusGoal = _customRequestedFocusGoal = _customAppliedFocusGoal = goal;
            _customFocusStatus = "applied";
            // Resource-only tests enter with an already acquired setup at this boundary.
            var gear = EquipmentRecording.ExactLoadout.ToArray();
            var selection = new FocusSetupSelection { Goal = goal, Settings = Settings, PlanFile = Settings.CustomPlanFile,
                ObservedGear = gear, ObservedMaterial = gear.Select(item => LoadoutPresetPrimitiveKey(item.equipment)).ToArray(),
                ObservedSupport = CaptureSupportState().StableKey };
            selection.Desired = new SelectedSetupRequirements { Owner = "focus:" + goal, Gear = gear,
                Acquisition = Guid.NewGuid().ToString("N"), Authorized = () => FocusSetupCurrent(selection),
                Support = CaptureSupportState().Selection, ChangeSupport = true, MaterialValid = () => true, EnforceSupportBudget = false };
            _focusSetupSelection = selection;
            var plan = LoadRuntimeCustomPlan();
            _customFocusEnergyNguWeights = goal == CustomFocusGoal.MagicNgu ? null : Settings.EnergyNguWeights.ToArray();
            _customFocusMagicNguWeights = Settings.MagicNguWeights.ToArray();
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
        internal void InvalidateCustomRuntimePlanCache() { Invalidations++; _checkScheduler?.Suspend(); }
        internal void InvalidateCustomFightSchedulePlanCache() { Invalidations++; }
        internal bool StageFightFundingContextMatches(string context) => false;
        internal void ObserveStageFightAugmentFunding(IDictionary<ResourceTarget, long[]> desired) { }
        internal void ValidateScheduledPlanEdit(string name, CustomPlan plan) { }
        internal void ForgetStageProgress(string name)
        {
            if (_checkPlanFile == name) { _checkScheduler = null; _checkPlanFile = null; }
        }
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
                ngu = BlackBarAllocator.GetNguRuntime();
                foreach (var energy in new[] {true, false})
                {
                    var applied = energy ? reference._customFocusEnergyNguWeights : reference._customFocusMagicNguWeights;
                    var configured = energy ? Settings.EnergyNguWeights : Settings.MagicNguWeights;
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

    internal sealed partial class FocusCharacter
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
        internal Adventure adventure = new Adventure();
        internal Arbitrary arbitrary = new Arbitrary();
        internal BossController bossController = new BossController();
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
        internal BasicTraining training = new BasicTraining();
        internal Hacks hacks = new Hacks();
        internal Res3 res3 = new Res3();
        internal Beards beards = new Beards();
        internal AllBeardsController allBeards = new AllBeardsController();
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
        internal int calculateBestItopodLevel() => throw new NotSupportedException("Native tower floor math is tested in the native fixture.");
        internal void removeMostEnergy()
        {
            EnergyRecalls++;
            foreach (var skill in NGU.skills) { idleEnergy += skill.energy; skill.energy = 0; }
            for (var i = 0; i < advancedTraining.energy.Length; i++) { idleEnergy += advancedTraining.energy[i]; advancedTraining.energy[i] = 0; }
            foreach (var aug in augments.augs) { idleEnergy += aug.augEnergy + aug.upgradeEnergy; aug.augEnergy = aug.upgradeEnergy = 0; }
            foreach (var wish in wishes.wishes) { idleEnergy += wish.energy; wish.energy = 0; }
            idleEnergy += machine.speedEnergy + wandoos98.wandoosEnergy;
            machine.speedEnergy = wandoos98.wandoosEnergy = 0;
        }
        internal void removeMostMagic()
        {
            MagicRecalls++;
            foreach (var skill in NGU.magicSkills) { magic.idleMagic += skill.magic; skill.magic = 0; }
            foreach (var ritual in bloodMagic.ritual) { magic.idleMagic += ritual.magic; ritual.magic = 0; }
            foreach (var wish in wishes.wishes) { magic.idleMagic += wish.magic; wish.magic = 0; }
            magic.idleMagic += wandoos98.wandoosMagic + machine.goldMultiMagic;
            wandoos98.wandoosMagic = machine.goldMultiMagic = 0;
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
            augmentation = new Button(), advancedTraining = new Button(), bloodMagic = new Button(), beards = new Button {interactable = true};
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
        internal void updateMenu() { }
        internal void refreshMenu() { }
    }
    internal sealed class AdventureController
    {
        internal BonusController itopod = new BonusController();
        internal FocusZoneSelector zoneSelector = new FocusZoneSelector();
        internal object playerController = new object(), enemyAI = new object();
        internal int maxItopodLevel() => throw new NotSupportedException("Native tower floor limits are tested in the native fixture.");
    }
    internal sealed class FocusZoneSelector { internal void changeZone(int zone) { Main.Character.adventure.zone = zone; } }
    internal sealed class Adventure { internal int zone = -1, itopodStart, itopodEnd, highestItopodLevel; internal bool autoattacking, beastModeOn; }
    internal sealed class Arbitrary { internal bool boughtLazyITOPOD, lazyITOPODOn; }
    internal sealed class BossController { internal bool isFighting, nukeBoss; }
    internal sealed class Inventory { internal double[] macguffinBonuses = Enumerable.Repeat(1d, 13).ToArray(); }
    internal sealed class InventoryController { internal bool midDrag; internal Dictionary<specType, float> bonuses = new Dictionary<specType, float> {{specType.Augs, 0}}; }
    internal sealed class Challenge
    {
        internal bool inChallenge;
        internal int curCompletions = 7;
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
    internal sealed class Wandoos { internal bool disabled; internal int os; internal long wandoosEnergy, wandoosMagic; }
    internal sealed class WandoosController
    {
        internal bool ThrowOnUpdate;
        internal void removeAllMagic()
        {
            Main.Character.magic.idleMagic += Main.Character.wandoos98.wandoosMagic;
            Main.Character.wandoos98.wandoosMagic = 0;
        }
        internal float baseEnergyTime() { return 1; }
        internal float baseMagicTime() { return 1; }
        internal void updateText() { if (ThrowOnUpdate) throw new InvalidOperationException("Wandoos UI unavailable"); }
    }
    internal sealed class AugmentBar
    {
        internal bool UpgradeLocked;
        internal bool augLocked() { return false; }
        internal bool upgradeLocked() { return UpgradeLocked; }
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
        internal bool ThrowOnText;
        internal float currentCost() { return 1; }
        internal float bloodAdded() { return 1; }
        internal void add() { throw new Exception("Full ritual controller add is not used by priority allocation."); }
        internal float totalBloodMagicSpeedBonus() { return 1; }
        internal float sadisticDivider() { return 1; }
        internal void updateBloodMagicText() { if (ThrowOnText) throw new InvalidOperationException("Blood UI unavailable"); }
        internal void cap() { throw new NotSupportedException("Native ritual capping is outside this focus fixture."); }
    }
    internal sealed class BloodController
    {
        internal bool ThrowOnMenu;
        internal float LootBonus = 1f, GoldBonus = 1f;
        internal float lootBonus() { return LootBonus; }
        internal float goldBonus() { return GoldBonus; }
        internal RitualController[] bloodMagics = Enumerable.Range(0, 8).Select(_ => new RitualController()).ToArray();
        internal float[] normalSpeedDividers = new float[8], evilSpeedDividers = new float[8], sadisticSpeedDividers = new float[8];
        internal int ritualsUnlocked() { return 8; }
        internal void updateMenu() { if (ThrowOnMenu) throw new InvalidOperationException("Blood UI unavailable"); }
    }
    internal sealed class Training { internal long[] energy = new long[5], level = new long[5], levelTarget = new long[5]; internal bool autoAdvance; }
    internal sealed class TrainingController
    {
        internal Managers.AdvancedTrainingController defense = new Managers.AdvancedTrainingController(), attack = new Managers.AdvancedTrainingController(),
            block = new Managers.AdvancedTrainingController(), wandoosEnergy = new Managers.AdvancedTrainingController(), wandoosMagic = new Managers.AdvancedTrainingController();
    }
    internal sealed class Wish { internal int level; internal long energy, magic, res3; }
    internal sealed class Wishes { internal Wish[] wishes = Enumerable.Range(0, 191).Select(_ => new Wish()).ToArray(); }
}

namespace NGUInjector.Managers
{
    internal enum GearOptimizationGoal { EnergyCap, MagicCap, EnergyNgu, MagicNgu, AllNgu, TimeMachine, Blood, Gold, Drop, Fight, Augments, AdvancedTraining, Wandoos, ManualAdventure }
    internal enum FightScoreMode { SqrtPowerToughness, PowerTimesToughness, PowerPlusToughness }
    internal sealed class GearResult { internal bool Success = true; internal string Message = "gear"; internal ih[] Items = new ih[0]; }
    internal static class GearOptimizer
    {
        internal static (string Failure, GearRecallFootprint Recall) PreflightCapacityTransition(ih[] gear, GearRecallFootprint recall) =>
            throw new NotSupportedException("Coupled fruit return capacity is tested against the compiled native actuator.");
        internal static GearRecallFootprint LegacyRecallFootprint()
        {
            return (Main.Settings.ManageEnergy ? GearRecallFootprint.Energy : GearRecallFootprint.None)
                | (Main.Settings.ManageMagic ? GearRecallFootprint.Magic : GearRecallFootprint.None)
                | (Main.Settings.ManageR3 ? GearRecallFootprint.Res3 : GearRecallFootprint.None);
        }
        internal static Dictionary<int, string> GoalLabels = Enum.GetValues(typeof(GearOptimizationGoal)).Cast<GearOptimizationGoal>().ToDictionary(x => (int)x, x => x.ToString());
        internal static Dictionary<int, string> FightScoreModeLabels = Enum.GetValues(typeof(FightScoreMode)).Cast<FightScoreMode>().ToDictionary(x => (int)x, x => x.ToString());
        internal static GearResult Optimize(GearOptimizationGoal goal, FightScoreMode mode)
        {
            if (!EquipmentRecording.Enabled) throw new Exception("Sustain must not optimize gear.");
            EquipmentRecording.Gear.Add(goal);
            return new GearResult {Items = EquipmentRecording.SelectedGear};
        }
        internal static GearResult GetCachedOrOptimize(GearOptimizationGoal goal, FightScoreMode mode)
        {
            throw new NotSupportedException("Inline selection-cache execution requires the compiled injector fixture.");
        }
        internal static string CheckExactSwapCapacity(ih[] gear, decimal energy, decimal res3)
        {
            if (!EquipmentRecording.Enabled) throw new Exception("Sustain must not preflight gear.");
            EquipmentRecording.CapacityChecks.Add(new[] {energy, res3});
            return EquipmentRecording.CapacityFailure
                ?? (energy > EquipmentRecording.EnergySwapCapacity ? "fixture retained energy exceeds swap capacity"
                    : res3 > EquipmentRecording.Res3SwapCapacity ? "fixture retained R3 exceeds swap capacity" : null);
        }
        internal static GearResult OptimizeAndEquip(GearOptimizationGoal goal, FightScoreMode mode, bool save, bool skip)
        {
            if (!EquipmentRecording.Enabled) throw new Exception("Sustain must not equip gear.");
            EquipmentRecording.Gear.Add(goal);
            return new GearResult();
        }
    }
    internal sealed class AugmentOptimizerResult { internal bool Success, UsesUpgrade; internal int AugmentIndex; internal string Message; internal float EnergyToAugment, EnergyToUpgrade; }
    internal static class AugmentOptimizer
    {
        internal static AugmentOptimizerResult Selected;
        internal static AugmentOptimizerResult OptimizeForActualBudget(int seconds, long availableEnergy, int lookaheadSeconds,
            int augmentIndex = -1, bool useUpgrade = true)
        {
            if (Selected != null) return Selected;
            if (!Main.Character.buttons.augmentation.interactable) return new AugmentOptimizerResult();
            throw new Exception("No actual-budget augment selection was supplied by the fixture.");
        }
        internal static AugmentOptimizerResult OptimizeBest(int seconds, bool preview)
        {
            if (Selected != null) return Selected;
            if (!Main.Character.buttons.augmentation.interactable) return new AugmentOptimizerResult();
            throw new Exception("NGU sustain must not optimize unlocked augments.");
        }
        internal static AugmentOptimizerResult OptimizeTarget(int index, bool upgrade, int seconds, bool preview)
        {
            if (Selected != null) return Selected;
            throw new Exception("NGU sustain must not optimize augments.");
        }
    }
    internal sealed class AdvancedTrainingController { internal float baseTime = 1; internal void updateText() { } }
    internal static partial class LoadoutManager
    {
        internal static void RemoveResourcesForGearSwap()
        {
            if (!EquipmentRecording.Enabled) throw new Exception("Sustain must not swap gear.");
            EquipmentRecording.GearSwaps++;
        }
    }
    internal static class BeardManager
    {
        internal static int[] SelectBeards(IEnumerable<int> ids, bool randomize, int[] hardRequired = null)
        {
            if (!EquipmentRecording.Enabled) throw new Exception("Sustain must not select beards.");
            EquipmentRecording.BeardRandomize.Add(randomize);
            var selected = EquipmentRecording.BeardMessage.StartsWith("beards:", StringComparison.OrdinalIgnoreCase)
                ? EquipmentRecording.BeardMessage.Substring(7).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray()
                : throw new InvalidOperationException(EquipmentRecording.BeardMessage);
            if ((hardRequired ?? Array.Empty<int>()).Except(selected).Any()) throw new InvalidOperationException("Required beards are unavailable.");
            return selected;
        }
        internal static string RotateBeards(IEnumerable<int> ids, bool randomize)
        {
            if (!EquipmentRecording.Enabled) throw new Exception("Sustain must not equip beards.");
            EquipmentRecording.Beards.Add(ids.ToArray());
            EquipmentRecording.BeardRandomize.Add(randomize);
            if (EquipmentRecording.ThrowNextBeards)
            {
                EquipmentRecording.ThrowNextBeards = false;
                throw new InvalidOperationException("beard controller unavailable");
            }
            // Apply only the supplied boundary receipt, never a substitute selection policy.
            if (EquipmentRecording.BeardMessage.StartsWith("beards:", StringComparison.OrdinalIgnoreCase))
            {
                var selected = EquipmentRecording.BeardMessage.Substring(7).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                Main.Character.beards.activeBeards = selected.ToList();
                for (var id = 0; id < Main.Character.beards.beards.Count; id++)
                    Main.Character.beards.beards[id].active = selected.Contains(id);
            }
            return EquipmentRecording.BeardMessage;
        }
    }
    internal static class DiggerManager
    {
        internal sealed class DiggerRequest { internal int Id, Weight; internal string Name; }
        internal static string EquipWeightedDiggers(IEnumerable<DiggerRequest> diggers, double budget)
        {
            if (!EquipmentRecording.Enabled) throw new Exception("Sustain must not equip diggers.");
            EquipmentRecording.Diggers.Add(diggers.Select(x => new DiggerRequest {Id = x.Id, Weight = x.Weight, Name = x.Name}).ToArray());
            EquipmentRecording.Budgets.Add(budget);
            if (EquipmentRecording.ThrowNextDiggers)
            {
                EquipmentRecording.ThrowNextDiggers = false;
                throw new InvalidOperationException("digger controller unavailable");
            }
            return EquipmentRecording.DiggerMessage;
        }
    }
    internal sealed class TitanWindow { internal bool SpawningSoon; }
    internal static class ZoneHelpers
    {
        // Supply only the game-zone classification boundary, not routing policy.
        internal static bool ZoneIsTitan(int zone) { return zone == 6; }
        internal static TitanWindow TitansSpawningSoon()
        {
            if (EquipmentRecording.ThrowTitanTiming) throw new InvalidOperationException("titan clock unavailable");
            return new TitanWindow {SpawningSoon = EquipmentRecording.TitanSoon};
        }
    }

    internal static class EquipmentRecording
    {
        internal static bool Enabled, ThrowNextDiggers, ThrowNextBeards, TitanSoon, ThrowTitanTiming;
        internal static int GearSwaps;
        internal static ih[] SelectedGear = new ih[0], ExactLoadout = new ih[0];
        internal static string CapacityFailure;
        internal static decimal EnergySwapCapacity = decimal.MaxValue, Res3SwapCapacity = decimal.MaxValue;
        internal static Action AfterExactApplication;
        internal static readonly List<decimal[]> CapacityChecks = new List<decimal[]>();
        internal static readonly List<ih[]> ExactApplications = new List<ih[]>();
        internal static readonly List<SupportSelection> ExactSupport = new List<SupportSelection>();
        internal static string BeardMessage = "beards: 1,3,5", DiggerMessage = "diggers equipped";
        internal static readonly List<GearOptimizationGoal> Gear = new List<GearOptimizationGoal>();
        internal static readonly List<int[]> Beards = new List<int[]>();
        internal static readonly List<bool> BeardRandomize = new List<bool>();
        internal static readonly List<DiggerManager.DiggerRequest[]> Diggers = new List<DiggerManager.DiggerRequest[]>();
        internal static readonly List<double> Budgets = new List<double>();

        internal static void Reset(bool enabled)
        {
            Enabled = enabled;
            ThrowNextDiggers = ThrowNextBeards = TitanSoon = ThrowTitanTiming = false;
            GearSwaps = 0;
            SelectedGear = ExactLoadout = new ih[0];
            CapacityFailure = null;
            EnergySwapCapacity = Res3SwapCapacity = decimal.MaxValue;
            AfterExactApplication = null;
            CapacityChecks.Clear();
            ExactApplications.Clear();
            ExactSupport.Clear();
            BeardMessage = "beards: 1,3,5";
            DiggerMessage = "diggers equipped";
            Gear.Clear();
            Beards.Clear();
            BeardRandomize.Clear();
            Diggers.Clear();
            Budgets.Clear();
        }
    }
}
