using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using static NGUInjector.Main;
using static NGUInjector.Managers.CombatHelpers;

namespace NGUInjector.Managers
{
    internal class CombatManager
    {
        private readonly Character _character;
        private readonly PlayerController _pc;
        private bool _isFighting = false;
        private float _fightTimer = 0;
        private string _enemyName;
        private int _lastBlockedZone = int.MinValue;
        internal Action GoldFightCompleted;
        private ExecutionGrant _routeGrant;
        private Action _observeRoute;
        private bool _callbackRejected;

        private bool InvokeNative(Action invoke, Func<bool> effect, bool changesRoute = false, bool changesIdle = false, bool changesBeast = false)
        {
            var grant = _routeGrant;
            var coordinator = Main.reference;
            if (_callbackRejected || coordinator == null || !coordinator.VerifyExecutionPrerequisites(grant, ExecutionDomain.Route | ExecutionDomain.NativeSettings, () => true)) return false;
            var zone = _character.adventure.zone; var idle = _character.adventure.autoattacking;
            var beast = _character.adventure.beastModeOn;
            var enemy = _character.adventureController.currentEnemy;
            var rebirth = _character.stats.rebirthNumber;
            var allocations = ResourceWriter.CaptureAllocations(_character);
            var resources = ResourceWriter.CaptureResourceContext(_character);
            invoke();
            if (!ReferenceEquals(Main.reference, coordinator) || !ReferenceEquals(Main.Character, _character)
                || _character.stats.rebirthNumber != rebirth || !coordinator.RouteResourcesStillOwned(allocations, resources))
            { _callbackRejected = true; return false; }
            var nativeDeath = !changesRoute && zone != -1 && _character.adventure.zone == -1 && _character.adventure.curHP <= 0;
            if ((!changesRoute && !nativeDeath && _character.adventure.zone != zone)
                || (!changesIdle && _character.adventure.autoattacking != idle)
                || (!changesBeast && _character.adventure.beastModeOn != beast)
                || !changesRoute && enemy != null && _character.adventureController.currentEnemy != null
                    && !ReferenceEquals(enemy, _character.adventureController.currentEnemy) || !effect())
            { _callbackRejected = true; return false; }
            _observeRoute?.Invoke();
            return coordinator.ValidateExecutionGrant(grant, ExecutionDomain.Route | ExecutionDomain.NativeSettings);
        }

        private bool InvokeMove(Action invoke) => InvokeNative(invoke, () => true);
        private bool InvokeCast(Func<bool> invoke) { var cast = false; return InvokeNative(() => cast = invoke(), () => cast); }
        private bool CastCharge() => InvokeCast(() => CombatHelpers.CastCharge(_routeGrant));
        private bool CastParry() => InvokeCast(() => CombatHelpers.CastParry(_routeGrant));
        private bool CastHeal() => InvokeCast(() => CombatHelpers.CastHeal(_routeGrant));
        private bool CastHyperRegen() => InvokeCast(() => CombatHelpers.CastHyperRegen(_routeGrant));
        private bool CastUltimateBuff() => InvokeCast(() => CombatHelpers.CastUltimateBuff(_routeGrant));
        private bool CastMegaBuff() => InvokeCast(() => CombatHelpers.CastMegaBuff(_routeGrant));
        private bool CastOffensiveBuff() => InvokeCast(() => CombatHelpers.CastOffensiveBuff(_routeGrant));
        private bool CastBlock() => InvokeCast(() => CombatHelpers.CastBlock(_routeGrant));
        private bool CastDefensiveBuff() => InvokeCast(() => CombatHelpers.CastDefensiveBuff(_routeGrant));
        private bool CastParalyze() => InvokeCast(() => CombatHelpers.CastParalyze(_routeGrant));
        private bool CastOhShit() => InvokeCast(() => CombatHelpers.CastOhShit(_routeGrant));

        private bool ToggleIdle() { var expected = !_character.adventure.autoattacking;
            return InvokeNative(() => _character.adventureController.idleAttackMove.setToggle(),
                () => _character.adventure.autoattacking == expected, changesIdle: true); }

        private bool ToggleBeast() { var expected = !_character.adventure.beastModeOn;
            return InvokeNative(() => _character.adventureController.beastModeMove.doMove(),
                () => _character.adventure.beastModeOn == expected, changesBeast: true); }

