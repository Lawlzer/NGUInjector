using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

internal enum difficulty { normal, evil, sadistic }
internal sealed class Clock { internal double totalseconds; }
internal sealed class Character
{
    internal Clock rebirthTime = new Clock(), totalPlaytime = new Clock();
    internal Stats stats = new Stats();
    internal Blood bloodMagic = new Blood();
    internal Spell bloodSpells;
    internal BloodController bloodMagicController = new BloodController();
    internal Magic magic = new Magic();
    internal Buttons buttons = new Buttons();
    internal Adventure adventure = new Adventure();
    internal AdventureController adventureController = new AdventureController();
    internal NativeSettings settings = new NativeSettings();
    internal Inventory inventory = new Inventory();
    internal Wishes wishesController = new Wishes();
    internal int bossID = 100;
    internal Character() { bloodSpells = new Spell(this); }
}
internal sealed class Stats { internal long rebirthNumber = 7; }
internal sealed class Blood
{
    internal double bloodPoints = 10000d, rebirthPower = 11d;
    internal bool lootAutoSpell, goldAutoSpell, rebirthAutoSpell;
    internal Clock adventureSpellTime = new Clock { totalseconds = 10000d }, macguffin1Time = new Clock(), macguffin2Time = new Clock();
    internal List<int> ritual = new List<int> { 0 };
}
internal sealed class Spell
{
    private readonly Character model;
    internal int NumberCalls, PillCalls, MacguffinCalls, adventureSpellCooldown = 3600, macguffin1Cooldown = 3600, macguffin2Cooldown = 3600;
    internal bool RejectNumber, RejectPill, RejectMacguffin;
    internal Action AfterNumber;
    internal Spell(Character model) { this.model = model; }
    internal double minAdventureBlood() => 100d;
    internal double minMacguffin1Blood() => 100d;
    internal double minMacguffin2Blood() => 100d;
    internal void castRebirthSpell()
    {
        NumberCalls++;
        if (!RejectNumber) { model.bloodMagic.rebirthPower += model.bloodMagic.bloodPoints; model.bloodMagic.bloodPoints = 0d; }
        AfterNumber?.Invoke();
    }
    internal void castAdventurePowerupSpell()
    {
        PillCalls++;
        if (RejectPill) return;
        var worth = (float)IronPillAutomation.Worth(model.bloodMagic.bloodPoints, 1f);
        model.adventure.attack += worth; model.adventure.defense += worth;
        model.adventure.maxHP += worth * 3f; model.adventure.regen += worth * .03f;
        model.bloodMagic.bloodPoints = 0d; model.bloodMagic.adventureSpellTime.totalseconds = 0d;
    }
    internal void castMacguffin1Spell() { throw new NotSupportedException(); }
    internal void castMacguffin2Spell()
    {
        MacguffinCalls++;
        if (RejectMacguffin) return;
        var levels = (int)(Math.Log(model.bloodMagic.bloodPoints / minMacguffin2Blood(), 20d) + 1d);
        foreach (var guffin in model.inventory.macguffins.Where(x => x.id != 0)) guffin.level += levels;
        model.bloodMagic.bloodPoints = 0d; model.bloodMagic.macguffin2Time.totalseconds = 0d;
    }
    internal void updateLootToggleState() { }
    internal void updateGoldToggleState() { }
    internal void updateRebirthToggleState() { }
}
internal sealed class BloodController { internal int[] bloodMagics = new[] { 0 }; }
internal sealed class Magic { }
internal sealed class Buttons { internal Button bloodMagic = new Button(); }
internal sealed class Button { internal bool interactable = true; }
internal sealed class Adventure { internal float attack, defense, maxHP, regen; internal Itopod itopod = new Itopod(); }
internal sealed class Itopod { internal int[] perkLevel = new int[100]; }
internal sealed class AdventureController { internal ItopodController itopod = new ItopodController(); }
internal sealed class ItopodController { internal float ironPillBonus() => 1f; }
internal sealed class NativeSettings { internal difficulty rebirthDifficulty; }
internal sealed class Inventory { internal List<Guffin> macguffins = new List<Guffin>(); }
internal sealed class Guffin { internal int id, level; }
internal sealed class Wishes { internal float totalBloodGuffbonus() => 1f; }

