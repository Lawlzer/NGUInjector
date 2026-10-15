using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.Managers;
using Runtime = NGUInjector.Main;

namespace NGUInjector
{
    internal static partial class CombatChecks
    {
        private static int _assertions, _failures;
        private static readonly enemyType[] Walderps = { enemyType.waldo1, enemyType.waldo2, enemyType.waldo3, enemyType.waldo4, enemyType.bigBoss5 };
        private static readonly string[] AttackNames = { "regular", "strong", "pierce", "ultimate" };

        private sealed class Fight
        {
            internal readonly List<string> Calls = new List<string>();
            internal readonly Character Character = new Character();
            internal readonly WalderpAI AI = new WalderpAI();
            internal readonly CombatManager Manager;
            internal readonly Runtime RuntimeInstance;
            internal AdventureController Controller => Character.adventureController;
            internal Move[] Attacks => new[] { Controller.regularAttackMove, Controller.strongAttackMove, Controller.pierceMove, Controller.ultimateAttackMove };
            internal Fight(enemyType type = enemyType.bigBoss5)
            {
                Runtime.Character = Character;
                Runtime.PlayerController = new PlayerController();
                Runtime.Settings = new CombatSettings();
                LoadoutManager.Reset(Calls);
                Character.adventureController = new AdventureController(Calls) { currentEnemy = new Enemy { enemyType = type }, enemyAI = AI };
                foreach (var move in Attacks) move.button.interactable = true;
                Manager = new CombatManager();
                RuntimeInstance = new Runtime(Manager);
            }
            internal void Prompt(int id, bool says)
            {
                AI.inWaldoSaysLoop = true; AI.waldoAttackID = id; AI.waldoSays = says;
            }
            internal void BuffsReady()
            {
                Character.adventure.curHP = 25;
                foreach (var move in new[] { Controller.healMove, Controller.hyperRegenMove, Controller.megaBuffMove,
                    Controller.ultimateBuffMove, Controller.offenseBuffMove, Controller.defenseBuffMove,
                    Controller.blockMove, Controller.parryMove, Controller.paralyzeMove, Controller.chargeMove })
                    move.button.interactable = true;
            }
            internal void Tick(bool idle = false, bool bossOnly = false, bool fast = false, bool preserve = false)
            {
                if (idle) Manager.IdleZone(Character.adventure.zone, bossOnly, false);
                else Manager.ManualZone(Character.adventure.zone, bossOnly, false, false, fast, false, preserve);
            }
            internal void Expect(string name, params string[] expected)
            {
                _assertions++;
                if (Calls.SequenceEqual(expected)) return;
                _failures++;
                Console.Error.WriteLine("FAIL: " + name + "; expected [" + string.Join(", ", expected)
                    + "], got [" + string.Join(", ", Calls) + "]");
            }
        }

