using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Game/Unity state and action boundaries only. Combat and zone policy are source-linked.
namespace NGUInjector
{
    internal enum enemyType { normal, boss, waldo1, waldo2, waldo3, waldo4, bigBoss5, bigBoss6V1, bigBoss6V2, bigBoss6V3, bigBoss6V4 }
    internal enum AI { normal, charger, rapid, exploder }

    internal partial class Main
    {
        internal static Character Character;
        internal static PlayerController PlayerController;
        internal static CombatSettings Settings;
        internal static bool Test;
        internal static object CustomPlanStore;
        internal static string Status;
        internal string BlockedReason;
        internal bool TitanRouteActive;
        private readonly Managers.CombatManager _combManager;
        private readonly QuestManager _questManager = new QuestManager();
        internal Main(Managers.CombatManager manager)
        {
            _combManager = manager;
            CustomPlanStore = null;
            Status = null;
            Test = false;
        }
        private object LoadRuntimeCustomPlan() => CustomPlanStore;
        private bool CustomGoalBlocksAdventureGoldPush(object plan, out string reason)
        { reason = BlockedReason; return reason != null; }
        private bool TryRouteCustomTitanGoldKill() => TitanRouteActive;
        private void CancelCustomTitanGoldKill(string reason) { }
        private static void FocusSettle() => Managers.LoadoutManager.Calls.Add("settle");
        private static string ZoneName(int zone) => Character.adventureController.zoneName(zone);
        private static void SetCustomAutomationStatus(string status) => Status = status;
        private Managers.FightScoreMode GetActiveFightScoreMode() => Managers.FightScoreMode.PowerTimesToughness;
        internal static void Log(string message) { }
        internal static void LogCombat(string message) { }
    }

    internal sealed class QuestManager { internal int IsQuesting() => -1; }

    internal sealed class CombatSettings
    {
        internal bool MoreBlockParry, BeastMode, DoGoldSwap, ManageGoldLoadouts, SwapTitanLoadouts, OptimizeITOPODFloor;
        internal int ITOPODCombatMode;
        internal bool CustomAdventureEnabled, CustomAdventureGoldPush, UseCustomRebirthPlan;
        internal int CustomAdventureMode, CustomAdventureZone;
        internal int[] BlacklistedBosses = Array.Empty<int>();
        internal bool[] TitanGoldTargets, TitanMoneyDone, TitanSwapTargets;
    }

    internal sealed class Character
    {
        internal Adventure adventure = new Adventure();
        internal AdventureController adventureController;
        internal Training training = new Training();
        internal Wishes wishes = new Wishes();
        internal Challenges allChallenges = new Challenges();
        internal GameSettings settings = new GameSettings();
        internal Buttons buttons = new Buttons();
        internal Arbitrary arbitrary = new Arbitrary();
        internal int bossID = 1000, EffectiveBossId = 1000;
        internal PlayerTime rebirthTime = new PlayerTime();
        internal Challenges challenges = new Challenges();
        internal float totalAdvHP() => 100;
        internal float chargeCooldown() => 10;
        internal float ultimateAttackCooldown() => 10;
        internal float ultimateBuffDuration() => 10;
        internal float defenseBuffDuration() => 10;
        internal int effectiveBossID() => EffectiveBossId;
        internal float totalAdvAttack() => 1000;
        internal float idleAttackPower() => 1;
        internal float regAttackPower() => 1;
    }

    internal sealed class Adventure
    {
        internal int zone = 19, highestItopodLevel;
        internal float curHP = 100;
        internal bool autoattacking, beastModeOn;
    }
    internal sealed class Training { internal long[] attackTraining = { 5000, 1, 0, 0, 0, 0 }, defenseTraining = new long[6]; }
    internal sealed class Wish { internal int level; }
    internal sealed class Wishes { internal Wish[] wishes = Enumerable.Range(0, 59).Select(_ => new Wish()).ToArray(); }
    internal sealed class Challenges
    {
        internal TimeMachineChallenge timeMachineChallenge = new TimeMachineChallenge();
        internal bool hasParalyze() => false;
    }
    internal sealed class TimeMachineChallenge { internal bool inChallenge; }
    internal sealed class GameSettings { internal bool hasHyperRegen; }
    internal sealed class Buttons { internal Button adventure = new Button(), brokenTimeMachine = new Button(); }
    internal sealed class Arbitrary { internal bool boughtLazyITOPOD, lazyITOPODOn; }
    internal sealed class PlayerTime { internal double totalseconds; }
    internal sealed class InputField { internal string text; }

    internal sealed class PlayerController
    {
        internal bool CanMove = true, isParrying, isBlocking;
        internal float moveTimer, chargeFactor = 1, ultimateBuffTime, defenseBuffTime;
        internal bool moveCheck() => CanMove;
    }

    internal sealed class Enemy
    {
        internal string name = "Unremarkable enemy";
        internal enemyType enemyType;
        internal AI AI;
        internal int spriteID;
        internal float curHP = 100, maxHP = 100, attackRate = 10;
    }

    internal class EnemyAI
    {
        public int chargeCooldown, rapidEffect;
        public float enemyAttackTimer;
        // Reflection adapter for the game's fields, not a replacement combat decision.
        internal T GetPV<T>(string name) => (T)GetType().GetField(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this);
    }
    internal sealed class WalderpAI : EnemyAI
    {
        public bool inWaldoSaysLoop, waldoSays;
        public int waldoAttackID = 3, growCount = 2;
    }
    internal sealed class MissingAttackPromptAI : EnemyAI { public bool inWaldoSaysLoop = true, waldoSays = true; }
    internal sealed class MissingSaysPromptAI : EnemyAI { public bool inWaldoSaysLoop = true; public int waldoAttackID = 3; }

