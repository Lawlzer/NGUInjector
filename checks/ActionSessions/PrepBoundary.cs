using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

internal sealed class ih { internal int id; internal object equipment = new object(); }
internal sealed class Clock { internal double totalseconds = 6000; }
internal sealed class Stats { internal long rebirthNumber = 7; }
internal sealed class Res3 { internal long curRes3; }
internal sealed class Button { internal bool interactable; }
internal sealed class Buttons { internal Button beards = new Button(), yggdrasil = new Button { interactable = true }; }
internal sealed class BossController { internal bool isFighting, nukeBoss; }
internal sealed class Adventure { internal int zone = -1; }
internal sealed class Challenge { internal bool inChallenge; }
internal sealed class Challenges { internal Challenge noRebirthChallenge = new Challenge(); }
internal sealed class Beards { internal bool disabled; internal List<int> activeBeards = new List<int> { 2 }; }
internal sealed class AllBeards { internal int capBeards() => 7; }
internal sealed class Magic { internal long idleMagic, curMagic; }
internal sealed class Fruit { internal int harvests; internal float seconds = 3600; internal bool activated = true, eatFruit, permCostPaid; internal long maxTier = 1; }
internal sealed class Yggdrasil { internal List<Fruit> fruits = Enumerable.Range(0, 7).Select(x => new Fruit()).ToList(); }
internal sealed class FruitController
{
    internal bool RejectConsume;
    internal int ConsumeCalls;
    internal int harvestTier(int id) => MainModel.yggdrasil.fruits[id].seconds >= 3600 ? 1 : 0;
    internal bool fruitMaxxed(int id) => harvestTier(id) > 0;
    private Character MainModel => NGUInjector.Main.Character;
    internal void consumeFruit(int id) { ConsumeCalls++; if (!RejectConsume) harvest(id); }
    internal void harvest(int id) { MainModel.yggdrasil.fruits[id].harvests++; MainModel.yggdrasil.fruits[id].seconds = 0; }
    internal void activate(int id) => throw new NotSupportedException();
}
internal sealed class YggController
{
    internal FruitController[] fruits = new[] { new FruitController() };
    internal List<bool> usesEnergy = Enumerable.Repeat(true, 7).ToList();
    internal List<long> activationCost = Enumerable.Repeat(1L, 7).ToList();
}
internal sealed class Character
{
    internal Stats stats = new Stats(); internal Clock rebirthTime = new Clock();
    internal Res3 res3 = new Res3(); internal Buttons buttons = new Buttons();
    internal BossController bossController = new BossController(); internal Adventure adventure = new Adventure();
    internal Beards beards = new Beards(); internal AllBeards allBeards = new AllBeards();
    internal Yggdrasil yggdrasil = new Yggdrasil(); internal YggController yggdrasilController = new YggController();
    internal Magic magic = new Magic(); internal long curEnergy, idleEnergy;
    internal Rebirth rebirth = new Rebirth();
    internal Challenges challenges = new Challenges();
}
internal sealed class Rebirth
{
    internal int NormalCalls, ChallengeCalls;
    internal bool Reject, ThrowAfterCommit;
    internal double minRebirthTime() => 1d;
    internal void engage() { NormalCalls++; Commit(); }
    internal void engageBasicChallenge() { ChallengeCalls++; Commit(); }
    private void Commit()
    {
        if (Reject) return;
        NGUInjector.Main.Character.stats.rebirthNumber++; NGUInjector.Main.Character.rebirthTime.totalseconds = 0;
        if (ThrowAfterCommit) throw new InvalidOperationException("render failure after native commit");
    }
    private void calculateTimeMulti() { }
    private void calculateNextMultis() { }
}
namespace NGUInjector.CustomPlans
{
    internal sealed class CustomPlan { internal bool PrepRequireMaxedFruit = true, AutoRebirthEnabled = true; internal int FightScoreMode, RebirthPrepStartSeconds; }
    internal sealed class Store { internal CustomPlan Load(string name) => new CustomPlan(); }
}
namespace NGUInjector.Managers
{
    internal enum MutationDisposition { RejectedUnchanged, AppliedVerified, ModelAppliedRenderFailed, UnknownOutcome }
    [Flags] internal enum GearRecallFootprint { None = 0, Energy = 1, Magic = 2, Res3 = 4 }
    internal enum GearOptimizationGoal { Fight }
    internal enum FightScoreMode { Default }
    internal sealed class Digger { internal int Id; }
    internal sealed class SupportSelection { internal int[] Beards = new int[0]; internal Digger[] Diggers = new Digger[0]; internal string StableKey => "support"; }
    internal static class ActivityPolicy { internal static Budget Current = new Budget(); internal sealed class Budget { internal double DiggerBudgetFraction = 1; } }
    internal static class GearOptimizer
    {
        internal sealed class CapacityResult { internal string Failure; internal GearRecallFootprint Recall; }
        internal static string CheckExactSwapCapacity(ih[] gear, long energy, long r3) => null;
        internal static CapacityResult PreflightCapacityTransition(ih[] gear, GearRecallFootprint permitted)
        {
            if (NGUInjector.Main.Character.curEnergy != 0 || NGUInjector.Main.Character.magic.curMagic != 0
                || NGUInjector.Main.Character.res3.curRes3 != 0)
                throw new NotSupportedException("Native capacity transitions require the compiled fixture.");
            return new CapacityResult { Recall = permitted };
        }
    }
    internal static class SupportProjection
    {
        internal sealed class Result { internal bool Feasible; }
        internal static Result Evaluate(Character character, SupportSelection support, double budget, bool enforceBudget) => new Result {
            Feasible = character != null && support != null && budget >= 0 && budget <= 1
                && (!character.buttons.beards.interactable || !character.beards.disabled && support.Beards.Length <= character.allBeards.capBeards())
        };
    }
    internal static class LoadoutManager
    {
        internal sealed class SnapshotEpoch
        {
            private readonly object model = NGUInjector.Main.Character;
            private readonly long rebirth = NGUInjector.Main.Character.stats.rebirthNumber;
            internal bool Matches() => ReferenceEquals(model, NGUInjector.Main.Character) && rebirth == NGUInjector.Main.Character.stats.rebirthNumber;
        }
        internal static string ValidateExactLoadout(ih[] gear) => null;
        internal static bool ExactLoadoutMatches(ih[] gear) => true;
        internal static ih[] CaptureExactLoadout() => new ih[0];
    }
    internal static class YggdrasilManager { internal const int FruitOfPowerAlphaId = 1, FruitOfPowerBetaId = 6; }
    internal static class CustomStageAllocator
    {
        internal static bool ProtectedWorkComplete(CustomPlan plan) => NGUInjector.Main.reference.ProtectedWorkReady;
    }
}
namespace NGUInjector.AllocationProfiles.RebirthStuff
{
    internal sealed class CustomPlanRebirth
    {
        internal CustomPlanRebirth(CustomPlan plan) { }
        internal bool RebirthAvailable() => true;
        internal static void ResetRuntimeState() { } // This readiness boundary retains no NUMBER-confirmation state.
    }
    internal sealed class ManualRebirth { internal bool RebirthAvailable() => true; }
}
namespace NGUInjector
{
    internal static class ReflectionBoundary { internal static MethodInfo GetPrivateMethod(this Type type, string name) => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance); }
    internal enum CustomFocusGoal { Fight }
    internal sealed class Settings { internal bool UseCustomRebirthPlan = true, ManageEnergy = true, ManageMagic = true, AutoRebirth = true; internal string CustomPlanFile = "prep-unit"; internal int[] YggdrasilLoadout = new int[0]; }
    internal partial class Main
    {
        internal static Character Character; internal static Settings Settings; internal static Main reference;
        internal int SetupApplications, Selections; internal bool BossAdmissionReady = true, GrantReady = true;
        internal bool RouteApplicationReady = true, RouteRetirementReady = true;
        internal bool ProtectedWorkReady = true;
        internal Action SelectionHook;
        internal string CurrentExecutionCommandId;
        private object _bossPushProcess, _manualTitanProcess, _focusSetupSelection;
        private CombatPrepRun _combatPrepRun;
        private bool _manualTitanActive, CombatPrepActive;
        private int? _customSustainedFocusGoal;
        private Store CustomPlanStore;
        internal bool BossPushRunning;
        private const int BeardFuManchu = 1;
        private sealed class Form { internal void ReportCustomStatus(string message) { } }
        private Form settingsForm;
        [Flags] internal enum ExecutionDomain { None = 0, Energy = 1, Magic = 2, Gear = 8, Support = 16, Route = 32, FiniteAction = 64, Inventory = 128, NativeSettings = 256 }
        internal sealed class ExecutionClaim
        {
            internal string Owner, Acquisition; internal ExecutionDomain Domains;
            internal LoadoutManager.SnapshotEpoch Epoch = new LoadoutManager.SnapshotEpoch(); internal bool Ended, Applied, SetupReleased;
            internal Func<bool> Permission;
        }
        internal sealed class SelectedSetupRequirements
        {
            internal SupportSelection Support; internal ih[] Gear;
            internal bool ControlsSupport, EnforceSupportBudget;
            internal double Budget;
            internal int[] RequiredBeards = new int[0];
        }
        private sealed class RouteProcess
        {
            internal ExecutionClaim Claim;
            internal SelectedSetupRequirements Setup;
            internal string Owner, Permission;
            internal Func<bool> ApplicationCurrent;
            internal bool Applied, HoldEnded;
        }
        private bool _routeHandoffPending;
        private sealed class Scheduler { internal bool Finished; }
        private Scheduler _protectedScheduler;
        // This adapter has no stage schedule or automatic-prep executor. Native handoff
        // verification belongs to CombatPrepIntegration, never a successful stub receipt.
        private sealed class CombatPrepRun
        {
            internal bool Retired, FightReleaseVerified;
            internal ih[] FightSuccessorGear;
            internal string[] FightSuccessorMaterial;
            internal SupportState FightSuccessorSupport;
            internal object[] FightSuccessorSupportObjects;
            internal long FightConflictVersion;
        }
        private bool ScheduledFightAdmissionPermitted(FiniteActionIdentity identity) => _protectedScheduler == null && _combatPrepRun == null
            ? true : throw new NotSupportedException("Scheduled fight admission requires the compiled fixture.");
        private bool SuspendedFightHandoff => _combatPrepRun == null
            ? false : throw new NotSupportedException("Suspended combat-prep handoff requires the compiled fixture.");
        private bool CombatPrepEntryAuthority(CombatPrepRun run) => throw new NotSupportedException("Combat-prep authority requires the compiled fixture.");
        private static bool CombatPrepTargetsMatch(CombatPrepRun run) => throw new NotSupportedException("Native combat-prep targets require the compiled fixture.");
        private static bool CombatPrepGearMatches(ih[] gear) => throw new NotSupportedException("Native combat-prep gear requires the compiled fixture.");
        private static object[] SupportObjects() => throw new NotSupportedException("Native support identity requires the compiled fixture.");
        // Ordinary prep release reads this even without a scheduled handoff. No scheduled
        // conflict history exists here; reject any attempt to use it for automatic prep.
        private static long SelectedSetupConflictVersion(Character model) => reference?._protectedScheduler == null && reference?._combatPrepRun == null
            ? 0 : throw new NotSupportedException("Scheduled setup conflicts require the compiled fixture.");
        private bool _protectedCheckpointDurable;
        private string ExecutionPersistenceEntry => null;
        private bool ProtectedEpochMatches() => throw new NotSupportedException("Scheduled checkpoints require the compiled fixture.");
        private sealed class ResourceApplication { internal int BlockedLanes; }
        private sealed class ResourceExecutor { internal ResourceApplication Application = new ResourceApplication(); }
        private ResourceExecutor _resourceSetupExecutor = new ResourceExecutor();
        private enum ResourceExecutionState { Waiting }
        private void SetResourceExecution(ResourceExecutionState state, string owner, string reason) { }
        private Character CaptureSupportContext() => Character;
        private string RoutePermissionKey(string owner) => owner == "fight-preparation"
            ? Settings.ManageEnergy + ":" + Settings.ManageMagic + ":" + Settings.UseCustomRebirthPlan + ":" + Settings.CustomPlanFile
            : throw new NotSupportedException("Only fight-preparation route admission is supplied by this adapter.");
        // Route mutation/capacity are supplied boundaries; the linked production code owns session continuation.
        private RouteProcess BeginRouteProcess(string owner, ih[] gear, bool controlsGear = true,
            ExecutionDomain domains = ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.NativeSettings,
            SupportSelection selectedSupport = null, bool capacityTransition = false,
            GearRecallFootprint resourceRecall = GearRecallFootprint.None, string acquisition = null)
        {
            var attempt = _fightPreparationAttempt;
            if (!capacityTransition || attempt == null || attempt.Identity.Owner != owner || attempt.Identity.Acquisition != acquisition)
                throw new InvalidOperationException("Capacity transition requires the identified preparation attempt.");
            GearOptimizer.PreflightCapacityTransition(gear, resourceRecall);
            var material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var process = new RouteProcess { Owner = owner, Permission = RoutePermissionKey(owner) };
            process.ApplicationCurrent = () => !attempt.Cancelled && !attempt.Completed
                && (attempt.EntryCurrent == null || attempt.EntryCurrent())
                && !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any();
            process.Claim = BeginExecutionClaim(owner, acquisition, domains | (ExecutionDomain)(int)resourceRecall,
                () => ReferenceEquals(attempt, _fightPreparationAttempt) && ReferenceEquals(attempt.Settings, Settings)
                    && ReferenceEquals(attempt.Boss, Character.bossController) && attempt.PlanFile == Settings.CustomPlanFile);
            process.Setup = CreateFiniteActionSetup(process.Claim, gear, selectedSupport, controlsGear, selectedSupport != null);
            return process;
        }
        private bool RouteProcessDependenciesReady(RouteProcess process) => process != null && process.Applied
            && ExecutionClaimCurrent(process.Claim) && !process.Claim.SetupReleased;
        private bool ApplyRouteProcess(RouteProcess process)
        {
            if (!RouteApplicationReady || CustomBossFightActive() || !ExecutionClaimCurrent(process.Claim)
                || !process.ApplicationCurrent() || process.Permission != RoutePermissionKey(process.Owner)) return false;
            if (!process.Applied) ApplyExecutionClaim(process.Claim, process.Setup);
            process.Applied = true;
            return RouteProcessDependenciesReady(process);
        }
        private bool EndRouteProcess(RouteProcess process)
        {
            if (process == null || process.HoldEnded) return true;
            if (!process.Claim.Epoch.Matches()) return EndExecutionClaim(process.Claim, false);
            if (!RouteRetirementReady || CustomBossFightActive() || !ExecutionClaimSetupOwned(process.Claim)) return false;
            process.Claim.SetupReleased = true;
            if (!EndExecutionClaim(process.Claim, false)) return false;
            process.HoldEnded = true;
            return true;
        }
        internal sealed class ExecutionGrant { internal string Owner, Acquisition; internal object Model; internal long Rebirth; internal Func<bool> Current; }
        private ExecutionGrant ResolveExecutionGrant(string owner, string acquisition, ExecutionDomain domains, Func<bool> current, ExecutionClaim claim = null) =>
            new ExecutionGrant { Owner = owner, Acquisition = acquisition, Model = Character, Rebirth = Character.stats.rebirthNumber, Current = current };
        private bool ValidateExecutionGrant(ExecutionGrant grant, ExecutionDomain domains) => GrantReady && ReferenceEquals(grant.Model, Character)
            && grant.Rebirth == Character.stats.rebirthNumber && grant.Current();
        private bool VerifyExecutionPrerequisites(ExecutionGrant grant, ExecutionDomain domains, Func<bool> ready) => ValidateExecutionGrant(grant, domains) && ready();
        private Func<bool, bool> CaptureFiniteActionBoundary(FiniteAction action) { var model = Character; return after => ReferenceEquals(model, Character); }
        private void InvalidateExecutionObservations(ExecutionDomain domains) { }
        private bool CustomBossFightActive() => Character.bossController.isFighting || Character.bossController.nukeBoss;
        internal string Prep(CustomPlan plan, bool fruits, string opportunity = "original-request") => RunFightPrep(plan, fruits, "unit prep", opportunity: opportunity);
        internal bool ProcessPending() => ProcessPendingManualRebirth();
        internal bool ManualPending => _manualRebirthPending;
        internal bool ManualFightPending => _manualFightPreparationRequest != null;
        internal bool HarvestReserved(CustomPlan plan) => CustomRebirthFruitHarvestReserved(plan);
        internal void SetPendingManualRebirth() { _manualRebirthPending = true; _manualRebirthNumber = Character.stats.rebirthNumber; _customPreRebirthPrepCompleted = true; }
        private sealed class Policy { internal Stage Stage = new Stage(); }
        private sealed class Stage { internal int GearGoal; }
        private Policy SetupForFocus(CustomFocusGoal goal) => new Policy();
        private ih[] SelectSetupGear(GearOptimizationGoal goal, FightScoreMode mode) { Selections++; SelectionHook?.Invoke(); return new ih[0]; }
        private SupportSelection SelectFocusSupport(Policy policy) => new SupportSelection();
        private string LoadoutPresetPrimitiveKey(object item) => "material";
        private void PreflightExactSupport(SupportSelection support, double budget, bool required) { }
        private readonly List<ExecutionClaim> claims = new List<ExecutionClaim>();
        private bool ExecutionOwnsDomain(ExecutionDomain domains) => claims.Any(claim => !claim.Ended && (claim.Domains & domains) != 0);
        private ExecutionClaim BeginExecutionClaim(string owner, string acquisition, ExecutionDomain domains, Func<bool> current)
        {
            if (ExecutionOwnsDomain(domains)) throw new InvalidOperationException("A claim already owns part of this footprint.");
            var claim = new ExecutionClaim { Owner = owner, Acquisition = acquisition, Domains = domains, Permission = current };
            claims.Add(claim);
            return claim;
        }
        private SelectedSetupRequirements CreateFiniteActionSetup(ExecutionClaim claim, ih[] gear, SupportSelection support, bool controls, bool change, int[] required = null) => new SelectedSetupRequirements { Gear = gear, Support = support };
        private SelectedSetupRequirements AcquireFiniteActionSetup(ExecutionClaim claim, ih[] gear, SupportSelection support, bool controls, bool change)
        { var setup = CreateFiniteActionSetup(claim, gear, support, controls, change); ApplyExecutionClaim(claim, setup); return setup; }
        private void ApplyExecutionClaim(ExecutionClaim claim, SelectedSetupRequirements setup) { SetupApplications++; claim.Applied = true; }
        private bool FiniteActionSetupVerified(ExecutionClaim claim, SelectedSetupRequirements setup) => claim != null && setup != null && claim.Applied && ExecutionClaimCurrent(claim);
        private bool ExecutionClaimCurrent(ExecutionClaim claim) => !claim.Ended && claim.Epoch.Matches() && claim.Permission();
        private bool ExecutionClaimSetupOwned(ExecutionClaim claim) => ExecutionClaimCurrent(claim);
        private bool EndExecutionClaim(ExecutionClaim claim, bool reconcile = true)
        {
            if (claim == null) return true;
            claim.Ended = true;
            return !reconcile || claim.Epoch.Matches();
        }
        internal void StartBossPush() { if (BossAdmissionReady) { BossPushRunning = true; _bossPushProcess = new object(); } }
        internal void StopBossPush() { BossPushRunning = false; _bossPushProcess = null; CompleteFightPreparation(); }
        private void CancelManualTitan(string reason) { }
        private void EndCombatPrep(string state, string reason, bool restore) { }
        private void ClearPersistedExecutionSelection(string kind) { }
        private void ClearCustomSustainedFocus() { }
        private void ResetCustomIronPillPrep() { }
        private void ResetCustomAutomationRuntime() { }
        private void CommitExecutionRebirthCheckpoint() { }
        private static void Log(string text) { }
        private sealed class Profile
        {
            internal bool DoRebirth(AllocationProfiles.RebirthStuff.ManualRebirth rebirth)
            {
                try { return reference.PrepareFinalRebirthAction() && reference.InvokeFinalRebirthAction("engage"); }
                finally { reference.ReleaseFinalRebirthAction(); }
            }
        }
        private Profile _profile = new Profile();
        private sealed class SupportState { internal SupportSelection Selection = new SupportSelection(); internal string StableKey => Selection.StableKey; }
        private SupportState CaptureSupportState() => new SupportState();
        private SupportSelection SelectActionSupport(int[] beards, params int[] diggers) => new SupportSelection { Beards = beards };
        private ih[] SelectConfiguredActionGear(int[] ids) => new ih[0];
        private void SetExecutionResourceReservation(ExecutionClaim claim, long energy, long magic) { }
        private void ReconcileExecutionResourceClaim(ExecutionClaim claim) { }
        private bool ExecutionResourceReservationReady(ExecutionClaim claim) => false;
    }
}