        internal bool SetIdleAttack(bool enabled, ExecutionGrant grant, Action observe = null)
        {
            _routeGrant = grant; _observeRoute = observe ?? Main.reference.ObserveOrdinaryRoute; _callbackRejected = false;
            if (!Main.reference.ValidateExecutionGrant(grant, ExecutionDomain.Route | ExecutionDomain.NativeSettings)) return false;
            return _character.adventure.autoattacking == enabled || ToggleIdle();
        }

        public CombatManager()
        {
            _character = Main.Character;
            _pc = Main.PlayerController;
        }

        internal void UpdateFightTimer(float diff)
        {
            _fightTimer += diff;
        }

        bool HasFullHP()
        {
            return Math.Abs(_character.totalAdvHP() - _character.adventure.curHP) < 5;
        }

        float GetHPPercentage()
        {
            return _character.adventure.curHP / _character.totalAdvHP();
        }

        private void DoCombat(bool fastCombat)
        {
            if (!_pc.moveCheck())
                return;

            if (Main.PlayerController.moveTimer > 0)
                return;

            if (TryHandleWalderpSays())
                return;

            if (IsWalderpFight())
            {
                if (!TryGetEnemyAIValue(_character.adventureController.enemyAI, "inWaldoSaysLoop", out bool inWaldoSaysLoop)
                    || inWaldoSaysLoop)
                    return;

                // Utility need not reserve attack cooldowns before prompts, but must not delay an active response.
                if (!fastCombat && CombatBuffs())
                    return;

                // Recover before the prompt, leaving room for button refresh and automation ticks.
                var budget = WalderpSecondsUntilPrompt() - .25f;
                var globalCooldown = _character.inventory.itemList.redLiquidComplete ? .8f : 1f;
                var cooldownSpeed = _pc.timeDilation(1f);
                if (!(budget >= globalCooldown) || !(cooldownSpeed > 0) || float.IsInfinity(cooldownSpeed))
                    return;

                for (var attackID = 6; attackID >= 3; attackID--)
                {
                    float cooldown;
                    switch (attackID)
                    {
                        case 6: cooldown = _character.ultimateAttackCooldown(); break;
                        case 5: cooldown = _character.pierceAttackCooldown(); break;
                        case 4: cooldown = _character.strongAttackCooldown(); break;
                        default: cooldown = _character.regAttackCooldown(); break;
                    }

                    if (cooldown > 0 && cooldown / cooldownSpeed <= budget && TryWalderpAttack(attackID))
                        return;
                }
                return;
            }

            if (!fastCombat)
            {
                if (CombatBuffs())
                    return;
            }

            CombatAttacks(fastCombat);
        }

