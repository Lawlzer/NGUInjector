using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private bool _customAdventureGoldPushActive;
        private bool _customAdventureGoldPushEnteredZone;
        private int _customAdventureGoldPushZone = -1;
        private int _customAdventureAttemptedGoldPushZone = -1;

        private const int CustomAdventureModeOptimalItopod = 0;
        private const int CustomAdventureModeFarmZone = 1;
        private const int CustomAdventureModeFarmBoosts = 2;
        private const double CustomAdventureGoldPushMaxSeconds = 60 * 60d;

        private bool TryRouteCustomAdventureGoal()
        {
            if (!Settings.CustomAdventureEnabled)
                return false;

            if (_questManager != null && _questManager.IsQuesting() >= 0)
                return false;

            var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null ? LoadRuntimeCustomPlan() : null;
            if (CustomGoalBlocksAdventureGoldPush(plan, out var blockedReason))
            {
                CancelCustomAdventureGoldPush(blockedReason);
                CancelCustomTitanGoldKill(blockedReason);
                return RouteConfiguredAdventureGoal($"{blockedReason} active; skipping adventure gold push");
            }

            if (TryRouteCustomTitanGoldKill())
                return true;

            var highestZone = ZoneHelpers.GetMaxReachableZone(false);
            var canGoldPush = CustomAdventureGoldPushAllowed(out var goldPushUnavailableReason) && highestZone >= 0;

            if (_customAdventureGoldPushActive && !canGoldPush)
            {
                CancelCustomAdventureGoldPush(goldPushUnavailableReason ?? "gold push cutoff reached");
                return RouteConfiguredAdventureGoal(goldPushUnavailableReason);
            }

            if (_customAdventureGoldPushActive)
            {
                if (LoadoutManager.CurrentLock == LockType.Gold)
                {
                    // Initial healing is allowed; after entry, Safe Zone means the attempt died.
                    // Boss-only trash rerolls leave and re-enter within the same ManualZone call.
                    if (_customAdventureGoldPushEnteredZone && (Character.adventure.zone == -1 || Character.adventure.curHP <= 0))
                    {
                        var reason = $"Adventure gold push failed in {ZoneName(_customAdventureGoldPushZone)}; not retrying this rebirth";
                        _customAdventureAttemptedGoldPushZone = _customAdventureGoldPushZone;
                        CancelCustomAdventureGoldPush(reason);
                        return RouteConfiguredAdventureGoal(reason);
                    }

                    SetCustomAutomationStatus($"Adventure: gold push boss in {ZoneName(_customAdventureGoldPushZone)}, then restore and return to the selected adventure goal");
                    _combManager.ManualZone(_customAdventureGoldPushZone, true, true, false, true, false, true);
                    _customAdventureGoldPushEnteredZone |= Character.adventure.zone == _customAdventureGoldPushZone;
                    return true;
                }

                _customAdventureGoldPushActive = false;
                _customAdventureGoldPushEnteredZone = false;
                _customAdventureAttemptedGoldPushZone = _customAdventureGoldPushZone;
                _customAdventureGoldPushZone = -1;
                return RouteConfiguredAdventureGoal("Adventure gold push complete");
            }

            if (canGoldPush && _customAdventureAttemptedGoldPushZone != highestZone)
            {
                if (!LoadoutManager.CanSwap())
                {
                    SetCustomAutomationStatus($"Adventure: waiting for loadout lock {LoadoutManager.CurrentLock} before gold push");
                    return true;
                }

                LoadoutManager.SaveCurrentLoadout();
                LoadoutManager.AcquireLock(LockType.Gold);
                var result = GearOptimizer.OptimizeAndEquip(GearOptimizationGoal.Gold, GetActiveFightScoreMode(), true);
                if (!result.Success)
                {
                    LoadoutManager.ReleaseLock();
                    return RouteConfiguredAdventureGoal($"Adventure gold push skipped: {result.Message}");
                }

                _customAdventureGoldPushActive = true;
                _customAdventureGoldPushZone = highestZone;
                Log($"Custom adventure gold push in {ZoneName(highestZone)}: {result.Message}. Boss {Character.bossID}, effective boss {ZoneHelpers.CurrentRebirthEffectiveBossId()}, requires boss {ZoneHelpers.ZoneUnlockEffectiveBossId(highestZone)}.");
                SetCustomAutomationStatus($"Adventure: gold gear equipped, pushing boss in {ZoneName(highestZone)}");
                _combManager.ManualZone(highestZone, true, true, false, true, false, true);
                _customAdventureGoldPushEnteredZone = Character.adventure.zone == highestZone;
                return true;
            }

            return RouteConfiguredAdventureGoal(canGoldPush ? $"Adventure {ZoneName(highestZone)} gold push already attempted this rebirth" : goldPushUnavailableReason);
        }

        private bool CustomAdventureGoldPushAllowed(out string reason)
        {
            if (!Settings.CustomAdventureGoldPush)
            {
                reason = "Adventure gold push disabled";
                return false;
            }

            if (!TimeMachineUnlockedForCustomAdventure())
            {
                reason = "Time Machine locked; skipping adventure gold push";
                return false;
            }

            var rebirthSeconds = Character.rebirthTime.totalseconds;
            if (rebirthSeconds >= CustomAdventureGoldPushMaxSeconds)
            {
                reason = $"rebirth time {CustomStageAllocator.FormatSeconds((int) rebirthSeconds)} reached; adventure gold push stops after {CustomStageAllocator.FormatSeconds((int) CustomAdventureGoldPushMaxSeconds)}";
                return false;
            }

            reason = null;
            return true;
        }

        private bool RouteConfiguredAdventureGoal(string note)
        {
            if (!Settings.CustomAdventureEnabled)
            {
                if (!string.IsNullOrWhiteSpace(note))
                {
                    SetCustomAutomationStatus(note);
                }

                return false;
            }

            if (Settings.CustomAdventureMode == CustomAdventureModeFarmZone)
            {
                var targetZone = Settings.CustomAdventureZone;
                if (CombatManager.IsZoneUnlocked(targetZone))
                {
                    var prefix = string.IsNullOrWhiteSpace(note) ? string.Empty : note + ". ";
                    SetCustomAutomationStatus($"Adventure: {prefix}farming {ZoneName(targetZone)}.");
                    _combManager.IdleZone(targetZone, false, false);
                    return true;
                }

                RouteCustomItopod($"Farm zone {ZoneName(targetZone)} is locked; optimized ITOPOD fallback" + (string.IsNullOrWhiteSpace(note) ? string.Empty : $". {note}"));
                return true;
            }

            if (Settings.CustomAdventureMode == CustomAdventureModeFarmBoosts)
            {
                var target = BoostFarmHelper.GetBestTarget();
                if (target == null)
                {
                    RouteCustomItopod("No boost farm target available; optimized ITOPOD fallback" + (string.IsNullOrWhiteSpace(note) ? string.Empty : $". {note}"));
                    return true;
                }

                if (target.IsItopod)
                {
                    RouteCustomItopod("Boost farm target: " + BoostFarmHelper.FormatTarget(target) + (string.IsNullOrWhiteSpace(note) ? string.Empty : $". {note}"));
                    return true;
                }

                var prefix = string.IsNullOrWhiteSpace(note) ? string.Empty : note + ". ";
                SetCustomAutomationStatus($"Adventure: {prefix}boost farming {ZoneName(target.Zone)} ({target.Summary}; score {target.Score:0.#####}).");
                _combManager.IdleZone(target.Zone, false, false);
                return true;
            }

            RouteCustomItopod(string.IsNullOrWhiteSpace(note) ? "Adventure goal is optimal ITOPOD" : note);
            return true;
        }

        private void RouteCustomItopod(string reason)
        {
            PrepareOptimalItopodFloor();
            SetCustomAutomationStatus($"Adventure: ITOPOD optimal floor. {reason}.");
            _combManager.IdleZone(1000, false, false);
        }

        private static void PrepareOptimalItopodFloor()
        {
            var controller = Character.adventureController;
            var optimal = ZoneHelpers.CalculateBestItopodLevel();
            controller.itopodStartInput.text = optimal.ToString();
            controller.itopodEndInput.text = optimal.ToString();
            controller.verifyItopodInputs();
        }

        private static bool TimeMachineUnlockedForCustomAdventure()
        {
            return Character.buttons.brokenTimeMachine.interactable && !Character.challenges.timeMachineChallenge.inChallenge;
        }

        private void ResetCustomAdventureRuntime()
        {
            CancelCustomAdventureGoldPush("rebirth runtime reset");
            _customAdventureGoldPushActive = false;
            _customAdventureGoldPushEnteredZone = false;
            _customAdventureGoldPushZone = -1;
            _customAdventureAttemptedGoldPushZone = -1;
        }

        private void CancelCustomAdventureGoldPush(string reason)
        {
            if (!_customAdventureGoldPushActive)
                return;

            // An entered attempt still counts if a titan or another priority interrupts cleanup.
            if (_customAdventureGoldPushEnteredZone || LoadoutManager.CurrentLock != LockType.Gold)
                _customAdventureAttemptedGoldPushZone = _customAdventureGoldPushZone;

            if (LoadoutManager.CurrentLock == LockType.Gold)
            {
                LoadoutManager.RestoreGear();
                LoadoutManager.ReleaseLock();
                FocusSettle();
            }

            _customAdventureGoldPushActive = false;
            _customAdventureGoldPushEnteredZone = false;
            _customAdventureGoldPushZone = -1;
            Log($"Custom adventure gold push cancelled for {reason}.");
        }
    }
}
