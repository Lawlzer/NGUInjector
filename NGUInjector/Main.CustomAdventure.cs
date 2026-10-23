using System;
using System.Linq;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private bool _customAdventureGoldPushActive;
        private bool _customAdventureGoldPushEnteredZone;
        private int _customAdventureGoldPushZone = -1;
        private int _customAdventureGoldPushHighestZone = -1;
        private int _customAdventureAttemptedGoldPushZone = -1;
        private RouteProcess _customAdventureGoldProcess;
        private string _customAdventureGoldPushCleanupReason;

        private const int CustomAdventureModeOptimalItopod = 0;
        private const int CustomAdventureModeFarmZone = 1;
        private const int CustomAdventureModeFarmBoosts = 2;
        private const int CustomAdventureModeMaxItopodPush = 3;
        private const int CustomAdventureModeManualZone = 4;
        private const double CustomAdventureGoldPushMaxSeconds = 60 * 60d;

        private bool TryRouteCustomAdventureGoal()
        {
            if (ServiceCustomAdventureGoldPushCleanup()) return true;
            if (!Settings.CustomAdventureEnabled)
            {
                if (!Settings.ManualAdventureEnabled) ResetManualAdventureSetup();
                return false;
            }

            // The combat tick owns bounded pushes, including pending mode selections.
            if (Settings.CustomAdventureMode == CustomAdventureModeMaxItopodPush)
                return false;

            if (_questManager != null && _questManager.IsQuesting() >= 0)
            {
                if (!Settings.ManualAdventureEnabled) ResetManualAdventureSetup();
                return false;
            }

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
                ResetManualAdventureSetup();
                if (CustomAdventureGoldSetupVerified())
                {
                    // Initial healing is allowed; after entry, Safe Zone means the attempt died.
                    // Boss-only trash rerolls leave and re-enter within the same ManualZone call.
                    if (_customAdventureGoldPushEnteredZone && (Character.adventure.zone == -1 || Character.adventure.curHP <= 0))
                    {
                        var failedZone = _customAdventureGoldPushZone;
                        _customAdventureAttemptedGoldPushZone = _customAdventureGoldPushHighestZone;
                        var nextZone = failedZone - 1;
                        while (nextZone >= 0 && (ZoneHelpers.ZoneIsTitan(nextZone) || !ZoneHelpers.IsZoneUnlocked(nextZone)))
                            nextZone--;

                        if (nextZone < 0)
                        {
                            var reason = $"Adventure gold push failed in {ZoneName(failedZone)}; no lower boss zone remains";
                            CancelCustomAdventureGoldPush(reason);
                            return RouteConfiguredAdventureGoal(reason);
                        }

                        _customAdventureGoldPushZone = nextZone;
                        _customAdventureGoldPushEnteredZone = false;
                        PersistRouteExecutionCommitments();
                        if (Character.adventure.zone != -1)
                            _combManager.MoveToZone(-1, RouteProcessGrant(_customAdventureGoldProcess), () => ObserveRouteProcess(_customAdventureGoldProcess));
                        Log($"Custom adventure gold push died in {ZoneName(failedZone)}; recovering and trying {ZoneName(nextZone)} with gold/combat gear.");
                    }

                    SetCustomAutomationStatus($"Adventure: gold push boss in {ZoneName(_customAdventureGoldPushZone)}, then restore and return to the selected adventure goal");
                    _combManager.ManualZone(_customAdventureGoldPushZone, true, true, false, true, false, true,
                        RouteProcessGrant(_customAdventureGoldProcess), () => ObserveRouteProcess(_customAdventureGoldProcess));
                    if (_customAdventureGoldPushActive)
                        _customAdventureGoldPushEnteredZone |= Character.adventure.zone == _customAdventureGoldPushZone;
                    return true;
                }

                CancelCustomAdventureGoldPush("equipment ownership lost");
                return RouteConfiguredAdventureGoal("Adventure gold push complete");
            }

            if (canGoldPush && _customAdventureAttemptedGoldPushZone != highestZone)
            {
                ResetManualAdventureSetup();
                var result = GearOptimizer.GetCachedOrOptimize(GearOptimizationGoal.AdventureGold, GetActiveFightScoreMode());
                if (!result.Success) { SetCustomAutomationStatus(result.Message); return true; }
                var selected = SelectedGearMap(result);
                var preflight = GearOptimizer.CheckExactSwapCapacity(selected, Character.curEnergy, Character.res3.curRes3);
                if (preflight != null) { SetCustomAutomationStatus(preflight); return true; }
                _customAdventureGoldProcess = BeginRouteProcess("adventure-gold", selected);
                _customAdventureGoldPushActive = true;
                _customAdventureGoldPushZone = highestZone;
                _customAdventureGoldPushHighestZone = highestZone;
                _combManager.GoldFightCompleted = () => CancelCustomAdventureGoldPush("boss defeated");
                string failure = null;
                try
                {
                    if (!ApplyRouteProcess(_customAdventureGoldProcess)) failure = "Selected setup is unverified";
                }
                catch (Exception error)
                {
                    failure = error.Message;
                }
                if (failure != null)
                {
                    _customAdventureAttemptedGoldPushZone = highestZone;
                    var foreign = !CustomAdventureGoldSetupVerified();
                    CancelCustomAdventureGoldPush("gear setup failed: " + failure);
                    if (foreign) return true;
                    return RouteConfiguredAdventureGoal("Adventure gold push skipped: " + failure);
                }

                Log($"Custom adventure gold push in {ZoneName(highestZone)}: {result.Message}. Boss {Character.bossID}, effective boss {ZoneHelpers.CurrentRebirthEffectiveBossId()}, requires boss {ZoneHelpers.ZoneUnlockEffectiveBossId(highestZone)}.");
                SetCustomAutomationStatus($"Adventure: gold/combat gear equipped (90% gold, 10% power/toughness), pushing boss in {ZoneName(highestZone)}");
                _combManager.ManualZone(highestZone, true, true, false, true, false, true,
                    RouteProcessGrant(_customAdventureGoldProcess), () => ObserveRouteProcess(_customAdventureGoldProcess));
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
            if (_customAdventureGoldPushActive) CancelCustomAdventureGoldPush("configured adventure route takeover");
            if (_manualTitanActive) return false;
            if (!Settings.CustomAdventureEnabled)
            {
                if (!string.IsNullOrWhiteSpace(note))
                {
                    SetCustomAutomationStatus(note);
                }

                return false;
            }

            // Direct callers such as Adventure quick focus must not replace a live push's bounds.
            if (_maxItopodPushRemainingDeaths > 0 || _maxItopodPushRouting
                || Settings.CustomAdventureMode == CustomAdventureModeMaxItopodPush)
                return false;

            ResolveConfiguredAdventureGoal(out var mode, out var targetZone, out var ruleIndex);
            if (ruleIndex >= 0)
            {
                note = $"Rule {ruleIndex + 1}" + (string.IsNullOrWhiteSpace(note) ? string.Empty : $". {note}");
            }

            if (mode == CustomAdventureModeManualZone)
            {
                var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null ? LoadRuntimeCustomPlan() : null;
                if ((_questManager != null && _questManager.IsQuesting() >= 0)
                    || CustomGoalBlocksAdventureGoldPush(plan, out _))
                {
                    ResetManualAdventureSetup();
                    return false;
                }
                return RouteManualAdventureZone(targetZone, false, note);
            }

            ResetManualAdventureSetup();
            if (mode == CustomAdventureModeFarmZone)
            {
                if (ManualTitanBlocksZone(targetZone))
                {
                    _combManager.MoveToZone(-1);
                    SetCustomAutomationStatus("Adventure: selected titan is reserved for the bounded titan rule. " + _manualTitanStatus);
                    return true;
                }
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

            if (mode == CustomAdventureModeFarmBoosts)
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

        private static int FindMatchingAdventureRule(double power, double toughness, double regen)
        {
            var rules = Settings.CustomAdventureRules;
            for (var i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                if (rule.Matches(power, toughness, regen)
                    && (rule.Mode != CustomAdventureModeFarmZone && rule.Mode != CustomAdventureModeManualZone
                        || CombatManager.IsZoneUnlocked(rule.Zone)))
                    return i;
            }
            return -1;
        }

        private static void ResolveConfiguredAdventureGoal(out int mode, out int zone, out int ruleIndex)
        {
            mode = Settings.CustomAdventureMode;
            zone = Settings.CustomAdventureZone;
            ruleIndex = FindMatchingAdventureRule(Character.totalAdvAttack(), Character.totalAdvDefense(), Character.totalAdvHPRegen());
            if (ruleIndex < 0 || mode == CustomAdventureModeMaxItopodPush) return;
            var rule = Settings.CustomAdventureRules[ruleIndex];
            mode = rule.Mode;
            zone = rule.Zone;
        }

        private JSONObject GetAdventureRulesRuntime()
        {
            var state = new JSONObject();
            if (Settings == null || Character == null) return state;
            try
            {
                var power = Character.totalAdvAttack();
                var toughness = Character.totalAdvDefense();
                var regen = Character.totalAdvHPRegen();
                if (float.IsNaN(power) || float.IsInfinity(power) || float.IsNaN(toughness)
                    || float.IsInfinity(toughness) || float.IsNaN(regen) || float.IsInfinity(regen))
                    return state;
                state["power"] = power;
                state["toughness"] = toughness;
                state["regen"] = regen;
                // This is a match preview, not activity ownership; higher-priority routes may be active.
                state["matchedRuleIndex"] = FindMatchingAdventureRule(power, toughness, regen);
            }
            catch
            {
                // Native stat dependencies may not be ready during startup or save loading.
                return new JSONObject();
            }
            return state;
        }

        private static void PrepareOptimalItopodFloor()
        {
            var owner = reference;
            var grant = owner.ObserveRouteGrant(1000);
            var controller = Character.adventureController;
            var optimal = ZoneHelpers.CalculateBestItopodLevel();
            var start = Character.adventure.itopodStart; var end = Character.adventure.itopodEnd;
            var startText = controller.itopodStartInput.text; var endText = controller.itopodEndInput.text;
            Action require = () => { if (!owner.ValidateExecutionGrant(grant, ExecutionDomain.Route | ExecutionDomain.NativeSettings)
                || Character.adventure.itopodStart != start || Character.adventure.itopodEnd != end
                || controller.itopodStartInput.text != startText || controller.itopodEndInput.text != endText)
                throw new InvalidOperationException("Optimal tower settings authority changed."); };
            require();
            if (start == optimal && end == optimal) return;
            startText = optimal.ToString(); controller.itopodStartInput.text = startText; require();
            endText = optimal.ToString(); controller.itopodEndInput.text = endText; require();
            controller.verifyItopodInputs();
            if (!owner.ValidateExecutionGrant(grant, ExecutionDomain.Route | ExecutionDomain.NativeSettings)
                || Character.adventure.itopodStart != optimal || Character.adventure.itopodEnd != optimal)
                throw new InvalidOperationException("Optimal tower floor did not verify.");
        }

        private static bool TimeMachineUnlockedForCustomAdventure()
        {
            return Character.buttons.brokenTimeMachine.interactable && !Character.challenges.timeMachineChallenge.inChallenge;
        }

        private void ResetCustomAdventureRuntime()
        {
            ResetManualAdventureSetup();
            CancelMaxItopodPush();
            CancelCustomAdventureGoldPush("rebirth runtime reset");
            if (_customAdventureGoldPushActive || _maxItopodPushRouting || _delegatedManualAdventureProcess != null) return;
            _customAdventureGoldPushActive = false;
            _customAdventureGoldPushEnteredZone = false;
            _customAdventureGoldPushZone = -1;
            _customAdventureGoldPushHighestZone = -1;
            _customAdventureAttemptedGoldPushZone = -1;
        }

        private bool CustomAdventureGoldSetupVerified()
        {
            return RouteProcessReady(_customAdventureGoldProcess);
        }

        internal bool ServiceCustomAdventureGoldPushCleanup()
        {
            if (!_customAdventureGoldPushActive) return false;
            var process = _customAdventureGoldProcess;
            // A native death can be observed between combat ticks. Accept only the
            // entered attempt's Safe Zone/zero-HP outcome under its unchanged setup.
            if (_customAdventureGoldPushEnteredZone && process != null && process.Zone == _customAdventureGoldPushZone
                && Character.adventure.zone == -1 && Character.adventure.curHP <= 0
                && ExecutionClaimCurrent(process.Claim) && ExecutionClaimSetupOwned(process.Claim)
                && process.Setup.MaterialValid() && process.AutoAttack == Character.adventure.autoattacking
                && process.BeastMode == Character.adventure.beastModeOn
                && !CombatPrepAdventureRouteObjects().Where((x, i) => !ReferenceEquals(x, process.Objects[i])).Any())
                process.Zone = -1;
            var reason = _customAdventureGoldPushCleanupReason;
            if (reason == null && !Settings.CustomAdventureEnabled) reason = "Adventure disabled";
            if (reason == null && !CustomAdventureGoldSetupVerified()) reason = "equipment ownership lost";
            if (reason == null && _questManager != null && _questManager.IsQuesting() >= 0) reason = "questing is active";
            if (reason == null) return false;
            try { CancelCustomAdventureGoldPush(reason); }
            catch (InvalidOperationException) { return true; }
            return false;
        }

        private void CancelCustomAdventureGoldPush(string reason)
        {
            if (!_customAdventureGoldPushActive)
                return;

            // An entered attempt still counts if a titan or another priority interrupts cleanup.
            if (_customAdventureGoldPushEnteredZone || !CustomAdventureGoldSetupVerified())
                _customAdventureAttemptedGoldPushZone = _customAdventureGoldPushHighestZone;

            _customAdventureGoldPushCleanupReason = _customAdventureGoldPushCleanupReason ?? reason;
            if (!EndRouteProcess(_customAdventureGoldProcess)) return;

            _combManager.GoldFightCompleted = null;
            _customAdventureGoldPushActive = false;
            _customAdventureGoldPushEnteredZone = false;
            _customAdventureGoldPushZone = -1;
            _customAdventureGoldPushHighestZone = -1;
            _customAdventureGoldProcess = null;
            _customAdventureGoldPushCleanupReason = null;
            PersistRouteExecutionCommitments();
            Log($"Custom adventure gold push cancelled for {reason}.");
        }
    }
}
