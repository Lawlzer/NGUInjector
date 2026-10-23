using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NGUInjector.Managers
{
    internal static class CombatHelpers
    {
        private static bool Current(Main.ExecutionGrant grant) => Main.reference != null
            && Main.reference.ValidateExecutionGrant(grant, Main.ExecutionDomain.Route | Main.ExecutionDomain.NativeSettings);

        internal static bool CastCharge(Main.ExecutionGrant grant)
        {
            if (Current(grant) && Main.Character.adventureController.chargeMove.button.IsInteractable() && Current(grant))
            {
                Main.Character.adventureController.chargeMove.doMove();
                return Current(grant);
            }

            return false;
        }

        internal static bool CastParry(Main.ExecutionGrant grant)
        {
            if (Current(grant) && Main.Character.adventureController.parryMove.button.IsInteractable() && Current(grant))
            {
                Main.Character.adventureController.parryMove.doMove();
                return Current(grant);
            }

            return false;
        }

        internal static bool ChargeReady()
        { 
            return Main.Character.adventureController.chargeMove.button.IsInteractable();
        }

        internal static bool ParryReady()
        {
            return Main.Character.adventureController.parryMove.button.IsInteractable();
        }

        internal static float GetChargeCooldown()
        {
            var ua = Main.Character.adventureController.chargeMove;
            var type = ua.GetType().GetField("chargeTimer",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var val = type?.GetValue(ua);
            if (val == null)
            {
                return 0;
            }

            return (float)val / Main.Character.chargeCooldown();
        }

        internal static bool HealReady()
        {
            return Main.Character.adventureController.healMove.button.IsInteractable();
        }

        internal static bool CastHeal(Main.ExecutionGrant grant)
        {
            if (Current(grant) && Main.Character.adventureController.healMove.button.IsInteractable() && Current(grant))
            {
                Main.Character.adventureController.healMove.doMove();
                return Current(grant);
            }

            return false;
        }

        internal static bool CastHyperRegen(Main.ExecutionGrant grant)
        {
            if (Current(grant) && Main.PlayerController.hyperRegenTime <= 0 && Main.Character.adventureController.hyperRegenMove.button.IsInteractable() && Current(grant))
            {
                Main.Character.adventureController.hyperRegenMove.doMove();
                return Current(grant);
            }

            return false;
        }

        internal static bool ParryActive()
        {
            return Main.PlayerController.isParrying;
        }

        internal static bool ChargeActive()
        {
            return Main.PlayerController.chargeFactor > 1.05;
        }

        internal static bool UltimateBuffActive()
        {
            return Main.PlayerController.ultimateBuffTime > 0 && Main.PlayerController.ultimateBuffTime < Main.Character.ultimateBuffDuration();
        }

        internal static bool DefenseBuffActive()
        {
            return Main.PlayerController.defenseBuffTime > 0 && Main.PlayerController.defenseBuffTime < Main.Character.defenseBuffDuration();
        }

        internal static float GetUltimateAttackCooldown()
        {
            var ua = Main.Character.adventureController.ultimateAttackMove;
            var type = ua.GetType().GetField("ultimateAttackTimer",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var val = type?.GetValue(ua);
            if (val == null)
            {
                return 0;
            }

            return (float)val / Main.Character.ultimateAttackCooldown();
        }

        internal static bool CastUltimateBuff(Main.ExecutionGrant grant)
        {
            if (Current(grant) && Main.Character.adventureController.ultimateBuffMove.button.IsInteractable() && Current(grant))
            {
                Main.Character.adventureController.ultimateBuffMove.doMove();
                return Current(grant);
            }

            return false;
        }

        internal static bool CastMegaBuff(Main.ExecutionGrant grant)
        {
            if (Current(grant) && Main.Character.adventureController.megaBuffMove.button.IsInteractable() && Current(grant))
            {
                Main.Character.adventureController.megaBuffMove.doMove();
                return Current(grant);
            }

            return false;
        }

        internal static bool CastOffensiveBuff(Main.ExecutionGrant grant)
        {
            if (Current(grant) && Main.Character.adventureController.offenseBuffMove.button.IsInteractable() && Current(grant))
            {
                Main.Character.adventureController.offenseBuffMove.doMove();
                return Current(grant);
            }

            return false;
        }

        internal static bool BlockActive()
        {
            return Main.PlayerController.isBlocking;
        }

        internal static bool CastBlock(Main.ExecutionGrant grant)
        {
            if (Current(grant) && Main.Character.adventureController.blockMove.button.IsInteractable() && Current(grant))
            {
                Main.Character.adventureController.blockMove.doMove();
                return Current(grant);
            }

            return false;
        }

        internal static bool CastDefensiveBuff(Main.ExecutionGrant grant)
        {
            if (Current(grant) && Main.Character.adventureController.defenseBuffMove.button.IsInteractable() && Current(grant))
            {
                Main.Character.adventureController.defenseBuffMove.doMove();
                return Current(grant);
            }

            return false;
        }

        internal static bool CastParalyze(Main.ExecutionGrant grant)
        {
            if (!Current(grant) || !Main.Character.adventureController.paralyzeMove.button.IsInteractable() || !Current(grant))
            {
                return false;
            }

            Main.Character.adventureController.paralyzeMove.doMove();
            return Current(grant);
        }

        internal static bool UltimateAttackReady()
        {
            return Main.Character.adventureController.ultimateAttackMove.button.IsInteractable();
        }

        internal static bool PierceReady()
        {
            return Main.Character.adventureController.pierceMove.button.IsInteractable();
        }

        internal static bool ChargeUnlocked()
        {
            return Main.Character.training.defenseTraining[3] >= 20000L;
        }

        internal static bool ParryUnlocked()
        {
            return Main.Character.training.attackTraining[2] >= 15000L;
        }

        internal static bool UltimateBuffUnlocked()
        {
            return Main.Character.training.defenseTraining[4] >= 25000L;
        }

        internal static bool UltimateBuffReady()
        {
            return Main.Character.adventureController.ultimateBuffMove.button.IsInteractable();
        }

        internal static bool DefensiveBuffUnlocked()
        {
            return Main.Character.training.defenseTraining[0] >= 5000L;
        }

        internal static bool DefensiveBuffReady()
        {
            return Main.Character.adventureController.defenseBuffMove.button.IsInteractable();
        }

        internal static bool MegaBuffUnlocked()
        {
            return Main.Character.training.defenseTraining[4] >= 25000L && Main.Character.wishes.wishes[8].level >= 1;
        }

        internal static bool MegaBuffReady()
        {
            return Main.Character.adventureController.megaBuffMove.button.IsInteractable();
        }

        internal static bool OhShitUnlocked()
        {
            return Main.Character.wishes.wishes[58].level >= 1 && Main.Character.allChallenges.hasParalyze() &&
                   Main.Character.training.defenseTraining[1] >= 10000L && Main.Character.settings.hasHyperRegen;
        }

        internal static bool OhShitReady()
        {
            return Main.Character.adventureController.ohShitMove.button.IsInteractable();
        }

        internal static bool CastOhShit(Main.ExecutionGrant grant)
        {
            if (Current(grant) && Main.Character.adventureController.ohShitMove.button.IsInteractable() && Current(grant))
            {
                Main.Character.adventureController.ohShitMove.doMove();
                return Current(grant);
            }

            return false;
        }
    }
}