    internal sealed class Button
    {
        internal bool interactable;
        internal bool IsInteractable() => interactable;
    }

    internal sealed class Move
    {
        internal readonly Button button = new Button();
        private readonly List<string> _calls;
        private readonly string _name;
        public float chargeTimer, ultimateAttackTimer;
        internal Move(List<string> calls, string name) { _calls = calls; _name = name; }
        internal void doMove()
        {
            if (!button.IsInteractable()) throw new InvalidOperationException("Unavailable move called: " + _name);
            _calls.Add(_name);
        }
        internal void setToggle()
        {
            Main.Character.adventure.autoattacking = !Main.Character.adventure.autoattacking;
            _calls.Add(Main.Character.adventure.autoattacking ? "idle:on" : "idle:off");
        }
    }

    internal sealed class ZoneSelector
    {
        private readonly List<string> _calls;
        internal ZoneSelector(List<string> calls) { _calls = calls; }
        internal void changeZone(int zone) { _calls.Add("zone:" + zone); Main.Character.adventure.zone = zone; }
    }

    internal sealed class AdventureController
    {
        internal Enemy currentEnemy;
        internal EnemyAI enemyAI;
        internal readonly Move regularAttackMove, strongAttackMove, pierceMove, ultimateAttackMove,
            chargeMove, parryMove, blockMove, healMove, hyperRegenMove, ultimateBuffMove, megaBuffMove,
            offenseBuffMove, defenseBuffMove, paralyzeMove, ohShitMove, beastModeMove, idleAttackMove;
        internal readonly ZoneSelector zoneSelector;
        internal List<List<Enemy>> enemyList = new List<List<Enemy>>();
        internal int itopodLevel;
        internal InputField itopodStartInput = new InputField(), itopodEndInput = new InputField();
        internal AdventureController(List<string> calls)
        {
            regularAttackMove = new Move(calls, "regular"); strongAttackMove = new Move(calls, "strong");
            pierceMove = new Move(calls, "pierce"); ultimateAttackMove = new Move(calls, "ultimate");
            chargeMove = new Move(calls, "charge"); parryMove = new Move(calls, "parry");
            blockMove = new Move(calls, "block"); healMove = new Move(calls, "heal");
            hyperRegenMove = new Move(calls, "regen"); ultimateBuffMove = new Move(calls, "ultimate-buff");
            megaBuffMove = new Move(calls, "mega-buff"); offenseBuffMove = new Move(calls, "offense-buff");
            defenseBuffMove = new Move(calls, "defense-buff"); paralyzeMove = new Move(calls, "paralyze");
            ohShitMove = new Move(calls, "oh-shit"); beastModeMove = new Move(calls, "beast");
            idleAttackMove = new Move(calls, "idle"); zoneSelector = new ZoneSelector(calls);
        }
        internal string zoneName(int zone) => "zone " + zone;
        internal int maxItopodLevel() => 100;
        internal void verifyItopodInputs() { }
    }
}

namespace NGUInjector.Managers
{
    internal enum LockType { Titan, Yggdrasil, MoneyPit, Gold, None }
    internal static class LoadoutManager
    {
        internal static LockType CurrentLock;
        internal static List<string> Calls;
        internal static string Equipped, Saved;
        internal static void Reset(List<string> calls)
        {
            Calls = calls;
            CurrentLock = LockType.None;
            Equipped = "prior-gear";
            Saved = null;
            GearOptimizer.FailEquip = false;
            BoostFarmHelper.Target = new BoostFarmTarget { Zone = 3, Summary = "fixture boosts", Score = 2 };
        }
        internal static bool CanSwap() => CurrentLock == LockType.None;
        internal static void SaveCurrentLoadout() { Saved = Equipped; Calls.Add("save:" + Saved); }
        internal static void AcquireLock(LockType type)
        {
            if (!CanSwap()) throw new InvalidOperationException("Acquired busy gear lock");
            CurrentLock = type;
            Calls.Add("acquire:" + type);
        }
        internal static void RestoreGear()
        {
            if (Saved == null) throw new InvalidOperationException("Restored without a gear snapshot");
            Equipped = Saved;
            Calls.Add("restore:" + Equipped);
        }
        internal static void ReleaseLock() { Calls.Add("release:" + CurrentLock); CurrentLock = LockType.None; }
    }

    internal enum GearOptimizationGoal { Gold }
    internal enum FightScoreMode { PowerTimesToughness }
    internal sealed class GearOptimizationResult { internal bool Success; internal string Message; }
    internal static class GearOptimizer
    {
        internal static bool FailEquip;
        internal static GearOptimizationResult OptimizeAndEquip(GearOptimizationGoal goal, FightScoreMode mode, bool allowLocked)
        {
            if (LoadoutManager.CurrentLock != LockType.Gold || !allowLocked)
                throw new InvalidOperationException("Gold optimization requires its owned lock");
            LoadoutManager.Calls.Add("optimize:" + goal + ":" + mode + ":" + allowLocked);
            if (!FailEquip) LoadoutManager.Equipped = "gold-gear";
            return new GearOptimizationResult { Success = !FailEquip, Message = FailEquip ? "fixture equip failure" : "fixture gold gear" };
        }
    }
    internal sealed class BoostFarmTarget
    {
        internal int Zone;
        internal bool IsItopod;
        internal string Summary;
        internal double Score;
    }
    internal static class BoostFarmHelper
    {
        internal static BoostFarmTarget Target;
        internal static BoostFarmTarget GetBestTarget() => Target;
        internal static string FormatTarget(BoostFarmTarget target) => target.Summary;
    }
    internal static class CustomStageAllocator
    {
        internal static string FormatSeconds(int seconds) => seconds + "s";
    }
}
