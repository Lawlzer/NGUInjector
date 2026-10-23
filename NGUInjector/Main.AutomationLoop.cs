using System;
using NGUInjector.AllocationProfiles.RebirthStuff;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal static bool LegacyAutomationEnabled => false;

        // Stuff on a very short timer
        void QuickStuff()
        {
            if (!ReferenceEquals(reference, this) || _unloaded || _executionLifecycle != ExecutionLifecycle.Running) return;
            try
            {
                ObserveCustomRebirthEpoch();

                ServiceAutomaticRecovery();

                CustomPlan activeCustomPlan = null;
                if (Settings.UseCustomRebirthPlan && CustomPlanStore != null)
                {
                    activeCustomPlan = LoadRuntimeCustomPlan();
                }
                ObserveCustomRebirthReadiness(activeCustomPlan);
                if (CombatPrepActive) return;

                if (TryStartAutomaticStage(activeCustomPlan)) return;
                RunCustomBeardAutomation(activeCustomPlan);
                RunCustomBloodSpellToggles(activeCustomPlan);
                RunQuickCustomResourceRefresh(activeCustomPlan);
                WarnIfCustomAutomationHasIdleResources(activeCustomPlan);
            }
            catch (Exception e)
            {
                LogError("QuickStuff", e);
            }
        }

        // Runs every 10 seconds, our main loop
        void AutomationRoutine()
        {
            if (!ReferenceEquals(reference, this) || _unloaded || _executionLifecycle != ExecutionLifecycle.Running) return;
            try
            {
                ObserveCustomRebirthEpoch();
                // Inventory boosting does not acquire resources or restore gear. Automatic prep
                // observes material growth itself; manual previews and live cleanup remain pinned.
                var customAutoBoost = Settings.CustomAutoBoostEnabled;
                if (customAutoBoost && !Controller.midDrag
                    && _combatPrepRun?.CleanupPending != true && !_resourceReconciliationRunning
                    && (!CombatPrepRunActive || _combatPrepRun.Automatic))
                {
                    try { RunCustomAutoBoost(); }
                    catch (Exception error) { LogError("Custom auto boost", error); }
                }

                if (Character.buttons.yggdrasil.interactable)
                    _yggManager.ManageYggHarvest(allowSwap: !CombatPrepActive);

                ServiceAutomaticRecovery();

                CustomPlan activeCustomPlan = null;
                if (Settings.UseCustomRebirthPlan && CustomPlanStore != null)
                {
                    activeCustomPlan = LoadRuntimeCustomPlan();
                }
                ObserveCustomRebirthReadiness(activeCustomPlan);
                if (CombatPrepActive) { _timeLeft = 10f; return; }
                ProcessPendingManualRebirth();

                if (TryStartAutomaticStage(activeCustomPlan)) { _timeLeft = 10f; return; }

                if (CustomRebirthReady(activeCustomPlan))
                    CancelCustomAdventureGoldPush("Custom rebirth is ready");

                if (Settings.CustomAutoMoneyPitEnabled)
                {
                    var moneyPit = RequestMoneyPit();
                    if (!string.IsNullOrWhiteSpace(moneyPit) && moneyPit.StartsWith("Money Pit Reward", StringComparison.Ordinal))
                        SetCustomAutomationStatus(moneyPit);
                }

                RunCustomAutoExp();
                RunCustomBloodSpellAutomation(activeCustomPlan);
                var previousCombatPrepRun = _combatPrepRun;
                ReconcileCustomResources(activeCustomPlan, true);
                if (CombatPrepActive || !ReferenceEquals(previousCombatPrepRun, _combatPrepRun))
                { _timeLeft = 10f; return; }

                RunCustomFruitActivation(activeCustomPlan);
                RunCustomFocusEquipmentRefresh();
                WarnIfCustomAutomationHasIdleResources(activeCustomPlan);
                if (RunCustomRebirthAutomation(activeCustomPlan))
                {
                    _timeLeft = 10f;
                    return;
                }
            }
            catch (Exception e)
            {
                LogError("AutomationRoutine", e);
            }
            _timeLeft = 10f;
        }

        private bool RunCustomRebirthAutomation(CustomPlan activeCustomPlan)
        {
            if (CombatPrepActive) return false;
            var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null
                ? activeCustomPlan ?? CustomPlanStore.Load(Settings.CustomPlanFile) : null;
            if (!ObserveCustomRebirthReadiness(plan)) return false;
            var rebirth = new CustomPlanRebirth(plan);
            if (RunCustomPreRebirthPrepIfNeeded(plan))
                return true;

            var history = CaptureCustomRebirthHistory(DescribeCustomRebirthReason(plan));
            if (_profile.DoRebirth(rebirth))
            {
                CommitExecutionRebirthCheckpoint();
                ResetCustomPreRebirthPrep();
                ResetCustomAutomationRuntime();
                SaveCustomRebirthHistory(Settings.CustomPlanFile, history);
                return true;
            }

            return false;
        }
    }
}
