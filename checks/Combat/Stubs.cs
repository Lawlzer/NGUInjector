using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Decision inputs and record-only move requests. There is deliberately no setup,
// route producer, selected-support, grant, lease, snapshot or restoration model.
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
        internal static void Log(string text) { }
    }
    internal sealed class CombatSettings
    {
        internal bool MoreBlockParry, ManageGoldLoadouts, SwapTitanLoadouts, OptimizeITOPODFloor;
        internal int CustomAdventureMode, CustomAdventureZone, ITOPODCombatMode;
        internal AdventureRule[] CustomAdventureRules = Array.Empty<AdventureRule>();
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
        internal float totalAdvHP() => 100;
        internal float chargeCooldown() => 10;
        internal float RegularCooldown = 1, StrongCooldown = 6, PierceCooldown = 10, UltimateCooldown = 15;
        internal Inventory inventory = new Inventory();
        internal float regAttackCooldown() => RegularCooldown;
        internal float strongAttackCooldown() => StrongCooldown;
        internal float pierceAttackCooldown() => PierceCooldown;
        internal float ultimateAttackCooldown() => UltimateCooldown;
        internal float ultimateBuffDuration() => 10;
        internal float defenseBuffDuration() => 10;
        internal int effectiveBossID() => EffectiveBossId;
        internal float AdventureAttack = 1000, AdventureDefense, AdventureRegen;
        internal float totalAdvAttack() => AdventureAttack;
        internal float totalAdvDefense() => AdventureDefense;
        internal float totalAdvHPRegen() => AdventureRegen;
        internal float idleAttackPower() => 1;
        internal float regAttackPower() => 1;
    }
    internal class Adventure { internal int zone = 19, boss5Kills, highestItopodLevel; internal float curHP = 100; }
    internal sealed class Arbitrary { internal bool boughtLazyITOPOD, lazyITOPODOn; }
    internal sealed class InputField { internal string text; }
    internal sealed class ZoneSelector { internal void changeZone(int zone) => throw new NotSupportedException("Native route belongs to the compiled fixture."); }
    internal sealed class TitanAdventure : Adventure
    {
        public PlayerTime boss1Spawn = new PlayerTime(), boss2Spawn = new PlayerTime(), boss3Spawn = new PlayerTime(),
            boss4Spawn = new PlayerTime(), boss5Spawn = new PlayerTime(), boss6Spawn = new PlayerTime(),
            boss7Spawn = new PlayerTime(), boss8Spawn = new PlayerTime(), boss9Spawn = new PlayerTime(),
            boss10Spawn = new PlayerTime(), boss11Spawn = new PlayerTime(), boss12Spawn = new PlayerTime();
        public bool titan6Unlocked, titan7Unlocked, titan8Unlocked, titan9Unlocked, titan10Unlocked, titan11Unlocked, titan12Unlocked;
        public int titan6Version, titan7Version, titan8Version, titan9Version, titan10Version, titan11Version, titan12Version;
    }
    internal sealed class UnversionedTitanAdventure : Adventure { public bool titan6Unlocked = true; }
    internal sealed class Training { internal long[] attackTraining = { 5000, 1, 0, 0, 0, 0 }, defenseTraining = new long[6]; }
    internal sealed class Wish { internal int level; }
    internal sealed class Wishes { internal Wish[] wishes = Enumerable.Range(0, 59).Select(_ => new Wish()).ToArray(); }
    internal sealed class Challenges { internal bool ParalyzeUnlocked; internal bool hasParalyze() => ParalyzeUnlocked; }
    internal sealed class GameSettings { internal bool hasHyperRegen, autoKillTitans; }
    internal sealed class Inventory { internal ItemList itemList = new ItemList(); }
    internal sealed class ItemList { internal bool redLiquidComplete; internal bool[] itemMaxxed = new bool[136]; }
    internal sealed class Buttons { internal Button adventure = new Button(); }
    internal sealed class PlayerTime { internal double totalseconds; }
    internal sealed class PlayerController
    {
        internal bool CanMove = true, isParrying, isBlocking;
        internal float moveTimer, chargeFactor = 1, ultimateBuffTime, defenseBuffTime, hyperRegenTime;
        internal float CooldownSpeed = 1;
        internal bool moveCheck() => CanMove;
        internal float timeDilation(float seconds) => seconds * CooldownSpeed;
    }
    internal sealed class Enemy
    {
        internal string name = "Unremarkable enemy";
        internal enemyType enemyType;
        internal AI AI;
        internal float curHP = 100, maxHP = 100, attackRate = 10;
    }
    internal class EnemyAI
    {
        public int chargeCooldown, rapidEffect;
        public float enemyAttackTimer, paralyzeTime;
        public bool firstStrike, rapidMode;
        internal T GetPV<T>(string name) => (T)GetType().GetField(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this);
    }
    internal sealed class WalderpAI : EnemyAI { public bool inWaldoSaysLoop, waldoSays; public int waldoAttackID = 3, growCount = 2; }
    internal sealed class MissingAttackPromptAI : EnemyAI { public bool inWaldoSaysLoop = true, waldoSays = true; }
    internal sealed class MissingSaysPromptAI : EnemyAI { public bool inWaldoSaysLoop = true; public int waldoAttackID = 3; }
    internal sealed class MissingCountAI : EnemyAI { public bool inWaldoSaysLoop; }
    internal sealed class MissingPromptStateAI : EnemyAI { public int growCount = 2; }
    internal sealed class Button { internal bool interactable; internal bool IsInteractable() => interactable; }
    internal sealed class Move
    {
        internal readonly Button button = new Button();
        private readonly List<string> _calls;
        private readonly string _name;
        public float chargeTimer, ultimateAttackTimer;
        internal Action OnUse;
        internal Move(List<string> calls, string name) { _calls = calls; _name = name; }
        internal void doMove()
        {
            if (!button.IsInteractable()) throw new InvalidOperationException("Unavailable move requested: " + _name);
            _calls.Add(_name);
            OnUse?.Invoke();
        }
    }
    internal class AdventureController
    {
        internal List<List<Enemy>> enemyList = new List<List<Enemy>>();
        internal int itopodLevel;
        internal InputField itopodStartInput = new InputField(), itopodEndInput = new InputField();
        internal ZoneSelector zoneSelector = new ZoneSelector();
        internal int maxItopodLevel() => 100;
        internal void verifyItopodInputs() => throw new NotSupportedException("Native settings belong to the compiled fixture.");
        internal Enemy currentEnemy;
        internal EnemyAI enemyAI;
        internal readonly Move regularAttackMove, strongAttackMove, pierceMove, ultimateAttackMove,
            chargeMove, parryMove, blockMove, healMove, hyperRegenMove, ultimateBuffMove, megaBuffMove,
            offenseBuffMove, defenseBuffMove, paralyzeMove, ohShitMove;
        internal AdventureController(List<string> calls)
        {
            regularAttackMove = new Move(calls, "regular"); strongAttackMove = new Move(calls, "strong");
            pierceMove = new Move(calls, "pierce"); ultimateAttackMove = new Move(calls, "ultimate");
            chargeMove = new Move(calls, "charge"); parryMove = new Move(calls, "parry"); blockMove = new Move(calls, "block");
            healMove = new Move(calls, "heal"); hyperRegenMove = new Move(calls, "regen");
            ultimateBuffMove = new Move(calls, "ultimate-buff"); megaBuffMove = new Move(calls, "mega-buff");
            offenseBuffMove = new Move(calls, "offense-buff"); defenseBuffMove = new Move(calls, "defense-buff");
            paralyzeMove = new Move(calls, "paralyze"); ohShitMove = new Move(calls, "oh-shit");
        }
    }
    // Explicit native eligibility boundary, never executor authorization.
    internal sealed class TitanAdventureController : AdventureController
    {
        internal readonly float[] SpawnIntervals = Enumerable.Repeat(100f, 12).ToArray();
        internal readonly bool[,] NativeAutokillResults = new bool[12, 4];
        internal readonly List<string> NativeAutokillCalls = new List<string>();
        internal bool FailSpawnInterval, FailNativeAutokill;
        internal TitanAdventureController(List<string> calls) : base(calls) { }
        private float SpawnInterval(int index) { if (FailSpawnInterval) throw new InvalidOperationException("native spawn failure"); return SpawnIntervals[index]; }
        private bool NativeAutokill(int titan, int version)
        {
            NativeAutokillCalls.Add($"{titan}:V{version}");
            if (FailNativeAutokill) throw new InvalidOperationException("native autokill failure");
            return NativeAutokillResults[titan - 1, version - 1];
        }
        public float boss1SpawnTime() => SpawnInterval(0);
        public float boss2SpawnTime() => SpawnInterval(1);
        public float boss3SpawnTime() => SpawnInterval(2);
        public float boss4SpawnTime() => SpawnInterval(3);
        public float boss5SpawnTime() => SpawnInterval(4);
        public float boss6SpawnTime() => SpawnInterval(5);
        public float boss7SpawnTime() => SpawnInterval(6);
        public float boss8SpawnTime() => SpawnInterval(7);
        public float boss9SpawnTime() => SpawnInterval(8);
        public float boss10SpawnTime() => SpawnInterval(9);
        public float boss11SpawnTime() => SpawnInterval(10);
        public float boss12SpawnTime() => SpawnInterval(11);
        public bool autokillTitan6V1Achieved() => NativeAutokill(6, 1);
        public bool autokillTitan6V2Achieved() => NativeAutokill(6, 2);
        public bool autokillTitan6V3Achieved() => NativeAutokill(6, 3);
        public bool autokillTitan6V4Achieved() => NativeAutokill(6, 4);
        public bool autokillTitan7V1Achieved() => NativeAutokill(7, 1);
        public bool autokillTitan7V2Achieved() => NativeAutokill(7, 2);
        public bool autokillTitan7V3Achieved() => NativeAutokill(7, 3);
        public bool autokillTitan7V4Achieved() => NativeAutokill(7, 4);
        public bool autokillTitan8V1Achieved() => NativeAutokill(8, 1);
        public bool autokillTitan8V2Achieved() => NativeAutokill(8, 2);
        public bool autokillTitan8V3Achieved() => NativeAutokill(8, 3);
        public bool autokillTitan8V4Achieved() => NativeAutokill(8, 4);
        public bool autokillTitan9V1Achieved() => NativeAutokill(9, 1);
        public bool autokillTitan9V2Achieved() => NativeAutokill(9, 2);
        public bool autokillTitan9V3Achieved() => NativeAutokill(9, 3);
        public bool autokillTitan9V4Achieved() => NativeAutokill(9, 4);
        public bool autokillTitan10V1Achieved() => NativeAutokill(10, 1);
        public bool autokillTitan10V2Achieved() => NativeAutokill(10, 2);
        public bool autokillTitan10V3Achieved() => NativeAutokill(10, 3);
        public bool autokillTitan10V4Achieved() => NativeAutokill(10, 4);
        public bool autokillTitan11V1Achieved() => NativeAutokill(11, 1);
        public bool autokillTitan11V2Achieved() => NativeAutokill(11, 2);
        public bool autokillTitan11V3Achieved() => NativeAutokill(11, 3);
        public bool autokillTitan11V4Achieved() => NativeAutokill(11, 4);
        public bool autokillTitan12V1Achieved() => NativeAutokill(12, 1);
        public bool autokillTitan12V2Achieved() => NativeAutokill(12, 2);
        public bool autokillTitan12V3Achieved() => NativeAutokill(12, 3);
        public bool autokillTitan12V4Achieved() => NativeAutokill(12, 4);
    }
}
