using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private DateTime _customLastBeardRotation = DateTime.MinValue;
        private string _customBeardRequirements;
        private string _customBeardAppliedState;
        private SelectedSetupRequirements _customBeardDesired;
        private SelectedSetupRequirements _customBeardRetained;
        private string _customBeardSelectedKey;
        private string _customBeardDesiredKey;
        private bool _customBeardDesiredRotate;
        private DateTime _customBeardAcceptedAt;
        private string _customBeardObservedSupport;
        private LoadoutManager.SnapshotEpoch _customBeardEpoch;

        private void InvalidateCustomBeardRequirements()
        {
            _customBeardRequirements = null;
            _customBeardAppliedState = null;
        }

        private static string CustomBeardObservedState()
        {
            var beards = Character?.beards;
            if (beards?.beards == null || beards.activeBeards == null || Character.allBeards == null || beards.disabled ||
                Character.buttons?.beards?.interactable != true ||
                beards.activeBeards.Any(id => id < 0 || id >= beards.beards.Count || beards.beards[id] == null))
                return null;
            return Character.allBeards.capBeards() + ":" + beards.disabled + ":" +
                (Character.buttons?.beards?.interactable == true) + ":" +
                ((Character.challenges?.trollChallenge?.curCompletions ?? 0) >= 7) + ":" +
                string.Join(",", beards.activeBeards.OrderBy(id => id).Select(id => id.ToString()).ToArray()) + ":" +
                string.Join(",", beards.beards.Select((beard, id) => id + "=" +
                    (beard == null ? "missing" : beard.active.ToString())).ToArray());
        }

        private void RunCustomBeardAutomation(CustomPlan plan)
        {
            try
            {
                if (Settings == null || Character?.rebirthTime == null)
                    return;
                if (CustomBossFightActive()) return;

                if (ManualAdventureOverlayActive && ManualAdventureSupportOverridesActive) return;
                if (!Settings.ManualAdventureEnabled) ResetManualAdventureOverlay();

                if (GlobalPresetOwnsActivity(plan) && !BossPushRunning && !CustomBossFightActive()
                    && !_manualRebirthPending && !_customPreRebirthPrepRunning && !CombatPrepActive
                    && !_customTitanGoldActive && !_customTitanFightActive && (!ManualAdventureOwnsEquipment || ManualAdventureOverlayActive))
                {
                    RotateGlobalPresetBeards(DateTime.UtcNow);
                    return;
                }

                if (!RefreshInlineStageSupport(plan)) ApplyCustomBeards(CustomActivityBeards(plan), DateTime.UtcNow);
            }
            catch (Exception error)
            {
                LogError("Custom beard rotation", error);
            }
        }

        private IEnumerable<int> CustomActivityBeards(CustomPlan plan)
        {
            IEnumerable<int> required = new int[0];
            // Fight/rebirth prep temporarily owns activity, not the paused stage/focus.
            if (!BossPushRunning && !CustomBossFightActive() && !_manualRebirthPending && !_customPreRebirthPrepRunning)
            {
                if (plan != null && Settings.UseCustomRebirthPlan && plan.StageSchedulerEnabled)
                {
                    // Read-only snapshot: a beard tick must not advance the stage scheduler.
                    var index = CustomStageAllocator.GetSchedulerSnapshot(plan, Character.rebirthTime.totalseconds)?.ActiveStageIndex ?? -1;
                    if (index >= 0 && index < plan.Stages.Length)
                        required = SetupForStage(plan.Stages[index]).Beards;
                }
                else if (_customAppliedFocusGoal.HasValue)
                    required = SetupForFocus(_customAppliedFocusGoal.Value).Beards;
            }
            return Settings.AutoRebirth && CustomRebirthReady(plan) ? new[] {BeardReverseHitler}.Concat(required) : required;
        }

        private void EnsureCustomBeards(int requiredBeard)
        {
            var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null ? LoadRuntimeCustomPlan() : null;
            // Combat entry augments the same activity policy used by the global tick.
            ApplyCustomBeards(CustomActivityBeards(plan).Concat(new[] {requiredBeard}), DateTime.UtcNow, new[] { requiredBeard });
        }

        private int[] ResolveCustomBeardRequirements(IEnumerable<int> required)
        {
            var requirements = new List<int>();
            if (BossPushRunning || CustomBossFightActive())
                requirements.Add(BeardFuManchu);
            if (_manualRebirthPending || _customPreRebirthPrepRunning)
                requirements.Add(BeardReverseHitler);
            if (_customTitanGoldActive || _customTitanFightActive ||
                Character?.adventure != null && ZoneHelpers.ZoneIsTitan(Character.adventure.zone))
                requirements.Add(BeardGold);
            // A lower-priority or unmanaged lane can still be training Wandoos.
            if (!BossPushRunning && !CustomBossFightActive() && !_manualRebirthPending && !_customPreRebirthPrepRunning &&
                Character?.wandoos98 != null && !Character.wandoos98.disabled &&
                (Character.wandoos98.wandoosEnergy > 0 || Character.wandoos98.wandoosMagic > 0))
                requirements.Add(BeardLadybeard);
            requirements.AddRange(required ?? new int[0]);
            return requirements.Distinct().ToArray();
        }

        private static void RequireCustomHardBeards(int[] required)
        {
            if (required.Length == 0) return;
            var c = Character;
            var beards = c?.beards;
            // Observe beard prerequisites without claiming or replanning coupled diggers.
            if (CustomBeardObservedState() == null || beards.activeBeards.Count > c.allBeards.capBeards()
                || !beards.activeBeards.OrderBy(id => id).SequenceEqual(beards.beards.Select((beard, id) => new { beard, id })
                    .Where(x => x.beard != null && x.beard.active).Select(x => x.id))
                || required.Any(id => id < 0 || id >= 7 || id >= beards.beards.Count
                    || !beards.activeBeards.Contains(id) || beards.beards[id]?.active != true
                    || id == BeardGold && (c.challenges?.trollChallenge?.curCompletions ?? 0) < 7))
                throw new InvalidOperationException("Current support does not verify the required beards.");
        }

        private string ApplyCustomBeards(IEnumerable<int> required, DateTime now, int[] hardRequired = null)
        {
            if (_customBeardEpoch != null && !_customBeardEpoch.Matches())
                throw new SelectedSetupAuthorityException("support", "Retained activity support needs current-session review.");
            hardRequired = (hardRequired ?? new int[0]).Distinct().OrderBy(id => id).ToArray();
            if (CombatPrepActive)
            {
                RequireCustomHardBeards(hardRequired);
                return "beards frozen by combat prep";
            }
            var ids = ResolveCustomBeardRequirements(required);
            // Core beards always lead in the manager; only competing optional order affects selection.
            var core = new HashSet<int>(ActivityPolicy.Current.CoreBeards());
            var key = string.Join(",", ids.Where(id => !core.Contains(id))
                .Select(id => id.ToString()).ToArray()) + "|" + string.Join(",", hardRequired);
            var rotate = (now - _customLastBeardRotation).TotalSeconds >= 60d;
            var observed = CustomBeardObservedState();
            if (_customBeardObservedSupport != null && _customBeardObservedSupport != CaptureSupportState().StableKey)
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "support", "Activity coupled support changed externally; review current setup.");
                throw new SelectedSetupAuthorityException("support", "Activity coupled support changed externally.");
            }
            if (!rotate && key == _customBeardRequirements && observed != null && observed == _customBeardAppliedState)
            {
                RequireCustomHardBeards(hardRequired);
                return string.Empty;
            }

            if (ExecutionOwnsDomain(ExecutionDomain.Support))
            {
                RequireCustomHardBeards(hardRequired);
                return "beards controlled by current support claim";
            }
            if (_customBeardAppliedState != null && observed != null && _customBeardAppliedState != observed)
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "support", "Activity support changed externally; review current setup.");
                throw new SelectedSetupAuthorityException("support", "Activity support changed externally.");
            }
            if (_customBeardDesired == null || _customBeardDesiredKey != key)
            {
                if (!rotate && _customBeardRetained != null && _customBeardSelectedKey == key)
                {
                    _customBeardDesired = _customBeardRetained;
                    _customBeardDesiredKey = key;
                    _customBeardDesiredRotate = false;
                }
                else
                {
                    // Replacing a pending selection does not renew its accepted minute.
                    var acceptedAt = _customBeardDesired != null && _customBeardDesiredRotate ? _customBeardAcceptedAt : now;
                    var selected = BeardManager.SelectBeards(ids, rotate, hardRequired);
                    var current = CaptureSupportState().Selection;
                    _customBeardDesiredKey = key; _customBeardDesiredRotate = rotate; _customBeardAcceptedAt = acceptedAt;
                    _customBeardEpoch = new LoadoutManager.SnapshotEpoch();
                    _customBeardDesired = new SelectedSetupRequirements { Owner = "activity-beards", ControlsGear = false,
                        Acquisition = Guid.NewGuid().ToString("N"), Support = new SupportSelection(selected, current.Diggers),
                        ChangeSupport = true, EnforceSupportBudget = false, RequiredBeards = hardRequired,
                        Authorized = () => _customBeardEpoch.Matches() && !CombatPrepActive && !ExecutionOwnsDomain(ExecutionDomain.Support),
                        ObserveProgress = (gear, support) => _customBeardAppliedState = CustomBeardObservedState(),
                        RouteRetirementAuthorized = () => Character.adventure.zone == -1 };
                }
            }
            var desired = _customBeardDesired;
            _customBeardObservedSupport = CaptureSupportState().StableKey;
            BeginPresetSupportAcquisition(desired.Owner, desired.Acquisition);
            ApplySelectedSetup(desired);
            RequireCustomHardBeards(hardRequired);
            var message = "beards: " + string.Join(",", desired.Support.Beards);
            _customBeardRequirements = key;
            _customBeardAppliedState = CustomBeardObservedState();
            // Requirement changes do not postpone the independent minute cadence.
            if (_customBeardDesiredRotate)
            {
                _customLastBeardRotation = _customBeardAcceptedAt;
                Log("Minute beard rotation: " + message);
            }
            _customBeardRetained = desired;
            _customBeardSelectedKey = key;
            _customBeardDesired = null;
            return message;
        }
    }
}
