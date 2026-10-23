using System;
using Runtime = NGUInjector.Main;

namespace NGUInjector
{
    internal static partial class CombatChecks
    {
        private static void CheckNonFastPriorities()
        {
            foreach (var ai in new[] { AI.normal, AI.charger, AI.rapid, AI.exploder })
            foreach (var active in new[] { "none", "block", "parry" })
            foreach (var enemyHP in new[] { 100f, 19f })
            {
                var fight = new Fight(enemyType.normal);
                fight.Controller.currentEnemy.AI = ai;
                fight.Controller.currentEnemy.curHP = enemyHP;
                fight.AI.chargeCooldown = 2;
                fight.AI.rapidEffect = 5;
                Runtime.PlayerController.isBlocking = active == "block";
                Runtime.PlayerController.isParrying = active == "parry";
                foreach (var move in new[] { fight.Controller.paralyzeMove, fight.Controller.megaBuffMove,
                    fight.Controller.ultimateBuffMove, fight.Controller.offenseBuffMove, fight.Controller.defenseBuffMove,
                    fight.Controller.blockMove, fight.Controller.parryMove, fight.Controller.chargeMove })
                    move.button.interactable = true;
                fight.Tick();
                fight.Expect($"Gaze before buffs/attacks, AI={ai}, active={active}, enemyHP={enemyHP}", "paralyze");
            }

            var repeated = new Fight(enemyType.normal);
            repeated.Controller.ultimateAttackMove.button.interactable = false;
            repeated.Controller.paralyzeMove.button.interactable = true;
            repeated.Controller.paralyzeMove.OnUse = () => repeated.Controller.paralyzeMove.button.interactable = false;
            repeated.Tick();
            repeated.Tick();
            repeated.Controller.paralyzeMove.button.interactable = true;
            Runtime.PlayerController.moveTimer = 1;
            repeated.Tick();
            Runtime.PlayerController.moveTimer = 0;
            repeated.Tick();
            repeated.Expect("Gaze every time ready, attack during its cooldown, respect shared cooldown", "paralyze", "pierce", "paralyze");

            foreach (var moreDefense in new[] { false, true })
            foreach (var ai in new[] { AI.normal, AI.charger, AI.rapid, AI.exploder })
            foreach (var hp in new[] { 25f, 50f, 50.01f, 100f })
            foreach (var due in new[] { false, true })
            {
                var fight = new Fight(enemyType.normal);
                Runtime.Settings.MoreBlockParry = moreDefense;
                fight.Controller.currentEnemy.AI = ai;
                fight.Character.adventure.curHP = hp;
                fight.AI.chargeCooldown = 4;
                fight.AI.rapidEffect = 7;
                fight.AI.enemyAttackTimer = due ? 9.5f : 0;
                fight.Controller.ultimateAttackMove.button.interactable = false;
                fight.Controller.blockMove.button.interactable = fight.Controller.parryMove.button.interactable = true;
                fight.Tick();
                var expected = !due ? "pierce" : hp <= 50 || ai == AI.rapid ? "block"
                    : ai == AI.charger || ai == AI.exploder ? "parry" : "pierce";
                fight.Expect($"threat-aware defense, more={moreDefense}, AI={ai}, HP={hp}, due={due}", expected);
            }

            foreach (var rate in new[] { .5f, 1f, 1.01f, 3f })
            {
                var fight = new Fight(enemyType.normal);
                fight.Controller.currentEnemy.attackRate = rate;
                fight.AI.enemyAttackTimer = rate - .25f;
                fight.Controller.blockMove.button.interactable = fight.Controller.parryMove.button.interactable = true;
                fight.Controller.ultimateAttackMove.button.interactable = false;
                fight.Tick();
                fight.Expect("Block healthy only when its duration covers several attacks, rate=" + rate,
                    rate <= 1 ? "block" : "pierce");
            }

            foreach (var ai in new[] { AI.charger, AI.rapid, AI.exploder })
            foreach (var active in new[] { "block", "parry" })
            {
                var fight = new Fight(enemyType.normal);
                fight.Controller.currentEnemy.AI = ai;
                fight.AI.chargeCooldown = 4;
                fight.AI.rapidEffect = 7;
                fight.AI.enemyAttackTimer = 9.5f;
                fight.Character.adventure.curHP = 25;
                Runtime.PlayerController.isBlocking = active == "block";
                Runtime.PlayerController.isParrying = active == "parry";
                fight.Controller.blockMove.button.interactable = fight.Controller.parryMove.button.interactable = true;
                fight.Controller.ultimateAttackMove.button.interactable = false;
                fight.Tick();
                fight.Expect($"no redundant defense against {ai} with {active} active", "pierce");
            }

            foreach (var hp in new[] { 50f, 100f })
            foreach (var phase in new[] { 0, 1, 2, 3, 4 })
            {
                var fight = new Fight(enemyType.normal);
                fight.Controller.currentEnemy.AI = AI.charger;
                fight.Character.adventure.curHP = hp;
                fight.AI.chargeCooldown = phase;
                fight.AI.enemyAttackTimer = 9.5f;
                fight.Controller.blockMove.button.interactable = fight.Controller.parryMove.button.interactable = true;
                fight.Controller.ultimateAttackMove.button.interactable = false;
                fight.Tick();
                var expected = phase == 2 || phase == 3 ? "pierce" : hp <= 50 ? "block" : phase == 4 ? "parry" : "pierce";
                fight.Expect($"Charger warning/blank turns do not consume defenses, phase={phase}, HP={hp}", expected);
            }

            foreach (var hp in new[] { 50f, 100f })
            foreach (var phase in new[] { 0, 3, 4, 5, 6, 7, 8, 12, 13 })
            {
                var fight = new Fight(enemyType.normal);
                fight.Controller.currentEnemy.AI = AI.rapid;
                fight.Controller.currentEnemy.attackRate = 3;
                fight.Character.adventure.curHP = hp;
                fight.AI.rapidEffect = phase;
                fight.AI.rapidMode = phase >= 8;
                fight.AI.enemyAttackTimer = (fight.AI.rapidMode ? .9f : 3f) - .25f;
                fight.Controller.blockMove.button.interactable = fight.Controller.parryMove.button.interactable = true;
                fight.Controller.ultimateAttackMove.button.interactable = false;
                fight.Tick();
                var expected = phase >= 4 && phase <= 6 ? "pierce" : hp <= 50 || phase >= 7 && phase < 13 ? "block" : "pierce";
                fight.Expect($"Rapid warning/blank turns and accelerated burst, phase={phase}, HP={hp}", expected);
            }

            foreach (var type in new[] { enemyType.bigBoss5, enemyType.bigBoss6V1 })
            {
                var fight = new Fight(type);
                fight.Character.adventure.curHP = 50;
                fight.Controller.currentEnemy.attackRate = 3;
                fight.AI.firstStrike = true;
                fight.AI.growCount = 3;
                fight.AI.enemyAttackTimer = 2.5f;
                fight.Controller.blockMove.button.interactable = true;
                fight.Tick();
                fight.Expect("special bosses do not use ordinary first-strike delay: " + type, "block");
            }

            foreach (var firstStrike in new[] { false, true })
            foreach (var elapsed in new[] { 9.5f, 14.5f })
            {
                var fight = new Fight(enemyType.normal);
                fight.Controller.currentEnemy.AI = AI.exploder;
                fight.Character.adventure.curHP = 50;
                fight.AI.firstStrike = firstStrike;
                fight.AI.enemyAttackTimer = elapsed;
                fight.Controller.blockMove.button.interactable = true;
                fight.Controller.ultimateAttackMove.button.interactable = false;
                fight.Tick();
                fight.Expect($"first enemy turn is delayed, initial={firstStrike}, elapsed={elapsed}",
                    firstStrike && elapsed < 10 ? "pierce" : "block");
            }

            var frozen = new Fight(enemyType.normal);
            frozen.Character.adventure.curHP = 25;
            frozen.AI.enemyAttackTimer = 9.5f;
            frozen.Controller.ultimateAttackMove.button.interactable = false;
            frozen.Controller.blockMove.button.interactable = frozen.Controller.paralyzeMove.button.interactable =
                frozen.Controller.healMove.button.interactable = true;
            frozen.Controller.paralyzeMove.OnUse = () =>
            {
                frozen.AI.paralyzeTime = 3;
                frozen.Controller.paralyzeMove.button.interactable = false;
            };
            frozen.Controller.healMove.OnUse = () => frozen.Controller.healMove.button.interactable = false;
            frozen.Tick();
            frozen.Tick();
            frozen.Tick();
            frozen.AI.paralyzeTime = .25f;
            frozen.Tick();
            frozen.Expect("Gaze before Block; recover/attack while frozen, Block only when damage is due", "paralyze", "heal", "pierce", "block");

            var emergency = new Fight(enemyType.normal);
            emergency.Character.adventure.curHP = 25;
            emergency.Character.wishes.wishes[58].level = 1;
            emergency.Character.allChallenges.ParalyzeUnlocked = true;
            emergency.Character.training.defenseTraining[1] = 10000;
            emergency.Character.settings.hasHyperRegen = true;
            emergency.Controller.ohShitMove.button.interactable = emergency.Controller.paralyzeMove.button.interactable = true;
            emergency.Tick();
            emergency.Expect("emergency recovery retains priority over Gaze", "oh-shit");

            foreach (var hp in new[] { 50f, 50.01f, 100f })
            {
                var fight = new Fight();
                fight.Character.adventure.curHP = hp;
                fight.Controller.currentEnemy.attackRate = 3;
                fight.AI.growCount = 3;
                fight.AI.enemyAttackTimer = 2.5f;
                fight.Controller.blockMove.button.interactable = fight.Controller.parryMove.button.interactable = true;
                fight.Tick();
                fight.Expect("healthy Walderp attacks instead of routinely blocking/parrying, HP=" + hp, hp <= 50 ? "block" : "pierce");
            }

            foreach (var type in new[] { enemyType.normal, enemyType.bigBoss5 })
            foreach (var hp in new[] { 60f, 100f })
            foreach (var ultimate in new[] { false, true })
            foreach (var defense in new[] { false, true })
            {
                var fight = new Fight(type);
                fight.AI.growCount = 1;
                fight.AI.enemyAttackTimer = fight.Controller.currentEnemy.attackRate;
                fight.Character.adventure.curHP = hp;
                Runtime.PlayerController.ultimateBuffTime = ultimate ? 1 : 0;
                Runtime.PlayerController.defenseBuffTime = defense ? 1 : 0;
                fight.Controller.defenseBuffMove.button.interactable = true;
                fight.Controller.ultimateAttackMove.button.interactable = false;
                fight.Tick();
                var expected = !ultimate && !defense ? "defense-buff" : type == enemyType.normal ? "pierce" : null;
                fight.Expect($"Defensive Buff priority, type={type}, HP={hp}, ultimate={ultimate}, defense={defense}",
                    expected == null ? Array.Empty<string>() : new[] { expected });
            }

            var fast = new Fight(enemyType.normal);
            fast.BuffsReady();
            fast.AI.enemyAttackTimer = 9.5f;
            fast.Controller.ultimateAttackMove.button.interactable = false;
            fast.Tick(fast: true);
            fast.Expect("Fast Combat ignores low-HP defenses and Gaze", "pierce");

            foreach (var says in new[] { false, true })
            {
                var prompt = new Fight();
                prompt.Prompt(6, says);
                prompt.Controller.paralyzeMove.button.interactable = true;
                prompt.Tick();
                prompt.Expect("Walderp prompt attack before ready Gaze, says=" + says, says ? "ultimate" : "regular");
            }
        }
    }
}
