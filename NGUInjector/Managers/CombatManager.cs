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

            if (IsWalderpFight() && WalderpAttacksLeft() <= 1)
                return;

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

            if (ai == AI.charger && eai.GetPV<int>("chargeCooldown") >= 3)
            {
                if (ac.blockMove.button.IsInteractable() && !_pc.isParrying)
                {
                    ac.blockMove.doMove();
                    return true;
                }

                if (ac.parryMove.button.IsInteractable() && !_pc.isBlocking && !_pc.isParrying)
                {
                    ac.parryMove.doMove();
                    return true;
                }
            }

            if (ai == AI.rapid && eai.GetPV<int>("rapidEffect") >= 6)
            {
                if (ac.blockMove.button.IsInteractable())
                {
                    ac.blockMove.doMove();
                    return true;
                }
            }

            if (ai == AI.exploder && ac.currentEnemy.attackRate - eai.GetPV<float>("enemyAttackTimer") < 1)
            {
                if (ac.blockMove.button.IsInteractable())
                {
                    ac.blockMove.doMove();
                    return true;
                }
            }

            if (ac.currentEnemy.curHP / ac.currentEnemy.maxHP < .2)
            {
                return false;
            }

            if (OhShitUnlocked() && GetHPPercentage() < .5 && OhShitReady())
            {
                if (CastOhShit())
                {
                    return true;
                }
            }

            if (GetHPPercentage() < .5)
            {
                if (CastHeal())
                {
                    return true;
                }
            }

            if (GetHPPercentage() < .5 && !HealReady())
            {
                if (CastHyperRegen())
                {
                    return true;
                }
            }

            if (CastMegaBuff())
            {
                return true;
            }

            if (!MegaBuffUnlocked())
            {
                if (!DefenseBuffActive())
                {
                    if (CastUltimateBuff())
                    {
                        return true;
                    }
                }

                if (UltimateBuffActive())
                {
                    if (CastOffensiveBuff())
                        return true;
                }

                if (GetHPPercentage() < .75 && !UltimateBuffActive() && !BlockActive())
                {
                    if (CastDefensiveBuff())
                        return true;
                }
            }

            if (ai != AI.charger && ai != AI.rapid && ai != AI.exploder && (Settings.MoreBlockParry || !UltimateBuffActive() && !DefenseBuffActive()))
            {
                if (!ParryActive() && !BlockActive())
                {
                    if (CastBlock())
                        return true;
                }

                if (!BlockActive() && !ParryActive())
                {
                    if (CastParry())
                        return true;
                }
            }

            if (_pc.isBlocking || _pc.isParrying)
            {
                return false;
            }

            if (CastParalyze(ai, eai))
                return true;


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

        //private bool ParalyzeBoss()
        //{
        //    var ac = _character.adventureController;
        //    var ai = ac.currentEnemy.AI;
        //    var eai = ac.enemyAI;

        //    if (!ac.paralyzeMove.button.IsInteractable())
        //        return false;

        //    if (GetHPPercentage() < .2)
        //        return false;

        //    if (UltimateBuffActive())
        //        return false;

        //    if (ai == AI.charger && eai.GetPV<int>("chargeCooldown") == 0)
        //    {
        //        ac.paralyzeMove.doMove();
        //        return true;
        //    }

        //    if (ai == AI.rapid && eai.GetPV<int>("rapidEffect") < 5)
        //    {
        //        ac.paralyzeMove.doMove();
        //        return true;
        //    }

        //    if (ai != AI.rapid && ai != AI.charger)
        //    {
        //        ac.paralyzeMove.doMove();
        //        return true;
        //    }

        //    return false;
        //}

        private void CombatAttacks(bool fastCombat)
        {
            var ac = _character.adventureController;

            if (IsWalderpFight())
            {
                WalderpCombatAttacks();
                return;
            }

            if (ac.ultimateAttackMove.button.IsInteractable())
            {
                if (fastCombat)
                {
                    ac.ultimateAttackMove.doMove();
                }

                if (ChargeActive())
                {
                    ac.ultimateAttackMove.doMove();
                }

                if (GetChargeCooldown() > .45)
                {
                    ac.ultimateAttackMove.doMove();
                }
            }

            if (ac.pierceMove.button.IsInteractable())
            {
                ac.pierceMove.doMove();
                return;
            }

            if (ac.strongAttackMove.button.IsInteractable())
            {
                ac.strongAttackMove.doMove();
                return;
            }

            if (ac.regularAttackMove.button.IsInteractable())
            {
                ac.regularAttackMove.doMove();
                return;
            }
        }

        private void WalderpCombatAttacks()
        {
            if (WalderpAttacksLeft() <= 1)
                return;

            TryWalderpAttack(3);
        }

        private bool TryHandleWalderpSays()
        {
            if (!IsWalderpFight())
                return false;

            var eai = _character.adventureController.enemyAI;
            if (!TryGetEnemyAIValue(eai, "inWaldoSaysLoop", out bool inWaldoSaysLoop) || !inWaldoSaysLoop)
                return false;

            if (!TryGetEnemyAIValue(eai, "waldoAttackID", out int waldoAttackID) || waldoAttackID < 3 || waldoAttackID > 6)
                return true;

            if (!TryGetEnemyAIValue(eai, "waldoSays", out bool waldoSays))
                return true;

            if (waldoSays)
            {
                TryWalderpAttack(waldoAttackID);
                return true;
            }

            TryWalderpAlternateAttack(waldoAttackID);
            return true;
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

                    ac.regularAttackMove.doMove();
                    return true;
                case 4:
                    if (!ac.strongAttackMove.button.IsInteractable())
                        return false;

                    ac.strongAttackMove.doMove();
                    return true;
                case 5:
                    if (!ac.pierceMove.button.IsInteractable())
                        return false;

                    ac.pierceMove.doMove();
                    return true;
                case 6:
                    if (!ac.ultimateAttackMove.button.IsInteractable())
                        return false;

                    ac.ultimateAttackMove.doMove();
                    return true;
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

        private int WalderpAttacksLeft()
        {
            var eai = _character.adventureController.enemyAI;
            if (!TryGetEnemyAIValue(eai, "growCount", out int growCount))
                return int.MaxValue;

            return 5 - ((growCount + 4) % 6);
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

        internal bool MoveToZone(int zone)
        {
            if (!CanRouteToZone(zone))
                return false;

            _character.adventureController.zoneSelector.changeZone(zone);
            return true;
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

            if (_character.adventure.zone != -1)
                _character.adventureController.zoneSelector.changeZone(-1);

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

        internal void IdleZone(int zone, bool bossOnly, bool recoverHealth)
        {
            if (!CanRouteToZone(zone))
                return;

            if (zone == -1)
            {
                if (_character.adventure.zone != -1)
                {
                    MoveToZone(-1);
                    return;
                }
            }
            //Enable idle attack if its not on
            if (!_character.adventure.autoattacking && !IsWalderpFight())
            {
                _character.adventureController.idleAttackMove.setToggle();
                return;
            }

            //Turn on beast mode depending
            if (_character.adventure.beastModeOn && !Settings.BeastMode && _character.adventureController.beastModeMove.button.interactable)
            {
                _character.adventureController.beastModeMove.doMove();
                return;
            }

            //Turn off beast mode depending
            if (!_character.adventure.beastModeOn && Settings.BeastMode &&
                _character.adventureController.beastModeMove.button.interactable)
            {
                _character.adventureController.beastModeMove.doMove();
                return;
            }

            if (_character.adventure.zone == -1 && !HasFullHP() && recoverHealth)
                return;

            //Check if we're in not in the right zone and not in safe zone, if not move to safe zone first
            if (_character.adventure.zone != zone && _character.adventure.zone != -1)
            {
                MoveToZone(-1);
            }

            //Move to the zone
            if (_character.adventure.zone != zone)
            {
                MoveToZone(zone);
                return;
            }

            //Wait for an enemy to spawn
            if (_character.adventureController.currentEnemy == null)
                return;

            if (zone < 1000 && Settings.BlacklistedBosses.Contains(_character.adventureController.currentEnemy.spriteID))
            {
                MoveToZone(-1);
                MoveToZone(zone);
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
                    MoveToZone(-1);
                }
            }

            if (IsWalderpFight())
            {
                if (_character.adventure.autoattacking)
                {
                    _character.adventureController.idleAttackMove.setToggle();
                    return;
                }

                DoCombat(false);
                return;
            }
        }

        internal void ManualZone(int zone, bool bossOnly, bool recoverHealth, bool precastBuffs, bool fastCombat, bool beastMode, bool preserveIdleMode = false)
        {
            if (!CanRouteToZone(zone))
                return;

            if (zone == -1)
            {
                if (_character.adventure.zone != -1)
                {
                    MoveToZone(-1);
                    return;
                }
            }

            // Legacy manual combat turns idle off. Custom gold-push keeps idle on.
            if (preserveIdleMode)
            {
                if (!_character.adventure.autoattacking && !IsWalderpFight())
                {
                    _character.adventureController.idleAttackMove.setToggle();
                    return;
                }
            }
            else if (!_character.adventure.autoattacking)
            {
                if (_character.training.attackTraining[1] == 0 && !IsWalderpFight())
                {
                    _character.adventureController.idleAttackMove.setToggle();
                    return;
                }
            }
            else
            {
                if (_character.training.attackTraining[1] > 0)
                {
                    _character.adventureController.idleAttackMove.setToggle();
                }
            }

            if (_character.adventure.beastModeOn && !beastMode && _character.adventureController.beastModeMove.button.interactable)
            {
                _character.adventureController.beastModeMove.doMove();
                return;
            }

            if (!_character.adventure.beastModeOn && beastMode &&
                _character.adventureController.beastModeMove.button.interactable)
            {
                _character.adventureController.beastModeMove.doMove();
                return;
            }

            //Move back to safe zone if we're in the wrong zone
            if (_character.adventure.zone != zone && _character.adventure.zone != -1)
            {
                MoveToZone(-1);
            }

            //If precast buffs is true and we have no enemy and charge isn't active, go back to safe zone
            if (precastBuffs && !ChargeActive() && _character.adventureController.currentEnemy == null)
            {
                MoveToZone(-1);
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
                MoveToZone(zone);
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
                    if (LoadoutManager.CurrentLock == LockType.Gold)
                    {
                        Log("Gold Loadout kill done. Turning off setting and swapping gear");
                        Settings.DoGoldSwap = false;
                        LoadoutManager.RestoreGear();
                        LoadoutManager.ReleaseLock();
                        MoveToZone(-1);
                        return;
                    }

                    if (precastBuffs || recoverHealth && !HasFullHP())
                    {
                        MoveToZone(-1);
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

                    if (!ParryActive())
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
                MoveToZone(-1);
                MoveToZone(zone);
                return;
            }

            //We have an enemy. Lets check if we're in bossOnly mode
            if (bossOnly && zone < 1000 && !IsWalderpFight())
            {
                var ec = _character.adventureController.currentEnemy.enemyType;
                if (ec != enemyType.boss && !ec.ToString().Contains("bigBoss"))
                {
                    MoveToZone(-1);
                    MoveToZone(zone);
                    return;
                }
            }

            if (IsWalderpFight() && _character.adventure.autoattacking)
            {
                _character.adventureController.idleAttackMove.setToggle();
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
