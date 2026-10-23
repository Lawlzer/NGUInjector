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
                Character.adventureController = new AdventureController(Calls) { currentEnemy = new Enemy { enemyType = type }, enemyAI = AI };
                foreach (var move in Attacks) move.button.interactable = true;
                Manager = new CombatManager();
                RuntimeInstance = new Runtime();
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
            internal void Tick(bool fast = false)
            {
                Manager.Decide(fast);
            }
            internal void Expect(string name, params string[] expected)
            {
                _assertions++;
                var actions = Calls.ToArray();
                if (actions.SequenceEqual(expected)) return;
                _failures++;
                Console.Error.WriteLine("FAIL: " + name + "; expected [" + string.Join(", ", expected)
                    + "], got [" + string.Join(", ", actions) + "]");
            }
        }

        public static int Main()
        {
            try
            {
                CheckPrompts();
                CheckClassificationAndRouting();
                CheckOrdinaryWalderp();
                CheckWalderpTiming();
                CheckWalderpUtilities();
                CheckSurvival();
                CheckSkillUnlocks();
                CheckWalderpDefense();
                CheckNormalCombat();
                CheckNonFastPriorities();
                CheckAdventureRules();
                var titanAssertions = _assertions;
                CheckTitanEligibility();
                Console.WriteLine("Titan eligibility: " + (_assertions - titanAssertions) + " assertions.");
                Console.WriteLine((_failures == 0 ? "PASS: " : "FAIL: ") + _assertions + " assertions, " + _failures
                    + " failures against production combat decisions, rule reads and ZoneHelpers; no executor or native route acceptance is claimed.");
                return _failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void CheckPrompts()
        {
            foreach (var fast in new[] { false, true })
            foreach (var id in Enumerable.Range(3, 4))
            {
                var fight = new Fight();
                fight.Prompt(id, true);
                fight.BuffsReady();
                fight.AI.growCount = 0;
                fight.Tick(fast: fast);
                fight.Expect($"true prompt {id}, fast={fast}: exact move before buffs/reservation", AttackNames[id - 3]);

                fight = new Fight();
                fight.Prompt(id, true);
                fight.BuffsReady();
                fight.Attacks[id - 3].button.interactable = false;
                fight.Tick(fast: fast);
                fight.Expect($"true prompt {id}, fast={fast}: unavailable exact move reserves shared cooldown, no utility");

                fight = new Fight();
                fight.Prompt(id, true);
                fight.Attacks[id - 3].button.interactable = false;
                fight.Tick(fast: fast);
                fight.Expect($"true prompt {id}, fast={fast}: no utility does not permit a wrong attack");

                // Every distinct alternative must work, not just the first available move.
                foreach (var alternate in Enumerable.Range(3, 4).Where(other => other != id))
                {
                    fight = new Fight();
                    fight.Prompt(id, false);
                    fight.BuffsReady();
                    fight.AI.growCount = 1;
                    for (var i = 0; i < 4; i++) fight.Attacks[i].button.interactable = i + 3 == id || i + 3 == alternate;
                    fight.Tick(fast: fast);
                    fight.Expect($"false prompt {id}, alternate {alternate}, fast={fast}", AttackNames[alternate - 3]);
                }

                fight = new Fight();
                fight.Prompt(id, false);
                fight.BuffsReady();
                for (var i = 0; i < 4; i++) fight.Attacks[i].button.interactable = i + 3 == id;
                fight.Tick(fast: fast);
                fight.Expect($"false prompt {id}, fast={fast}: only forbidden move ready reserves shared cooldown");

                fight = new Fight();
                fight.Prompt(id, false);
                for (var i = 0; i < 4; i++) fight.Attacks[i].button.interactable = i + 3 == id;
                fight.Tick(fast: fast);
                fight.Expect($"false prompt {id}, fast={fast}: no utility does not permit the forbidden attack");
            }

            foreach (var says in new[] { false, true })
            foreach (var id in new[] { int.MinValue, -1, 0, 1, 2, 7, int.MaxValue })
            {
                var fight = new Fight();
                fight.Prompt(id, says);
                fight.BuffsReady();
                fight.Tick();
                fight.Expect($"invalid prompt {id}, says={says}: no utility or attack");

                fight = new Fight();
                fight.Prompt(id, says);
                fight.Tick();
                fight.Expect($"invalid prompt {id}, says={says}: no utility still waits");
            }

            foreach (var ai in new EnemyAI[] { new MissingAttackPromptAI(), new MissingSaysPromptAI() })
            {
                var fight = new Fight();
                fight.Controller.enemyAI = ai;
                fight.BuffsReady();
                fight.Tick();
                fight.Expect(ai.GetType().Name + ": incomplete prompt reserves shared cooldown");

                fight = new Fight();
                fight.Controller.enemyAI = ai;
                fight.Tick();
                fight.Expect(ai.GetType().Name + ": incomplete prompt without utility still waits");
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
            foreach (var fast in new[] { false, true })
            {
                var fight = new Fight(type);
                fight.Character.adventure.zone = 17; // Type identity, not zone or enemy/AI name.
                fight.Prompt(6, true);
                fight.Tick(fast: fast);
                fight.Expect($"{type}, fast={fast}: Walderp classification ignores names and zone", "ultimate");

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
            foreach (var fast in new[] { false, true })
            foreach (var grow in Enumerable.Range(0, 14))
            {
                var fight = new Fight();
                fight.Controller.currentEnemy.attackRate = 3;
                fight.AI.growCount = grow;
                fight.Tick(fast: fast);
                var turns = new[] { 2, 1, 6, 5, 4, 3 }[grow % 6];
                var expected = turns == 6 ? "ultimate" : turns >= 4 ? "pierce" : turns >= 3 ? "strong" : "regular";
                fight.Expect($"ordinary Walderp growCount={grow}, fast={fast}",
                    expected);
            }

            var unavailable = new Fight();
            unavailable.Controller.ultimateAttackMove.button.interactable = false;
            unavailable.Tick(fast: true);
            unavailable.Expect("ordinary Walderp falls back to next ready safe attack", "pierce");

            {
                var fight = new Fight();
                fight.Character.training.attackTraining[1] = 0;
                for (var i = 1; i < 4; i++) fight.Attacks[i].button.interactable = false;
                for (var tick = 0; tick < 3; tick++) fight.Tick();
                fight.Expect("regular unlocked, strong untrained: repeated decisions select regular",
                    "regular", "regular", "regular");
            }
        }

        private static void CheckWalderpTiming()
        {
            // At count 1, the next enemy turn issues the prompt: 3 seconds minus elapsed time.
            foreach (var id in Enumerable.Range(3, 4))
            foreach (var cooldown in new[] { 2.74f, 2.75f, 2.76f })
            {
                var fight = new Fight();
                fight.AI.growCount = 1;
                fight.Controller.currentEnemy.attackRate = 3;
                fight.Character.RegularCooldown = fight.Character.StrongCooldown =
                    fight.Character.PierceCooldown = fight.Character.UltimateCooldown = cooldown;
                for (var i = 0; i < 4; i++) fight.Attacks[i].button.interactable = i + 3 == id;
                fight.Tick();
                fight.Expect($"attack {id} cooldown {cooldown} vs 2.75s budget",
                    cooldown <= 2.75f ? new[] { AttackNames[id - 3] } : Array.Empty<string>());
            }

            foreach (var redLiquid in new[] { false, true })
            foreach (var offset in new[] { -.01f, 0, .01f })
            {
                if (redLiquid && offset == 0) continue; // 0.8 is not exactly representable as a float.
                var fight = new Fight();
                fight.Character.inventory.itemList.redLiquidComplete = redLiquid;
                fight.Character.RegularCooldown = .1f;
                fight.AI.growCount = 1;
                fight.Controller.currentEnemy.attackRate = 4;
                var gcd = redLiquid ? .8f : 1;
                fight.AI.enemyAttackTimer = 4 - .25f - gcd + offset;
                fight.Tick();
                fight.Expect($"shared cooldown redLiquid={redLiquid}, offset={offset}",
                    offset <= 0 ? new[] { "regular" } : Array.Empty<string>());
            }

            foreach (var timer in new[] { 0f, 1f, 1.76f, 3f, 30f })
            {
                var fight = new Fight();
                fight.AI.growCount = 1;
                fight.Controller.currentEnemy.attackRate = 3;
                fight.AI.enemyAttackTimer = timer;
                fight.BuffsReady();
                fight.Controller.paralyzeMove.button.interactable = false;
                fight.Tick();
                fight.Expect($"healing ignores attack reservation, elapsed={timer}", "heal");
            }

            foreach (var speed in new[] { 1f, .5f, 1f / 3 })
            {
                var fight = new Fight();
                fight.AI.growCount = 0;
                fight.Controller.currentEnemy.attackRate = 3;
                fight.Character.StrongCooldown = 2;
                Runtime.PlayerController.CooldownSpeed = speed;
                fight.Tick();
                fight.Expect("cooldown clock speed=" + speed, speed < .5f ? "regular" : "strong");
            }

            foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -1f })
            foreach (var input in new[] { "rate", "timer", "speed", "cooldown" })
            {
                if (input == "timer" && invalid <= 0) continue;
                var fight = new Fight();
                if (input == "rate") fight.Controller.currentEnemy.attackRate = invalid;
                if (input == "timer") fight.AI.enemyAttackTimer = invalid;
                if (input == "speed") Runtime.PlayerController.CooldownSpeed = invalid;
                if (input == "cooldown") fight.Character.RegularCooldown = fight.Character.StrongCooldown =
                    fight.Character.PierceCooldown = fight.Character.UltimateCooldown = invalid;
                fight.Tick();
                fight.Expect($"invalid {input}={invalid} waits");
                fight.Prompt(6, true);
                fight.Tick();
                fight.Expect($"invalid {input}={invalid} does not block prompt", "ultimate");
            }

            foreach (var missing in new EnemyAI[] { new MissingCountAI(), new MissingPromptStateAI(), new MissingAttackPromptAI() })
            {
                var fight = new Fight();
                fight.Controller.enemyAI = missing;
                fight.Tick();
                fight.Expect("unknown prompt/count state waits: " + missing.GetType().Name);
            }

            var negativeCount = new Fight();
            negativeCount.AI.growCount = -1;
            negativeCount.Tick();
            negativeCount.Expect("invalid negative enemy turn count waits");

            CheckWalderpCycles();
        }

        private static void CheckWalderpUtilities()
        {
            foreach (var utility in new[] { "heal", "regen", "ultimate-buff", "offense-buff", "defense-buff", "block", "parry" })
            foreach (var state in new[] { "near prompt", "waiting for attack", "unknown prompt", "missing timing", "invalid timing" })
            {
                var fight = new Fight();
                fight.AI.growCount = 1;
                fight.Controller.currentEnemy.attackRate = 3;
                fight.AI.enemyAttackTimer = 3;
                if (state == "waiting for attack")
                {
                    fight.Prompt(6, true);
                    fight.Controller.ultimateAttackMove.button.interactable = false;
                }
                if (state == "unknown prompt") fight.Controller.enemyAI = new MissingPromptStateAI();
                if (state == "missing timing") fight.Controller.enemyAI = new MissingCountAI();
                if (state == "invalid timing") fight.AI.enemyAttackTimer = float.NaN;

                switch (utility)
                {
                    case "heal":
                        fight.Character.adventure.curHP = 25;
                        fight.Controller.healMove.button.interactable = true;
                        break;
                    case "regen":
                        fight.Character.adventure.curHP = 25;
                        fight.Controller.hyperRegenMove.button.interactable = true;
                        break;
                    case "ultimate-buff": fight.Controller.ultimateBuffMove.button.interactable = true; break;
                    case "offense-buff":
                        Runtime.PlayerController.ultimateBuffTime = 1;
                        fight.Controller.offenseBuffMove.button.interactable = true;
                        break;
                    case "defense-buff":
                        fight.Character.adventure.curHP = 60;
                        fight.Controller.defenseBuffMove.button.interactable = true;
                        break;
                    case "block": fight.Controller.blockMove.button.interactable = true; break;
                    case "parry": fight.Controller.parryMove.button.interactable = true; break;
                }
                fight.Tick();
                var wait = state == "waiting for attack" || state == "unknown prompt"
                    || utility == "block" || utility == "parry"; // Healthy fights reserve defenses instead of preparing them.
                fight.Expect($"Walderp utility {utility}, {state}", wait ? Array.Empty<string>() : new[] { utility });
            }

            var charge = new Fight();
            charge.AI.growCount = 1;
            charge.AI.enemyAttackTimer = charge.Controller.currentEnemy.attackRate;
            charge.Controller.chargeMove.button.interactable = true;
            charge.Tick();
            charge.Expect("Walderp can charge while reserving attacks", "charge");

            var cooldown = new Fight();
            cooldown.BuffsReady();
            Runtime.PlayerController.moveTimer = 1;
            cooldown.Tick();
            cooldown.Expect("Walderp utilities still respect the shared move cooldown");

            var fast = new Fight();
            fast.Prompt(6, true);
            fast.Controller.ultimateAttackMove.button.interactable = false;
            fast.BuffsReady();
            fast.Tick(fast: true);
            fast.Expect("Walderp fast combat still opts out of utility skills while waiting");
        }

        private static void CheckWalderpCycles()
        {
            foreach (var slowed in new[] { false, true })
            foreach (var says in new[] { false, true })
            foreach (var requested in Enumerable.Range(3, 4))
            foreach (var utilities in new[] { false, true })
            {
                var fight = new Fight();
                fight.Controller.currentEnemy.attackRate = 3;
                fight.AI.growCount = 2; // A prompt has just been answered.
                Runtime.PlayerController.CooldownSpeed = slowed ? .5f : 1;
                fight.Character.inventory.itemList.redLiquidComplete = slowed;
                var gcd = slowed ? .8f : 1;
                // Each move must be capable of recovering between consecutive prompts.
                if (slowed)
                {
                    fight.Character.PierceCooldown = 7;
                    fight.Character.UltimateCooldown = 8;
                }
                var remaining = new float[4];
                var cooldowns = new[] { fight.Character.RegularCooldown, fight.Character.StrongCooldown,
                    fight.Character.PierceCooldown, fight.Character.UltimateCooldown };
                var answered = 0;
                var utilityMoves = new[] { fight.Controller.healMove, fight.Controller.hyperRegenMove,
                    fight.Controller.ultimateBuffMove, fight.Controller.offenseBuffMove, fight.Controller.defenseBuffMove,
                    fight.Controller.blockMove, fight.Controller.parryMove };
                var utilityRemaining = new float[utilityMoves.Length];
                var blockTime = -1f;
                var blockAwaitingHit = false;
                for (var i = 0; i < utilityMoves.Length; i++)
                {
                    var utility = i;
                    utilityMoves[i].OnUse = () =>
                    {
                        ExpectValue("timed utility never spends an active prompt's shared cooldown", false, fight.AI.inWaldoSaysLoop);
                        Runtime.PlayerController.moveTimer = gcd;
                        utilityRemaining[utility] = 10;
                        if (utility == 0) fight.Character.adventure.curHP = Math.Min(100, fight.Character.adventure.curHP + 15);
                        if (utility == 1) Runtime.PlayerController.hyperRegenTime = 5;
                        if (utility == 2) Runtime.PlayerController.ultimateBuffTime = 0;
                        if (utility == 4) Runtime.PlayerController.defenseBuffTime = 0;
                        if (utility == 5) { Runtime.PlayerController.isBlocking = true; blockTime = 0; blockAwaitingHit = true; }
                        if (utility == 6) Runtime.PlayerController.isParrying = true;
                    };
                }
                for (var i = 0; i < 4; i++)
                {
                    var attack = i;
                    fight.Attacks[i].OnUse = () =>
                    {
                        remaining[attack] = cooldowns[attack];
                        Runtime.PlayerController.moveTimer = gcd;
                        if (!fight.AI.inWaldoSaysLoop) return;
                        ExpectValue("timed cycle obeys true/false prompt", says, attack + 3 == requested);
                        fight.AI.inWaldoSaysLoop = false;
                        answered++;
                    };
                }

                for (var tick = 0; tick < 600; tick++)
                {
                    Runtime.PlayerController.moveTimer -= .1f;
                    Runtime.PlayerController.hyperRegenTime -= .1f;
                    Runtime.PlayerController.ultimateBuffTime += .1f;
                    Runtime.PlayerController.defenseBuffTime += .1f;
                    if (blockTime >= 0) blockTime += .1f;
                    if (blockTime >= 3) Runtime.PlayerController.isBlocking = false;
                    for (var i = 0; i < utilityMoves.Length; i++)
                    {
                        utilityRemaining[i] -= .1f;
                        utilityMoves[i].button.interactable = utilities && utilityRemaining[i] < 0 && Runtime.PlayerController.moveTimer <= 0;
                    }
                    for (var i = 0; i < 4; i++)
                    {
                        remaining[i] -= Runtime.PlayerController.timeDilation(.1f);
                        fight.Attacks[i].button.interactable = remaining[i] < 0 && Runtime.PlayerController.moveTimer <= 0;
                    }
                    fight.AI.enemyAttackTimer += .1f;
                    if (fight.AI.enemyAttackTimer > 3)
                    {
                        fight.AI.enemyAttackTimer = 0;
                        fight.AI.growCount++;
                        if (fight.AI.growCount % 6 == 2)
                        {
                            for (var i = 0; i < 4; i++)
                                ExpectValue("all attacks ready when timed prompt appears: " + AttackNames[i], true,
                                    remaining[i] < 0); // A pre-prompt utility may still occupy the shared cooldown.
                            fight.Prompt(requested, says);
                        }
                        else
                        {
                            ExpectValue("timed prompt answered before deadline", false, fight.AI.inWaldoSaysLoop);
                            if (blockAwaitingHit)
                            {
                                ExpectValue("timed Block still covers the next damaging turn", true, Runtime.PlayerController.isBlocking);
                                blockAwaitingHit = false;
                            }
                            fight.Character.adventure.curHP = 40;
                            Runtime.PlayerController.isParrying = false;
                        }
                    }
                    fight.Tick(fast: !utilities);
                }
                ExpectValue("three successive timed prompts answered, attack=" + requested, 3, answered);
                if (!slowed && !utilities)
                    ExpectValue("timed cycle spends safe ultimate attacks", true, fight.Calls.Contains("ultimate"));
                if (utilities)
                    ExpectValue("timed cycle actually uses utility with a shared cooldown", true,
                        fight.Calls.Any(call => call == "heal" || call == "regen"));
            }

            foreach (var redLiquid in new[] { false, true })
            foreach (var says in new[] { false, true })
            {
                var fight = new Fight();
                fight.Character.inventory.itemList.redLiquidComplete = redLiquid;
                fight.Controller.currentEnemy.attackRate = 3;
                fight.AI.growCount = 2;
                fight.AI.enemyAttackTimer = 2.5f;
                fight.Prompt(6, says);
                fight.BuffsReady();
                foreach (var attack in fight.Attacks) attack.button.interactable = false;
                fight.Controller.healMove.OnUse = () => Runtime.PlayerController.moveTimer = redLiquid ? .8f : 1;
                fight.Tick();
                fight.AI.enemyAttackTimer += .2f;
                Runtime.PlayerController.moveTimer -= .2f;
                (says ? fight.Controller.ultimateAttackMove : fight.Controller.regularAttackMove).button.interactable = true;
                fight.Tick();
                fight.Expect($"late ready response not delayed by utility, says={says}, RedLiquid={redLiquid}",
                    says ? "ultimate" : "regular");
            }
        }

        private static void CheckSurvival()
        {
            foreach (var type in new[] { enemyType.normal, enemyType.bigBoss5 })
            foreach (var hp in new[] { 49f, 50f })
            {
                var emergency = new Fight(type);
                emergency.AI.growCount = 1;
                emergency.AI.enemyAttackTimer = emergency.Controller.currentEnemy.attackRate;
                emergency.Character.adventure.curHP = hp;
                emergency.Controller.currentEnemy.curHP = 19;
                emergency.Character.wishes.wishes[58].level = 1;
                emergency.Character.allChallenges.ParalyzeUnlocked = true;
                emergency.Character.training.defenseTraining[1] = 10000;
                emergency.Character.settings.hasHyperRegen = true;
                emergency.Controller.ohShitMove.button.interactable = emergency.Controller.healMove.button.interactable = true;
                emergency.Tick();
                emergency.Expect($"emergency remains first strictly below 50%, type={type}, HP={hp}", hp < 50 ? "oh-shit" : "heal");
            }

            foreach (var enemyHP in new[] { 100f, 19f })
            foreach (var hp in new[] { 25f, 84.99f, 85f, 85.01f, 100f })
            foreach (var healReady in new[] { false, true })
            foreach (var regenActive in new[] { false, true })
            {
                var fight = new Fight();
                fight.AI.growCount = 1;
                fight.AI.enemyAttackTimer = 3;
                fight.Controller.currentEnemy.attackRate = 3;
                fight.Controller.currentEnemy.curHP = enemyHP;
                fight.Character.adventure.curHP = hp;
                fight.Controller.healMove.button.interactable = healReady;
                fight.Controller.hyperRegenMove.button.interactable = true;
                Runtime.PlayerController.hyperRegenTime = regenActive ? 1 : -1;
                fight.Tick();
                var expected = hp <= 85 && healReady ? "heal" : hp < 85 && !regenActive ? "regen" : null;
                fight.Expect($"survival enemyHP={enemyHP}, HP={hp}, heal={healReady}, regenActive={regenActive}",
                    expected == null ? Array.Empty<string>() : new[] { expected });
            }

            var sequence = new Fight();
            sequence.AI.growCount = 1;
            sequence.AI.enemyAttackTimer = sequence.Controller.currentEnemy.attackRate;
            sequence.Character.adventure.curHP = 60;
            sequence.Controller.healMove.button.interactable = sequence.Controller.hyperRegenMove.button.interactable = true;
            sequence.Controller.healMove.OnUse = () =>
            {
                sequence.Character.adventure.curHP += 15;
                sequence.Controller.healMove.button.interactable = false;
            };
            sequence.Controller.hyperRegenMove.OnUse = () => Runtime.PlayerController.hyperRegenTime = 5;
            sequence.Tick();
            sequence.Tick();
            sequence.Tick();
            sequence.Expect("heal first, then proactive regen, never recast active regen", "heal", "regen");
        }

        private static void CheckSkillUnlocks()
        {
            foreach (var skill in new[] { "charge", "parry", "ultimate", "defense" })
            foreach (var offset in new[] { -1, 0, 1 })
            {
                var fight = new Fight();
                var threshold = skill == "charge" ? 20000 : skill == "parry" ? 15000 : skill == "ultimate" ? 25000 : 5000;
                var training = skill == "parry" ? fight.Character.training.attackTraining : fight.Character.training.defenseTraining;
                var index = skill == "charge" ? 3 : skill == "parry" ? 2 : skill == "ultimate" ? 4 : 0;
                training[index] = threshold + offset;
                var unlocked = skill == "charge" ? CombatHelpers.ChargeUnlocked() : skill == "parry" ? CombatHelpers.ParryUnlocked()
                    : skill == "ultimate" ? CombatHelpers.UltimateBuffUnlocked() : CombatHelpers.DefensiveBuffUnlocked();
                ExpectValue($"actual {skill} training unlock at threshold offset {offset}", offset >= 0, unlocked);
            }
            var wrongIndices = new Fight();
            wrongIndices.Character.training.attackTraining[3] = 50000;
            wrongIndices.Character.training.defenseTraining[1] = wrongIndices.Character.training.defenseTraining[5] = 50000;
            ExpectValue("old parry index does not unlock Parry", false, CombatHelpers.ParryUnlocked());
            ExpectValue("old ultimate index does not unlock Ultimate", false, CombatHelpers.UltimateBuffUnlocked());
            ExpectValue("old defense index does not unlock Defense", false, CombatHelpers.DefensiveBuffUnlocked());
            wrongIndices.Character.training.defenseTraining[4] = 50000;
            ExpectValue("Ultimate training does not unlock Charge", false, CombatHelpers.ChargeUnlocked());
        }

        private static void CheckWalderpDefense()
        {
            foreach (var defense in new[] { "block", "parry" })
            {
                var urgent = new Fight();
                urgent.AI.growCount = 3;
                urgent.Controller.currentEnemy.attackRate = 3.2f;
                urgent.AI.enemyAttackTimer = 3;
                urgent.Character.adventure.curHP = 40;
                urgent.Controller.healMove.button.interactable = urgent.Controller.hyperRegenMove.button.interactable = true;
                urgent.Controller.blockMove.button.interactable = defense == "block";
                urgent.Controller.parryMove.button.interactable = defense == "parry";
                urgent.Tick();
                urgent.Expect("imminent hit defense before ordinary healing: " + defense, defense);
            }

            foreach (var utility in new[] { "block", "parry" })
            foreach (var buff in new[] { "ultimate", "defense", "both" })
            foreach (var enemyHP in new[] { 100f, 19f })
            {
                var fight = new Fight();
                fight.AI.growCount = 3;
                fight.Controller.currentEnemy.attackRate = 3.2f;
                fight.AI.enemyAttackTimer = 2.5f;
                fight.Controller.currentEnemy.curHP = enemyHP;
                fight.Character.adventure.curHP = 40;
                Runtime.PlayerController.ultimateBuffTime = buff != "defense" ? 1 : -1;
                Runtime.PlayerController.defenseBuffTime = buff != "ultimate" ? 1 : -1;
                fight.Controller.blockMove.button.interactable = utility == "block";
                fight.Controller.parryMove.button.interactable = utility == "parry";
                fight.Controller.offenseBuffMove.button.interactable = true;
                fight.Tick();
                fight.Expect($"{utility} before discretionary buffs with {buff} active, enemyHP={enemyHP}", utility);
            }

            foreach (var rate in new[] { 3f, 3.2f })
            foreach (var grow in Enumerable.Range(0, 12))
            foreach (var elapsed in new[] { 0f, .3f, 1f, 2f, 2.9f })
            {
                var fight = new Fight();
                fight.Controller.currentEnemy.attackRate = rate;
                fight.AI.growCount = grow;
                fight.AI.enemyAttackTimer = elapsed;
                fight.Character.adventure.curHP = 40;
                fight.Controller.blockMove.button.interactable = true;
                foreach (var attack in fight.Attacks) attack.button.interactable = false;
                fight.Tick();
                var damaging = (grow + 1) % 6 != 2;
                var due = rate - elapsed <= 1.25f;
                fight.Expect($"block timing rate={rate}, count={grow}, elapsed={elapsed}",
                    damaging && due ? new[] { "block" } : Array.Empty<string>());
                if (fight.Calls.Contains("block"))
                    ExpectValue("Block survives until the upcoming damaging turn", true, rate - elapsed < 3);
            }

            foreach (var active in new[] { "block", "parry" })
            {
                var fight = new Fight();
                fight.AI.growCount = 3;
                fight.Controller.currentEnemy.attackRate = 3;
                fight.AI.enemyAttackTimer = 2.5f;
                fight.Controller.blockMove.button.interactable = fight.Controller.parryMove.button.interactable = true;
                Runtime.PlayerController.isBlocking = active == "block";
                Runtime.PlayerController.isParrying = active == "parry";
                foreach (var attack in fight.Attacks) attack.button.interactable = false;
                fight.Tick();
                fight.Expect("no redundant defense with active " + active);
            }

            foreach (var skill in new[] { "ultimate-buff", "offense-buff", "defense-buff" })
            {
                var fight = new Fight();
                fight.AI.growCount = 1;
                fight.AI.enemyAttackTimer = fight.Controller.currentEnemy.attackRate;
                fight.Character.adventure.curHP = 60;
                Runtime.PlayerController.ultimateBuffTime = skill == "defense-buff" ? 1 : -1;
                Runtime.PlayerController.defenseBuffTime = skill == "ultimate-buff" ? 1 : -1;
                Runtime.PlayerController.isBlocking = skill == "defense-buff";
                fight.Controller.ultimateBuffMove.button.interactable = skill == "ultimate-buff";
                fight.Controller.offenseBuffMove.button.interactable = skill == "offense-buff";
                fight.Controller.defenseBuffMove.button.interactable = skill == "defense-buff";
                fight.Tick();
                fight.Expect("buff priority with active ultimate: " + skill,
                    skill == "defense-buff" ? Array.Empty<string>() : new[] { skill });
            }

            var stacked = new Fight();
            stacked.AI.growCount = 1;
            stacked.AI.enemyAttackTimer = stacked.Controller.currentEnemy.attackRate;
            stacked.Character.adventure.curHP = 60;
            foreach (var move in new[] { stacked.Controller.ultimateBuffMove, stacked.Controller.offenseBuffMove, stacked.Controller.defenseBuffMove })
            {
                move.button.interactable = true;
                move.OnUse = () => move.button.interactable = false;
            }
            stacked.Tick();
            Runtime.PlayerController.ultimateBuffTime = 1;
            stacked.Tick();
            stacked.Tick();
            stacked.Expect("Ultimate prevents spending a turn on Defensive Buff", "ultimate-buff", "offense-buff");
            Runtime.PlayerController.ultimateBuffTime = stacked.Character.ultimateBuffDuration();
            stacked.Tick();
            Runtime.PlayerController.defenseBuffTime = 1;
            stacked.Tick();
            stacked.Expect("Defensive Buff becomes available after Ultimate expires; no active recast",
                "ultimate-buff", "offense-buff", "defense-buff");

            foreach (var megaReady in new[] { false, true })
            {
                var mega = new Fight();
                mega.AI.growCount = 1;
                mega.AI.enemyAttackTimer = mega.Controller.currentEnemy.attackRate;
                mega.Character.training.defenseTraining[4] = 25000;
                mega.Character.wishes.wishes[8].level = 1;
                mega.Controller.megaBuffMove.button.interactable = megaReady;
                mega.Controller.ultimateBuffMove.button.interactable = mega.Controller.offenseBuffMove.button.interactable =
                    mega.Controller.defenseBuffMove.button.interactable = true;
                mega.Tick();
                mega.Expect("Mega retains its own branch, ready=" + megaReady, megaReady ? new[] { "mega-buff" } : Array.Empty<string>());
            }
        }

        private static void CheckNormalCombat()
        {
            var fight = new Fight(enemyType.normal);
            fight.Controller.ultimateAttackMove.button.interactable = false;
            fight.Tick(fast: true);
            fight.Expect("normal fast decision selects available pierce", "pierce");

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
            fight.Expect("normal combat denies enemy turns before ordinary healing", "paralyze");

            fight = new Fight(enemyType.normal);
            fight.BuffsReady();
            fight.Controller.ultimateAttackMove.button.interactable = false;
            fight.Tick(fast: true);
            fight.Expect("normal fast combat still bypasses buffs", "pierce");

        }
    }
}