        private bool CombatBuffs()
        {
            var ac = _character.adventureController;
            var ai = ac.currentEnemy.AI;
            var eai = ac.enemyAI;
            var walderp = IsWalderpFight();
            var lowHealth = GetHPPercentage() <= .5f;

            if (OhShitUnlocked() && GetHPPercentage() < .5 && OhShitReady() && CastOhShit())
                return true;

            // Deny enemy turns before spending a short-lived defense, even with Block/Parry active.
            if (CastParalyze())
                return true;

            // Block lasts three seconds. Reserve it for several hits or an imminent low-HP hit.
            var damagingTurn = !walderp || TryGetEnemyAIValue(eai, "growCount", out int count) && count >= 0 && count % 6 != 1;
            var attackInterval = ac.currentEnemy.attackRate;
            var chargeCount = ai == AI.charger ? eai.GetPV<int>("chargeCooldown") : 0;
            var rapidCount = ai == AI.rapid ? eai.GetPV<int>("rapidEffect") : 0;
            // Special bosses bypass the ordinary first-strike and AI-counter routines.
            var ordinaryEnemy = ac.currentEnemy.enemyType == enemyType.normal || ac.currentEnemy.enemyType == enemyType.boss;
            if (ordinaryEnemy)
            {
                if (TryGetEnemyAIValue(eai, "firstStrike", out bool firstStrike) && firstStrike)
                    attackInterval *= 1.5f;
                else if (ai == AI.rapid && TryGetEnemyAIValue(eai, "rapidMode", out bool rapidMode) && rapidMode)
                    attackInterval *= .3f;

                if (ai == AI.charger)
                    damagingTurn = chargeCount < 2 || chargeCount >= 4;
                else if (ai == AI.rapid)
                    damagingTurn = rapidCount < 4 || rapidCount >= 7;
            }
            var paralysisRemaining = TryGetEnemyAIValue(eai, "paralyzeTime", out float paralyzeTime) ? Math.Max(0, paralyzeTime) : 0;
            var hitDue = damagingTurn
                && TryGetEnemyAIValue(eai, "enemyAttackTimer", out float elapsed)
                && attackInterval > 0 && !float.IsInfinity(attackInterval)
                && elapsed >= 0 && !float.IsInfinity(elapsed)
                && paralysisRemaining + attackInterval - elapsed <= (_character.inventory.itemList.redLiquidComplete ? .8f : 1f) + .25f;
            var manyAttacks = ac.currentEnemy.attackRate > 0 && ac.currentEnemy.attackRate <= 1f
                || ordinaryEnemy && ai == AI.rapid && rapidCount >= 7 && rapidCount < 13;
            var strongAttack = ai == AI.charger && chargeCount >= 4 || ai == AI.exploder;

            if (hitDue && !BlockActive() && !ParryActive())
            {
                if ((manyAttacks || lowHealth) && CastBlock())
                    return true;

                if ((strongAttack || lowHealth) && CastParry())
                    return true;
            }

            if (GetHPPercentage() <= .85f)
            {
                if (CastHeal())
                {
                    return true;
                }
            }

            if (GetHPPercentage() < .85f)
            {
                if (CastHyperRegen())
                {
                    return true;
                }
            }

            if (!walderp && ac.currentEnemy.curHP / ac.currentEnemy.maxHP < .2)
                return false;

            if (CastMegaBuff())
            {
                return true;
            }

            if (!MegaBuffUnlocked())
            {
                if (CastUltimateBuff())
                    return true;

                if (CastOffensiveBuff())
                    return true;

                if (!UltimateBuffActive() && !DefenseBuffActive())
                {
                    if (CastDefensiveBuff())
                        return true;
                }
            }

            if (_pc.isBlocking || _pc.isParrying)
            {
                return false;
            }

            if (ChargeReady())
            {
                if (UltimateAttackReady())
                {
                    if (CastCharge())
                        return true;
                }

                if (GetUltimateAttackCooldown() > .45 && PierceReady())
                {
                    if (CastCharge())
                        return true;
                }
            }

            return false;
        }

        private void CombatAttacks(bool fastCombat)
        {
            var ac = _character.adventureController;

            if (ac.ultimateAttackMove.button.IsInteractable())
            {
                if (fastCombat)
                {
                    if (!InvokeMove(() => ac.ultimateAttackMove.doMove())) return;
                }

                if (ChargeActive())
                {
                    if (!InvokeMove(() => ac.ultimateAttackMove.doMove())) return;
                }

                if (GetChargeCooldown() > .45)
                {
                    if (!InvokeMove(() => ac.ultimateAttackMove.doMove())) return;
                }
            }

            if (ac.pierceMove.button.IsInteractable())
            {
                InvokeMove(() => ac.pierceMove.doMove());
                return;
            }

            if (ac.strongAttackMove.button.IsInteractable())
            {
                InvokeMove(() => ac.strongAttackMove.doMove());
                return;
            }

            if (ac.regularAttackMove.button.IsInteractable())
            {
                InvokeMove(() => ac.regularAttackMove.doMove());
                return;
            }
        }

        private bool TryHandleWalderpSays()
        {
            if (!IsWalderpFight())
                return false;

            var eai = _character.adventureController.enemyAI;
            if (!TryGetEnemyAIValue(eai, "inWaldoSaysLoop", out bool inWaldoSaysLoop))
                return false;

            if (!inWaldoSaysLoop)
                return false;

            if (!TryGetEnemyAIValue(eai, "waldoAttackID", out int waldoAttackID) || waldoAttackID < 3 || waldoAttackID > 6)
                return false;

            if (!TryGetEnemyAIValue(eai, "waldoSays", out bool waldoSays))
                return false;

            if (waldoSays)
                return TryWalderpAttack(waldoAttackID);

            return TryWalderpAlternateAttack(waldoAttackID);
        }

        private bool TryWalderpAlternateAttack(int forbiddenAttackID)
        {
            for (var attackID = 3; attackID <= 6; attackID++)
            {
                if (attackID != forbiddenAttackID && TryWalderpAttack(attackID))
                    return true;
            }

            return false;
        }