        public static int Main()
        {
            try
            {
                CheckPrompts();
                CheckClassificationAndRouting();
                CheckOrdinaryWalderp();
                CheckNormalCombat();
                var combatAssertions = _assertions;
                CheckCustomAdventure();
                Console.WriteLine("Custom adventure: " + (_assertions - combatAssertions) + " assertions.");
                Console.WriteLine((_failures == 0 ? "PASS: " : "FAIL: ") + _assertions + " assertions, " + _failures
                    + " failures against real custom-adventure routing, CombatManager, CombatHelpers, and ZoneHelpers; game/Unity boundaries stubbed.");
                return _failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void CheckPrompts()
        {
            foreach (var idle in new[] { false, true })
            foreach (var id in Enumerable.Range(3, 4))
            {
                var fight = new Fight();
                fight.Prompt(id, true);
                fight.BuffsReady();
                fight.AI.growCount = 0;
                fight.Tick(idle);
                fight.Expect($"true prompt {id}, idle={idle}: exact move before buffs/reservation", AttackNames[id - 3]);

                fight = new Fight();
                fight.Prompt(id, true);
                fight.BuffsReady();
                fight.Attacks[id - 3].button.interactable = false;
                fight.Tick(idle);
                fight.Expect($"true prompt {id}, idle={idle}: unavailable exact move waits, no fallback/buff");

                // Every distinct alternative must work, not just the first available move.
                foreach (var alternate in Enumerable.Range(3, 4).Where(other => other != id))
                {
                    fight = new Fight();
                    fight.Prompt(id, false);
                    fight.BuffsReady();
                    fight.AI.growCount = 1;
                    for (var i = 0; i < 4; i++) fight.Attacks[i].button.interactable = i + 3 == id || i + 3 == alternate;
                    fight.Tick(idle);
                    fight.Expect($"false prompt {id}, alternate {alternate}, idle={idle}", AttackNames[alternate - 3]);
                }

                fight = new Fight();
                fight.Prompt(id, false);
                fight.BuffsReady();
                for (var i = 0; i < 4; i++) fight.Attacks[i].button.interactable = i + 3 == id;
                fight.Tick(idle);
                fight.Expect($"false prompt {id}, idle={idle}: only forbidden move ready waits");
            }

            foreach (var says in new[] { false, true })
            foreach (var id in new[] { int.MinValue, -1, 0, 1, 2, 7, int.MaxValue })
            {
                var fight = new Fight();
                fight.Prompt(id, says);
                fight.BuffsReady();
                fight.Tick();
                fight.Expect($"invalid prompt {id}, says={says}: no attack or buff");
            }

            foreach (var ai in new EnemyAI[] { new MissingAttackPromptAI(), new MissingSaysPromptAI() })
            {
                var fight = new Fight();
                fight.Controller.enemyAI = ai;
                fight.BuffsReady();
                fight.Tick();
                fight.Expect(ai.GetType().Name + ": incomplete prompt waits");
            }

            foreach (var timerBlocked in new[] { false, true })
            {
                var fight = new Fight();
                fight.Prompt(6, true);
                if (timerBlocked) Runtime.PlayerController.moveTimer = 1;
                else Runtime.PlayerController.CanMove = false;
                fight.Tick();
                fight.Expect("movement gate blocks prompt, timerBlocked=" + timerBlocked);
            }
        }

        private static void CheckClassificationAndRouting()
        {
            foreach (var type in Walderps)
            foreach (var idle in new[] { false, true })
            {
                var fight = new Fight(type);
                fight.Character.adventure.zone = 17; // Type identity, not zone or enemy/AI name.
                fight.Prompt(6, true);
                fight.Tick(idle, bossOnly: true);
                fight.Expect($"{type}, idle={idle}: bossOnly accepts Walderp with neutral names outside zone 19", "ultimate");

                fight = new Fight(type);
                fight.Character.adventure.autoattacking = true;
                fight.Prompt(5, true);
                fight.Tick(idle);
                fight.Expect($"{type}, idle={idle}: disable idle before attacking",
                    idle ? new[] { "idle:off" } : new[] { "idle:off", "pierce" });
                fight.Calls.Clear();
                fight.Tick(idle);
                fight.Expect($"{type}, idle={idle}: idle stays off while answering prompt", "pierce");
            }

            foreach (var type in new[] { enemyType.normal, enemyType.boss, enemyType.bigBoss6V1,
                enemyType.bigBoss6V2, enemyType.bigBoss6V3, enemyType.bigBoss6V4 })
            foreach (var name in new[] { "Walderp", "Waldo", "Demented Hiker", "The Beast", "unrelated", null })
            {
                var fight = new Fight(type);
                fight.Controller.currentEnemy.name = name;
                fight.Prompt(6, true);
                fight.AI.growCount = 0;
                fight.Controller.ultimateAttackMove.button.interactable = false;
                fight.Tick(fast: true);
                fight.Expect($"{type}, name={name ?? "null"}: zone 19 does not imply Walderp", "pierce");
            }
        }

        private static void CheckOrdinaryWalderp()
        {
            foreach (var idle in new[] { false, true })
            foreach (var fast in new[] { false, true })
            foreach (var grow in Enumerable.Range(0, 8))
            {
                var fight = new Fight();
                fight.AI.growCount = grow;
                if (grow == 0 || grow == 1 || grow == 6 || grow == 7) fight.BuffsReady();
                fight.Tick(idle, fast: fast);
                fight.Expect($"ordinary Walderp growCount={grow}, idle={idle}, fast={fast}",
                    grow == 0 || grow == 1 || grow == 6 || grow == 7 ? Array.Empty<string>() : new[] { "regular" });
            }

            var unavailable = new Fight();
            unavailable.Controller.regularAttackMove.button.interactable = false;
            unavailable.Tick(fast: true);
            unavailable.Expect("ordinary Walderp waits for regular instead of spending another attack");

            foreach (var preserve in new[] { false, true })
            foreach (var auto in new[] { false, true })
            {
                var fight = new Fight();
                fight.Character.training.attackTraining[1] = 0;
                fight.Character.adventure.autoattacking = auto;
                for (var tick = 0; tick < 3; tick++) fight.Tick(preserve: preserve);
                fight.Expect($"regular unlocked, strong untrained, preserve={preserve}, idle initially={auto}: no oscillation",
                    auto ? new[] { "idle:off", "regular", "regular" } : new[] { "regular", "regular", "regular" });
            }
        }

        private static void CheckNormalCombat()
        {
            var fight = new Fight(enemyType.normal);
            fight.Tick(idle: true);
            fight.Tick(idle: true);
            fight.Expect("normal IdleZone enables idle once and does not cast manual attacks", "idle:on");

            fight = new Fight(enemyType.normal);
            fight.Character.training.attackTraining[1] = 0;
            fight.Tick();
            fight.Tick();
            fight.Expect("normal manual combat without strong training retains idle fallback", "idle:on");

            fight = new Fight(enemyType.normal);
            fight.Character.adventure.autoattacking = true;
            fight.Controller.ultimateAttackMove.button.interactable = false;
            fight.Tick(fast: true);
            fight.Expect("normal trained manual combat turns idle off and attacks", "idle:off", "pierce");

            foreach (var available in new[] { 5, 4, 3 })
            {
                fight = new Fight(enemyType.normal);
                for (var i = 0; i < 4; i++) fight.Attacks[i].button.interactable = i + 3 <= available;
                fight.Tick(fast: true);
                fight.Expect("normal attack fallback, highest available=" + available, AttackNames[available - 3]);
            }

            fight = new Fight(enemyType.normal);
            fight.BuffsReady();
            fight.Tick();
            fight.Expect("normal combat still prioritizes healing", "heal");

            fight = new Fight(enemyType.normal);
            fight.BuffsReady();
            fight.Controller.ultimateAttackMove.button.interactable = false;
            fight.Tick(fast: true);
            fight.Expect("normal fast combat still bypasses buffs", "pierce");

            fight = new Fight(enemyType.normal);
            fight.Controller.ultimateAttackMove.button.interactable = false;
            fight.Tick(preserve: true);
            fight.Tick(fast: true, preserve: true);
            fight.Expect("normal preserveIdleMode keeps idle on", "idle:on", "pierce");

            foreach (var idle in new[] { false, true })
            {
                fight = new Fight(enemyType.normal);
                fight.Character.adventure.autoattacking = idle;
                fight.Tick(idle, bossOnly: true, fast: true);
                fight.Expect("normal bossOnly still rejects trash, idle=" + idle,
                    idle ? new[] { "zone:-1" } : new[] { "zone:-1", "zone:19" });

                fight = new Fight(enemyType.boss);
                fight.Character.adventure.autoattacking = idle;
                fight.Controller.ultimateAttackMove.button.interactable = false;
                fight.Tick(idle, bossOnly: true, fast: true);
                fight.Expect("normal bossOnly still accepts bosses, idle=" + idle,
                    idle ? Array.Empty<string>() : new[] { "pierce" });
            }
        }
    }
}
