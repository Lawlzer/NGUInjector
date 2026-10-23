using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.AllocationProfiles.RebirthStuff;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private double _customIronPillPrepStartedAt = -1d;
        private double _customIronPillRetryAt;
        private double _customIronPillPrepLastWaitLogAt = -1d;
        private string _customIronPillPlanName;
        private DateTime _customEnergyFruitCapLastRetry = DateTime.MinValue;
        private DateTime _customMagicFruitCapLastRetry = DateTime.MinValue;
        private const double CustomFruitCapRetryCooldownSeconds = 5 * 60d;
        private bool TryActivateCustomFruits(CustomPlan plan, CustomStageRuntime runtime, out string message)
        {
            message = null;
            if (!CustomFruitActivationEligible(plan))
                return true;

            var activated = YggdrasilManager.ActivateAffordableFruits();
            if (runtime != null && (runtime.Stage.GearGoal >= 0 || !string.IsNullOrEmpty(runtime.Stage.PresetId))
                && !ManualAdventureOwnsEquipment)
            {
                if (YggdrasilManager.HasInactiveUnlockedFruit(true) && !CustomFruitCapRetryOnCooldown(true))
                {
                    activated += ActivateCustomFruitsWithCapGear(GearOptimizationGoal.EnergyCap, (FightScoreMode) plan.FightScoreMode);
                    if (YggdrasilManager.HasInactiveUnlockedFruit(true)) MarkCustomFruitCapRetry(true);
                }
                if (YggdrasilManager.HasInactiveUnlockedFruit(false) && !CustomFruitCapRetryOnCooldown(false))
                {
                    activated += ActivateCustomFruitsWithCapGear(GearOptimizationGoal.MagicCap, (FightScoreMode) plan.FightScoreMode);
                    if (YggdrasilManager.HasInactiveUnlockedFruit(false)) MarkCustomFruitCapRetry(false);
                }
            }

            if (activated <= 0)
                return true;

            message = FruitActivationSummary(activated);
            return true;
        }

        private void RunCustomFruitActivation(CustomPlan plan)
        {
            if (!CustomFruitActivationAutomationActive())
                return;

            if (plan == null)
            {
                if (CustomPlanStore == null)
                    return;

                plan = LoadRuntimeCustomPlan();
            }

            if (!TryActivateCustomFruits(plan, null, out var fruitActivation))
            {
                if (!string.IsNullOrWhiteSpace(fruitActivation))
                    SetCustomAutomationStatus(fruitActivation);
                return;
            }

            if (!string.IsNullOrWhiteSpace(fruitActivation))
                SetCustomAutomationStatus(fruitActivation);
        }

        private int ActivateCustomFruitsWithCapGear(GearOptimizationGoal goal, FightScoreMode fightScoreMode)
        {
            var entry = _customStageEquipmentKey;
            var gear = SelectSetupGear(goal, fightScoreMode, true);
            var support = CaptureSupportState().Selection;
            SuspendProtectedStageProgress();
            var claim = BeginExecutionClaim("fruit-cap", "fruit-cap:" + entry,
                ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route,
                () => Settings.UseCustomRebirthPlan && entry == _customStageEquipmentKey && !ManualAdventureOwnsEquipment);
            try
            {
                var selected = AcquireFiniteActionSetup(claim, gear, support, true, false);
                if (!FiniteActionSetupVerified(claim, selected)) return 0;
                _fruitCapActionReady = () => FiniteActionSetupVerified(claim, selected);
                return YggdrasilManager.ActivateAffordableFruits(true);
            }
            finally { _fruitCapActionReady = null; EndExecutionClaim(claim); }
        }

        private static string FruitActivationSummary(int activated)
        {
            return activated == 1 ? "activated 1 fruit" : $"activated {activated} fruits";
        }

        private bool CustomFruitCapRetryOnCooldown(bool usesEnergy)
        {
            var lastRetry = usesEnergy ? _customEnergyFruitCapLastRetry : _customMagicFruitCapLastRetry;
            return (DateTime.UtcNow - lastRetry).TotalSeconds < CustomFruitCapRetryCooldownSeconds;
        }

        private void MarkCustomFruitCapRetry(bool usesEnergy)
        {
            if (usesEnergy)
                _customEnergyFruitCapLastRetry = DateTime.UtcNow;
            else
                _customMagicFruitCapLastRetry = DateTime.UtcNow;
        }

        private void ResetCustomFruitCapRetryCooldowns()
        {
            _customEnergyFruitCapLastRetry = DateTime.MinValue;
            _customMagicFruitCapLastRetry = DateTime.MinValue;
        }

        private static bool CustomFruitActivationEligible(CustomPlan plan)
        {
            return CustomFruitActivationAutomationActive()
                   && plan != null
                   && plan.AutoActivateFruits
                   && CustomPlannedRebirthSeconds(plan) >= 3600;
        }

        private static bool CustomFruitActivationAutomationActive()
        {
            return Settings.UseCustomRebirthPlan
                   || Settings.CustomAdventureEnabled
                   || Settings.ManualAdventureEnabled
                   || Settings.ManualTitanEnabled;
        }


        private void RunCustomBloodSpellAutomation(CustomPlan plan)
        {
            RunCustomBloodSpellToggles(plan);
            if (!CustomIronPillPrepActive())
                return;

            var elapsed = Math.Max(0d, Character.rebirthTime.totalseconds - _customIronPillPrepStartedAt);
            var acquired = IronWindowData;
            if (acquired == null) return;
            if (IronPillAutomation.TargetReached(elapsed, CustomIronPillWorth(), acquired["saveSeconds"].AsInt, acquired["minWorth"].AsDouble))
            {
                if (TryCastCustomIronPill())
                {
                    ResetCustomIronPillPrep();
                    _customIronPillRetryAt = Character.rebirthTime.totalseconds + 10d;
                    // Restore spells without re-entering preparation on this same pass.
                    ApplyCustomBloodSpellToggles(plan);
                    return;
                }

                var waitReason = CustomIronPillWaitReason();
                if (_customIronPillPrepLastWaitLogAt < 0d || elapsed - _customIronPillPrepLastWaitLogAt >= 60d)
                {
                    _customIronPillPrepLastWaitLogAt = elapsed;
                    Log($"Custom Iron Pill target reached after {elapsed:0}s; waiting to cast: {waitReason}.");
                }
            }
        }

        private void UpdateCustomIronPillPrep(CustomPlan plan)
        {
            var planName = plan != null ? Settings.CustomPlanFile : null;
            var effectivePlan = Settings.UseCustomRebirthPlan ? plan : null;
            if (_customIronPillPlanName != planName)
            {
                // A new plan must not inherit elapsed saving time or the previous plan's timeout.
                var wasSaving = _customIronPillPrepStartedAt >= 0d;
                ResetCustomIronPillPrep();
                _customIronPillRetryAt = wasSaving ? (Character?.rebirthTime?.totalseconds ?? 0d) + 10d : 0d;
                _customIronPillPlanName = planName;
            }
            var eligible = CustomIronPillEligibilityBlocker(plan).Length == 0;
            if (!IronPillAutomation.ShouldReserve(eligible, CustomIronPillPrepActive(), IronPillCooldownRemainingSeconds(),
                EffectiveCustomIronPillSaveSeconds(effectivePlan), EffectiveCustomIronPillMinWorth(effectivePlan)))
            {
                ResetCustomIronPillPrep();
                return;
            }

            if (CustomIronPillPrepActive())
            {
                if (IronWindowData != null && Character.rebirthTime.totalseconds < IronWindowData["deadline"].AsDouble)
                    return;

                ResetCustomIronPillPrep();
                _customIronPillRetryAt = Character.rebirthTime.totalseconds + 900d;
                FinishIronBloodWindow(false);
                Log("Custom Iron Pill saving timed out; ordinary magic priorities and spells restored for 900s.");
                return;
            }

            if (Character.rebirthTime.totalseconds < _customIronPillRetryAt)
                return;

            // Do not spend banked blood while the Blood Magic scene/state is unavailable.
            if (Character.bloodMagicController == null || Character.magic == null || Character.buttons?.bloodMagic == null
                || !Character.buttons.bloodMagic.interactable || Character.bloodMagic.ritual == null || Character.bloodMagic.ritual.Count == 0
                || Character.bloodMagicController.bloodMagics == null || Character.bloodMagicController.bloodMagics.Length == 0)
                return;

            if (!AcquireIronBloodWindow(effectivePlan, out var originalStart)) return;
            if (!TryFlushCustomBloodToNumber())
            {
                _customIronPillRetryAt = Character.rebirthTime.totalseconds + 900d;
                Log("Custom Iron Pill saving postponed for 900s: NUMBER blood flush did not succeed.");
                return;
            }

            _customIronPillPrepStartedAt = originalStart;
            Log($"Custom Iron Pill saving started: boss {Character.bossID}, cooldown remaining {IronPillCooldownRemainingSeconds():0}s.");
        }

        private bool TryFlushCustomBloodToNumber()
        {
            var blood = Character.bloodMagic.bloodPoints;
            if (double.IsNaN(blood) || double.IsInfinity(blood) || blood < 0d)
                return false;
            if (blood == 0d)
                return true;

            // NUMBER has no minimum in RebirthPowerSpell (unlike loot/gold/pill).
            // Call it directly: autoSpell() would split the pool across enabled spells.
            if (_ironWindowRecord == null) return false;
            var id = ActionIdentity("blood-number", "iron-pill-prep", _ironWindowRecord.Identity.Key);
            var action = new NumberBloodAction { Identity = id, Blood = blood, Number = Character.bloodMagic.rebirthPower,
                Footprint = ExecutionDomain.FiniteAction | ExecutionDomain.Magic };
            var result = RunFiniteAction(action, ResolveExecutionGrant(id.Owner, id.Acquisition, action.Footprint,
                () => Settings.ManageMagic));
            return result.Committed && result.Durable;
        }

        private void RunCustomBloodSpellToggles(CustomPlan plan)
        {
            if (plan == null && (_globalPresetManualId != null || _customSustainedFocusGoal.HasValue))
                plan = LoadRuntimeCustomPlan();
            UpdateCustomIronPillPrep(plan);
            ApplyCustomBloodSpellToggles(plan);
        }

        private void ApplyCustomBloodSpellToggles(CustomPlan plan)
        {
            if (Settings == null || !Settings.UseCustomRebirthPlan) plan = null;
            if (!EffectiveCustomAutoBloodSpellsEnabled(plan) || Character == null || Character.bloodMagic == null
                || Character.bloodSpells == null || Character.bloodMagicController == null)
                return;

            var ironPrep = CustomIronPillPrepActive();
            var shortRebirth = !ironPrep && CustomBloodGoalsSuppressed(plan);
            var wantLoot = false;
            var wantGold = false;
            if (!ironPrep && !TryGetCustomBloodGoals(plan, out wantLoot, out wantGold)) return;
            wantLoot &= !ironPrep && !shortRebirth;
            wantGold &= !ironPrep && !shortRebirth;
            var wantNumber = !ironPrep && (shortRebirth || (!wantLoot && !wantGold));

            var footprint = ExecutionDomain.NativeSettings | ExecutionDomain.Magic;
            var grant = ResolveExecutionGrant("blood-auto-flags", Settings.CustomPlanFile, footprint,
                () => EffectiveCustomAutoBloodSpellsEnabled(plan));
            if (!ValidateExecutionGrant(grant, footprint)) return;
            if (Character.bloodMagic.lootAutoSpell != wantLoot)
            {
                Character.bloodMagic.lootAutoSpell = wantLoot;
                Character.bloodSpells.updateLootToggleState();
                if (!ValidateExecutionGrant(grant, footprint)) return;
            }
            if (Character.bloodMagic.goldAutoSpell != wantGold)
            {
                Character.bloodMagic.goldAutoSpell = wantGold;
                Character.bloodSpells.updateGoldToggleState();
                if (!ValidateExecutionGrant(grant, footprint)) return;
            }
            if (Character.bloodMagic.rebirthAutoSpell != wantNumber)
            {
                Character.bloodMagic.rebirthAutoSpell = wantNumber;
                Character.bloodSpells.updateRebirthToggleState();
                ValidateExecutionGrant(grant, footprint);
            }
        }

        private double CustomIronPillWorth()
        {
            var bonus = Character.settings.rebirthDifficulty >= difficulty.evil
                ? Character.adventureController.itopod.ironPillBonus() : 1f;
            return IronPillAutomation.Worth(Character.bloodMagic.bloodPoints, bonus);
        }

        private double IronPillCooldownRemainingSeconds()
        {
            if (Character == null || Character.bloodMagic?.adventureSpellTime == null || Character.bloodSpells == null)
                return double.PositiveInfinity;

            return Math.Max(0d, Character.bloodSpells.adventureSpellCooldown - Character.bloodMagic.adventureSpellTime.totalseconds);
        }

        private bool TryCastCustomIronPill()
        {
            if (IronPillCooldownRemainingSeconds() > 0)
                return false;

            if (Character.bloodMagic.bloodPoints < Character.bloodSpells.minAdventureBlood())
                return false;

            var entry = new IronPillHistoryEntry
            {
                CastAtUtc = DateTime.UtcNow.ToString("O"),
                WaitSeconds = Math.Max(0d, Character.rebirthTime.totalseconds - _customIronPillPrepStartedAt),
                Worth = CustomIronPillWorth(),
                RebirthSeconds = Character.rebirthTime.totalseconds
            };
            if (_ironWindowRecord == null) return false;
            var id = ActionIdentity("blood-iron-pill", "iron-pill", _ironWindowRecord.Identity.Key);
            var action = new PillBloodAction { Identity = id, Blood = Character.bloodMagic.bloodPoints,
                Power = Character.adventure.attack, Toughness = Character.adventure.defense,
                Health = Character.adventure.maxHP, Regen = Character.adventure.regen,
                Worth = (float)Math.Min(100000000d, entry.Worth), Footprint = ExecutionDomain.FiniteAction | ExecutionDomain.Magic };
            var result = RunFiniteAction(action, ResolveExecutionGrant(id.Owner, id.Acquisition, action.Footprint,
                () => CustomIronPillPrepActive()));
            if (!result.Committed || !result.Durable) return false;
            FinishIronBloodWindow(true);
            if (!result.Verified) return true;

            Log($"Custom Iron Pill cast: waited {entry.WaitSeconds:0}s, worth {entry.Worth:0.###E+0} Power/Toughness.");
            try
            {
                Settings.AddIronPillHistory(entry);
            }
            catch (Exception e)
            {
                LogError("Saving Iron Pill history", e);
            }
            return true;
        }

        private string CustomIronPillWaitReason()
        {
            var cooldown = IronPillCooldownRemainingSeconds();
            if (cooldown > 0d)
                return $"cooldown {CustomStageAllocator.FormatSeconds((int) Math.Ceiling(cooldown))}";

            return $"blood {Character.bloodMagic.bloodPoints:0.###}/{Character.bloodSpells.minAdventureBlood():0.###}";
        }

        private bool CustomIronPillPrepActive()
        {
            return _customIronPillPrepStartedAt >= 0 && Character?.rebirthTime != null
                   && Settings != null && Settings.ManageMagic
                   && (Settings.UseCustomRebirthPlan || _globalPresetManualId != null || _customSustainedFocusGoal.HasValue)
                   && _customIronPillPlanName == Settings.CustomPlanFile
                   && Character.rebirthTime.totalseconds >= _customIronPillPrepStartedAt
                   && !_manualRebirthPending && !_customPreRebirthPrepRunning && !BossPushRunning;
        }

        internal static bool IronPillBloodReserved => reference != null && reference.CustomIronPillPrepActive();

        private void ResetCustomIronPillPrep()
        {
            _customIronPillPrepStartedAt = -1d;
            _customIronPillPrepLastWaitLogAt = -1d;
        }

    }
}