        private bool TryWalderpAttack(int attackID)
        {
            var ac = _character.adventureController;

            // EnemyAI uses IDs 3-6 for regular, strong, piercing, and ultimate attacks.
            switch (attackID)
            {
                case 3:
                    if (!ac.regularAttackMove.button.IsInteractable())
                        return false;

                    return InvokeMove(() => ac.regularAttackMove.doMove());
                case 4:
                    if (!ac.strongAttackMove.button.IsInteractable())
                        return false;

                    return InvokeMove(() => ac.strongAttackMove.doMove());
                case 5:
                    if (!ac.pierceMove.button.IsInteractable())
                        return false;

                    return InvokeMove(() => ac.pierceMove.doMove());
                case 6:
                    if (!ac.ultimateAttackMove.button.IsInteractable())
                        return false;

                    return InvokeMove(() => ac.ultimateAttackMove.doMove());
                default:
                    return false;
            }
        }

        private bool IsWalderpFight()
        {
            var enemy = _character.adventureController.currentEnemy;
            if (enemy == null)
                return false;

            return enemy.enemyType == enemyType.waldo1
                   || enemy.enemyType == enemyType.waldo2
                   || enemy.enemyType == enemyType.waldo3
                   || enemy.enemyType == enemyType.waldo4
                   || enemy.enemyType == enemyType.bigBoss5;
        }

        private float WalderpSecondsUntilPrompt()
        {
            var ac = _character.adventureController;
            if (!TryGetEnemyAIValue(ac.enemyAI, "growCount", out int growCount) || growCount < 0
                || !TryGetEnemyAIValue(ac.enemyAI, "enemyAttackTimer", out float elapsed))
                return 0;

            var attackRate = ac.currentEnemy.attackRate;
            if (!(attackRate > 0) || float.IsInfinity(attackRate)
                || float.IsNaN(elapsed) || float.IsInfinity(elapsed))
                return 0;

            // growCount advances on enemy turns; prompts occur at 2, 8, 14, ... .
            var turns = (8 - growCount % 6) % 6;
            if (turns == 0)
                turns = 6;

            var seconds = turns * attackRate - Math.Max(0, Math.Min(elapsed, attackRate));
            return float.IsInfinity(seconds) ? 0 : seconds;
        }

