using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal sealed class CustomEquipmentSetup
        {
            internal CustomPlanStage Stage;
            internal int[] Beards;
            internal int[] RequiredBeards;
            internal DiggerManager.DiggerRequest[] Diggers;
        }

        private string _customEquipmentOwner;
        private SelectedSetupRequirements _customSupportIntent;
        private DateTime _customEquipmentLastDiggersAt = DateTime.MinValue;
        private DateTime _customSupportAcceptedAt;
        private bool _customSupportRotates;
        private bool _customSupportPending;
        private string _customSupportObserved;
        private string _customSupportPolicyKey;
        private LoadoutManager.SnapshotEpoch _customSupportEpoch;
        private bool _manualAdventureSetupActive;
        private bool _globalPresetManualAdventureActive;

        private bool ManualAdventureOwnsEquipment => _manualTitanActive || _manualAdventureSetupActive
            && (Settings.ManualAdventureEnabled || Settings.CustomAdventureEnabled || _globalPresetManualAdventureActive);

        internal static CustomEquipmentSetup SetupForFocus(CustomFocusGoal goal)
        {
            return SetupForStage(FocusSetupStage(goal), goal == CustomFocusGoal.Fight, goal == CustomFocusGoal.ManualTitans);
        }

        private static CustomPlanStage FocusSetupStage(CustomFocusGoal goal)
        {
            var definition = ActivityPolicy.Current.FocusStageDefinition(goal.ToString());
            return new CustomPlanStage
            {
                GearGoal = (int) (GearOptimizationGoal) definition.GearGoal,
                EnergyFocus = (int) (CustomStageFocus) definition.EnergyFocus,
                MagicFocus = (int) (CustomStageFocus) definition.MagicFocus,
                EnergyPriorities = definition.EnergyPriorities,
                MagicPriorities = definition.MagicPriorities
            };
        }

        internal static CustomEquipmentSetup SetupForManualAdventure()
        {
            return SetupForStage(new CustomPlanStage {GearGoal = (int) GearOptimizationGoal.ManualAdventure}, manualAdventure: true);
        }

        internal static CustomEquipmentSetup SetupForStage(CustomPlanStage stage, bool bossFight = false, bool manualTitans = false, bool manualAdventure = false)
        {
            var energy = SetupPrimaryPriority(stage, true);
            var magic = SetupPrimaryPriority(stage, false);
            // Gear selection is independent of resource intent (e.g. Wandoos in Fight gear).
            bossFight |= stage.GearGoal == (int) GearOptimizationGoal.Fight && energy == null && magic == null && !manualTitans;
            var support = ActivityPolicy.Current.BuildSupport(energy, magic, bossFight, manualTitans, manualAdventure);
            return new CustomEquipmentSetup {Stage = stage, Beards = support.Beards,
                RequiredBeards = bossFight ? new[] { BeardFuManchu } : new int[0],
                Diggers = support.Diggers.Select(request => Digger(request.Id, request.Weight, request.Name)).ToArray()};
        }

        private static string SetupPrimaryPriority(CustomPlanStage stage, bool energy)
        {
            return ActivityPolicy.Current.PrimaryPriority(energy ? stage.EnergyPriorities : stage.MagicPriorities,
                energy ? stage.EnergyFocus : stage.MagicFocus, energy);
        }

        private string ApplyCustomEquipmentSupport(CustomEquipmentSetup setup, string owner, DateTime now)
        {
            if (_customSupportEpoch != null && !_customSupportEpoch.Matches())
                throw new SelectedSetupAuthorityException("support", "Retained equipment support needs current-session review.");
            if (_customSupportObserved != null && _customSupportObserved != CaptureSupportState().StableKey)
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "support", "Retained equipment support changed externally; review current setup.");
                throw new SelectedSetupAuthorityException("support", "Retained equipment support changed externally.");
            }
            _customSupportObserved = CaptureSupportState().StableKey;
            if (_customEquipmentOwner != owner)
            {
                ResetCustomEquipmentSetup();
                _customEquipmentOwner = owner;
            }

            var policyKey = string.Join(",", setup.Beards) + ":" + string.Join(",", setup.RequiredBeards ?? new int[0])
                + ":" + string.Join(",", setup.Diggers.Select(x => x.Id + "=" + x.Weight));
            if (_customSupportIntent == null || _customSupportIntent.Owner != owner || policyKey != _customSupportPolicyKey
                || !_customSupportPending && (now - _customEquipmentLastDiggersAt).TotalSeconds >= 60d)
            {
                var epoch = new LoadoutManager.SnapshotEpoch();
                _customSupportEpoch = epoch;
                _customSupportPolicyKey = policyKey;
                _customSupportAcceptedAt = now;
                _customSupportRotates = (now - _customLastBeardRotation).TotalSeconds >= 60d;
                var beards = BeardManager.SelectBeards(ResolveCustomBeardRequirements(setup.Beards), _customSupportRotates, setup.RequiredBeards);
                _customSupportIntent = new SelectedSetupRequirements { Owner = owner, Acquisition = Guid.NewGuid().ToString("N"),
                    ControlsGear = false, Support = SelectFocusSupport(setup, beards), ChangeSupport = true, RequiredBeards = setup.RequiredBeards,
                    Budget = ActivityPolicy.Current.DiggerBudgetFraction, Authorized = () => _customSupportEpoch.Matches() && _customEquipmentOwner == owner,
                    RouteRetirementAuthorized = () => Character.adventure.zone == -1 };
                _customSupportPending = true;
            }
            BeginPresetSupportAcquisition(owner, _customSupportIntent.Acquisition);
            ApplySelectedSetup(_customSupportIntent);
            _customEquipmentLastDiggersAt = _customSupportAcceptedAt;
            _customSupportPending = false;
            if (_customSupportRotates) _customLastBeardRotation = _customSupportAcceptedAt;
            return "support: " + _customSupportIntent.Support.StableKey;
        }

        private void RunCustomFocusEquipmentRefresh()
        {
            if (!_customAppliedFocusGoal.HasValue || Settings.UseCustomRebirthPlan || ManualAdventureOwnsEquipment
                || CustomQuickResourceRefreshBlocked(null))
                return;

            var goal = _customAppliedFocusGoal.Value;
            RefreshSelectedFocusSupport(goal);
        }

        private void ResetCustomEquipmentSetup()
        {
            _customEquipmentOwner = null;
        }
    }
}