namespace NGUInjector.CustomPlans
{
    internal sealed class CustomPlan
    {
        internal bool AutoActivateFruits, AutoIronPillEnabled = true;
        internal int FightScoreMode, IronPillSaveSeconds = 900;
        internal double IronPillMinWorth;
    }
    internal sealed class CustomStageRuntime { internal Stage Stage = new Stage(); }
    internal sealed class Stage { internal int GearGoal = -1; internal string PresetId; }
}
namespace NGUInjector.Managers
{
    internal enum MutationDisposition { RejectedUnchanged, AppliedVerified, ModelAppliedRenderFailed, UnknownOutcome }
    internal enum GearOptimizationGoal { EnergyCap, MagicCap }
    [Flags] internal enum GearRecallFootprint { None = 0, Energy = 1, Magic = 2, Res3 = 4 }
    internal enum FightScoreMode { Default }
    internal sealed class SupportSelection { }
    // Cap-gear execution is outside this blood-session harness, like SelectSetupGear below.
    internal static class LoadoutManager
    {
        internal static bool ExactLoadoutMatches(object gear) => throw new NotSupportedException("Cap gear requires the compiled fixture.");
    }
    internal static class GearOptimizer
    {
        internal sealed class CapacityResult { internal string Failure; }
        internal static CapacityResult PreflightCapacityTransition(object gear, GearRecallFootprint permitted) =>
            throw new NotSupportedException("Cap gear capacity requires the compiled fixture.");
    }
    internal static class YggdrasilManager
    {
        internal static int ActivateAffordableFruits(bool cap = false) => 0;
        internal static bool HasInactiveUnlockedFruit(bool energy) => false;
    }
    internal static class CustomStageAllocator { internal static string FormatSeconds(int seconds) => seconds.ToString(); }
}
namespace NGUInjector.AllocationProfiles.RebirthStuff { }
namespace NGUInjector
{
    internal sealed class IronPillHistoryEntry { internal string CastAtUtc; internal double WaitSeconds, Worth, RebirthSeconds; }
    internal sealed class Settings
    {
        internal string CustomPlanFile = "blood-unit";
        internal bool ManageMagic = true, UseCustomRebirthPlan = true, CustomAdventureEnabled, ManualAdventureEnabled,
            ManualTitanEnabled, CastBloodSpells, HistoryFailure;
        internal int BloodMacGuffinAThreshold, BloodMacGuffinBThreshold;
        internal List<IronPillHistoryEntry> History = new List<IronPillHistoryEntry>();
        internal void AddIronPillHistory(IronPillHistoryEntry entry)
        {
            if (HistoryFailure) throw new InvalidOperationException("history boundary failure");
            History.Add(entry);
        }
    }
    internal partial class Main
    {
        internal static Character Character;
        internal static Settings Settings;
        internal static Main reference;
        internal static string FixtureDirectory;
        private static string _dir => FixtureDirectory;
        private bool _manualRebirthPending, _customPreRebirthPrepRunning;
        private string _globalPresetManualId, _customStageEquipmentKey;
        private int? _customSustainedFocusGoal;
        internal bool BossPushRunning, GrantReady = true;
        private bool ManualAdventureOwnsEquipment => false;
        private Store CustomPlanStore => null;
        private sealed class Store { }
        internal bool Saving => CustomIronPillPrepActive();
        internal double Started => _customIronPillPrepStartedAt;
        internal double RetryAt => _customIronPillRetryAt;
        internal void BloodPass(CustomPlan plan) => RunCustomBloodSpellAutomation(plan);
        internal void LoadCheckpoint() => LoadIronBloodWindow();
        internal bool Cast() => TryCastCustomIronPill();
        [Flags] internal enum ExecutionDomain { None = 0, Energy = 1, Magic = 2, Gear = 8, Support = 16, Route = 32, FiniteAction = 64, Inventory = 128, NativeSettings = 256 }
        internal sealed class ExecutionClaim { internal string Owner, Acquisition; }
        internal sealed class SelectedSetupRequirements { }
        internal sealed class ExecutionGrant { internal string Owner, Acquisition; internal object Model; internal long Rebirth; internal Func<bool> Current; }
        private ExecutionGrant ResolveExecutionGrant(string owner, string acquisition, ExecutionDomain domains, Func<bool> current, ExecutionClaim claim = null) =>
            new ExecutionGrant { Owner = owner, Acquisition = acquisition, Model = Character, Rebirth = Character.stats.rebirthNumber, Current = current };
        private bool ValidateExecutionGrant(ExecutionGrant grant, ExecutionDomain domains) => GrantReady && ReferenceEquals(grant.Model, Character)
            && grant.Rebirth == Character.stats.rebirthNumber && grant.Current();
        private bool VerifyExecutionPrerequisites(ExecutionGrant grant, ExecutionDomain domains, Func<bool> ready) => ValidateExecutionGrant(grant, domains) && ready();
        private Func<bool, bool> CaptureFiniteActionBoundary(FiniteAction action) { var model = Character; return after => ReferenceEquals(model, Character); }
        private void InvalidateExecutionObservations(ExecutionDomain domains) { }
        private static int EffectiveCustomIronPillSaveSeconds(CustomPlan plan) => plan?.IronPillSaveSeconds ?? 900;
        private static double EffectiveCustomIronPillMinWorth(CustomPlan plan) => plan?.IronPillMinWorth ?? 0d;
        private static bool EffectiveCustomAutoBloodSpellsEnabled(CustomPlan plan) => true;
        private string CustomIronPillEligibilityBlocker(CustomPlan plan) => plan.AutoIronPillEnabled && Settings.ManageMagic ? "" : "disabled";
        private static void Log(string text) { }
        private static void LogError(string context, Exception error) { }
        private void SetCustomAutomationStatus(string message) { }
        private CustomPlan LoadRuntimeCustomPlan() => throw new NotSupportedException();
        private static int CustomPlannedRebirthSeconds(CustomPlan plan) => 7200;
        private bool CustomBloodGoalsSuppressed(CustomPlan plan) => false;
        private static bool TryGetCustomBloodGoals(CustomPlan plan, out bool loot, out bool gold) { loot = gold = false; return true; }
        private bool CustomBossFightActive() => false;
        private bool ExecutionScopeNeedsReview(ExecutionDomain domains) => throw new NotSupportedException("Cap gear review requires the compiled fixture.");
        private object SelectSetupGear(GearOptimizationGoal goal, FightScoreMode mode, bool cap) => throw new NotSupportedException();
        private sealed class SupportState { internal SupportSelection Selection = new SupportSelection(); }
        private SupportState CaptureSupportState() => throw new NotSupportedException();
        private void SuspendProtectedStageProgress() => throw new NotSupportedException();
        private ExecutionClaim BeginExecutionClaim(string owner, string acquisition, ExecutionDomain domains, Func<bool> current) => throw new NotSupportedException();
        private SelectedSetupRequirements AcquireFiniteActionSetup(ExecutionClaim claim, object gear, SupportSelection support, bool controls, bool change) => throw new NotSupportedException();
        private bool FiniteActionSetupVerified(ExecutionClaim claim, SelectedSetupRequirements setup) => throw new NotSupportedException();
        private void EndExecutionClaim(ExecutionClaim claim) => throw new NotSupportedException();
        private Func<bool> _fruitCapActionReady;
    }
}