        private static bool TryGetEnemyAIValue<T>(EnemyAI eai, string fieldName, out T value)
        {
            value = default(T);
            if (eai == null)
                return false;

            try
            {
                var field = eai.GetType().GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var raw = field?.GetValue(eai);
                if (raw is T typed)
                {
                    value = typed;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        internal static bool IsZoneUnlocked(int zone)
        {
            return ZoneHelpers.IsZoneUnlocked(zone);
        }

        internal bool MoveToZone(int zone, ExecutionGrant grant = null, Action observe = null)
        {
            _routeGrant = grant ?? Main.reference.ObserveRouteGrant(zone);
            _observeRoute = observe ?? Main.reference.ObserveOrdinaryRoute;
            _callbackRejected = false;
            return MoveWithinRoute(zone);
        }

        private bool MoveWithinRoute(int zone)
        {
            if (!CanRouteToZone(zone))
                return false;

            return InvokeNative(() => _character.adventureController.zoneSelector.changeZone(zone),
                () => _character.adventure.zone == zone, changesRoute: true);
        }

        private bool CanRouteToZone(int zone)
        {
            if (ZoneHelpers.IsZoneUnlocked(zone))
            {
                _lastBlockedZone = int.MinValue;
                return true;
            }

            if (_lastBlockedZone != zone)
            {
                _lastBlockedZone = zone;
                var required = ZoneHelpers.ZoneUnlockEffectiveBossId(zone);
                Log($"Adventure route blocked: {ZoneName(zone)} ({zone}) requires effective boss {required}, current effective boss {ZoneHelpers.CurrentRebirthEffectiveBossId()}.");
            }

            return false;
        }

        private string ZoneName(int zone)
        {
            try
            {
                return _character.adventureController.zoneName(zone);
            }
            catch
            {
                return zone == -1 ? "Safe Zone" : $"zone {zone}";
            }
        }

        internal void IdleZone(int zone, bool bossOnly, bool recoverHealth, ExecutionGrant grant = null, Action observe = null)
        {
            _routeGrant = grant ?? Main.reference.ObserveRouteGrant(zone); _observeRoute = observe ?? Main.reference.ObserveOrdinaryRoute; _callbackRejected = false;
            if (!Main.reference.ValidateExecutionGrant(_routeGrant, ExecutionDomain.Route | ExecutionDomain.NativeSettings)) return;
            if (!CanRouteToZone(zone))
                return;

            if (zone == -1)
            {
                if (_character.adventure.zone != -1)
                {
                    MoveWithinRoute(-1);
                    return;
                }
            }
            //Enable idle attack if its not on
            if (!_character.adventure.autoattacking && !IsWalderpFight())
            {
                if (!ToggleIdle()) return;
                return;
            }

            //Turn on beast mode depending
            if (_character.adventure.beastModeOn && !Settings.BeastMode && _character.adventureController.beastModeMove.button.interactable)
            {
                if (!ToggleBeast()) return;
                return;
            }

            //Turn off beast mode depending
            if (!_character.adventure.beastModeOn && Settings.BeastMode &&
                _character.adventureController.beastModeMove.button.interactable)
            {
                if (!ToggleBeast()) return;
                return;
            }

            if (_character.adventure.zone == -1 && !HasFullHP() && recoverHealth)
                return;

            //Check if we're in not in the right zone and not in safe zone, if not move to safe zone first
            if (_character.adventure.zone != zone && _character.adventure.zone != -1)
            {
                MoveWithinRoute(-1);
            }

            //Move to the zone
            if (_character.adventure.zone != zone)
            {
                MoveWithinRoute(zone);
                return;
            }

            //Wait for an enemy to spawn
            if (_character.adventureController.currentEnemy == null)
                return;

            if (zone < 1000 && Settings.BlacklistedBosses.Contains(_character.adventureController.currentEnemy.spriteID))
            {
                MoveWithinRoute(-1);
                MoveWithinRoute(zone);
                return;
            }

            //If we only want boss enemies
            if (bossOnly && !IsWalderpFight())
            {
                //Check the type of the enemy
                var ec = _character.adventureController.currentEnemy.enemyType;
                //If its not a boss, move back to safe zone. Next loop will put us back in the right zone.
                if (ec != enemyType.boss && !ec.ToString().Contains("bigBoss"))
                {
                    MoveWithinRoute(-1);
                }
            }

            if (IsWalderpFight())
            {
                if (_character.adventure.autoattacking)
                {
                    if (!ToggleIdle()) return;
                    return;
                }

                DoCombat(false);
                return;
            }
        }

        internal void ManualZone(int zone, bool bossOnly, bool recoverHealth, bool precastBuffs, bool fastCombat, bool beastMode, bool preserveIdleMode = false,
            ExecutionGrant grant = null, Action observe = null)
        {
            _routeGrant = grant ?? Main.reference.ObserveRouteGrant(zone); _observeRoute = observe ?? Main.reference.ObserveOrdinaryRoute; _callbackRejected = false;
            if (!Main.reference.ValidateExecutionGrant(_routeGrant, ExecutionDomain.Route | ExecutionDomain.NativeSettings)) return;
            if (!CanRouteToZone(zone))
                return;

            if (zone == -1)
            {
                if (_character.adventure.zone != -1)
                {
                    MoveWithinRoute(-1);
                    return;
                }
            }

            // Legacy manual combat turns idle off. Custom gold-push keeps idle on.
            if (preserveIdleMode)
            {
                if (!_character.adventure.autoattacking && !IsWalderpFight())
                {
                    if (!ToggleIdle()) return;
                    return;
                }
            }
            else if (!_character.adventure.autoattacking)
            {
                if (_character.training.attackTraining[1] == 0 && !IsWalderpFight())
                {
                    if (!ToggleIdle()) return;
                    return;
                }
            }
            else
            {
                if (_character.training.attackTraining[1] > 0)
                {
                    if (!ToggleIdle()) return;
                }
            }

            if (_character.adventure.beastModeOn && !beastMode && _character.adventureController.beastModeMove.button.interactable)
            {
                if (!ToggleBeast()) return;
                return;
            }

            if (!_character.adventure.beastModeOn && beastMode &&
                _character.adventureController.beastModeMove.button.interactable)
            {
                if (!ToggleBeast()) return;
                return;
            }

            //Move back to safe zone if we're in the wrong zone
            if (_character.adventure.zone != zone && _character.adventure.zone != -1)
            {
                MoveWithinRoute(-1);
            }

            //If precast buffs is true and we have no enemy and charge isn't active, go back to safe zone
            if (precastBuffs && !ChargeActive() && _character.adventureController.currentEnemy == null)
            {
                MoveWithinRoute(-1);
            }

            //If we're in safe zone, recover health if needed. Also precast buffs
            if (_character.adventure.zone == -1)
            {
                if (precastBuffs)
                {
                    if (ChargeUnlocked() && !ChargeActive())
                    {
                        if (CastCharge()) return;
                    }

                    if (ParryUnlocked() && !ParryActive())
                    {
                        if (CastParry()) return;
                    }

                    //Wait for Charge to be ready again, as well as other buffs
                    if (ChargeUnlocked() && !ChargeReady()) return;
                    if (ParryUnlocked() && !ParryReady()) return;
                    if (MegaBuffUnlocked() && !MegaBuffReady()) return;
                    if (UltimateBuffUnlocked() && !UltimateBuffReady()) return;
                    if (DefensiveBuffUnlocked() && !DefensiveBuffReady()) return;
                }

                if (recoverHealth && !HasFullHP())
                {
                    if (ChargeUnlocked() && !ChargeActive())
                    {
                        if (CastCharge()) return;
                    }

                    if (ParryUnlocked() && !ParryActive())
                    {
                        if (CastParry()) return;
                    }
                    return;
                }
            }
            
            //Move to the zone
            if (_character.adventure.zone != zone)
            {
                _isFighting = false;
                MoveWithinRoute(zone);
                return;
            }

            //Wait for an enemy to spawn
            if (_character.adventureController.currentEnemy == null)
            {
                if (_isFighting)
                {
                    _isFighting = false;
                    if (_fightTimer > 1)
                        LogCombat($"{_enemyName} killed in {_fightTimer:00.0}s");

                    _fightTimer = 0;
                    if (preserveIdleMode && GoldFightCompleted != null)
                    {
                        if (Main.reference.ValidateExecutionGrant(_routeGrant, ExecutionDomain.Route | ExecutionDomain.NativeSettings))
                            GoldFightCompleted();
                        Main.reference.ValidateExecutionGrant(_routeGrant, ExecutionDomain.Route | ExecutionDomain.NativeSettings);
                        return;
                    }
                    if (precastBuffs || recoverHealth && !HasFullHP())
                    {
                        MoveWithinRoute(-1);
                        return;
                    }
                }
                _fightTimer = 0;
                if (!precastBuffs && bossOnly)
                {
                    if (!ChargeActive())
                    {
                        if (CastCharge())
                        {
                            return;
                        }
                    }

                    if (!ParryActive() && (fastCombat || GetHPPercentage() <= .5f))
                    {
                        if (CastParry())
                        {
                            return;
                        }
                    }

                    if (GetHPPercentage() < .75)
                    {
                        if (CastHeal())
                            return;
                    }
                }

                if (fastCombat)
                {
                    if (GetHPPercentage() < .75)
                    {
                        if (CastHeal())
                            return;
                    }

                    if (GetHPPercentage() < .60)
                    {
                        if (CastHyperRegen())
                            return;
                    }
                }

                
                return;
            }

            if (zone < 1000 && Settings.BlacklistedBosses.Contains(_character.adventureController.currentEnemy.spriteID))
            {
                MoveWithinRoute(-1);
                MoveWithinRoute(zone);
                return;
            }

            //We have an enemy. Lets check if we're in bossOnly mode
            if (bossOnly && zone < 1000 && !IsWalderpFight())
            {
                var ec = _character.adventureController.currentEnemy.enemyType;
                if (ec != enemyType.boss && !ec.ToString().Contains("bigBoss"))
                {
                    MoveWithinRoute(-1);
                    MoveWithinRoute(zone);
                    return;
                }
            }

            if (IsWalderpFight() && _character.adventure.autoattacking)
            {
                if (!ToggleIdle()) return;
                return;
            }

            _isFighting = true;
            _enemyName = _character.adventureController.currentEnemy.name;
            //We have an enemy and we're ready to fight. Run through our combat routine
            if (_character.training.attackTraining[1] > 0 || IsWalderpFight())
                DoCombat(fastCombat);
        }
    }
}
