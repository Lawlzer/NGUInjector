using System;
using System.Linq;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal SelectedSetupRequirements AcquireFiniteActionSetup(ExecutionClaim claim, ih[] gear, SupportSelection support,
            bool controlsGear, bool changeSupport)
        {
            var setup = CreateFiniteActionSetup(claim, gear, support, controlsGear, changeSupport);
            ApplyExecutionClaim(claim, setup);
            return setup;
        }

        private SelectedSetupRequirements CreateFiniteActionSetup(ExecutionClaim claim, ih[] gear, SupportSelection support,
            bool controlsGear, bool changeSupport, int[] requiredBeards = null)
        {
            var material = gear?.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var baselineRoute = CaptureExecutionBaselineRouteRetirement(claim);
            var setup = new SelectedSetupRequirements { Owner = claim.Owner, Acquisition = claim.Acquisition,
                Gear = gear, Support = support, ControlsGear = controlsGear, ControlsSupport = changeSupport,
                RequiredBeards = requiredBeards ?? new int[0], ChangeSupport = changeSupport, Recall = GearRecallFootprint.None, Budget = 1,
                RetireRoute = Character.adventure.zone != -1,
                Authorized = () => ExecutionClaimCurrent(claim),
                RouteRetirementAuthorized = () => Character.adventure.zone == -1 || baselineRoute(),
                MaterialValid = () => gear == null || !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any() };
            return setup;
        }

        internal bool FiniteActionSetupVerified(ExecutionClaim claim, SelectedSetupRequirements setup)
        {
            return ExecutionClaimCurrent(claim) && ExecutionClaimSetupOwned(claim)
                && (setup.MaterialValid == null || setup.MaterialValid())
                && (!setup.ControlsGear || LoadoutManager.ExactLoadoutMatches(setup.Gear))
                && CaptureSupportState().Selection.StableKey == setup.Support.StableKey
                && (!setup.ChangeSupport || SupportProjection.Evaluate(CaptureSupportContext(), setup.Support, setup.Budget, setup.EnforceSupportBudget).Feasible)
                && Character.adventure.zone == -1 && !Character.bossController.isFighting && !Character.bossController.nukeBoss;
        }

        internal ih[] SelectConfiguredActionGear(int[] ids)
        {
            var current = LoadoutManager.CaptureExactLoadout();
            var owned = current.Concat(Character.inventory.GetConvertedInventory().Where(x => x.slot < Controller.curSpaces())).ToArray();
            var selected = ids.Select(id => {
                var matches = owned.Where(x => x.id == id && x.equipment.isEquipment()).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Configured action equipment is missing or ambiguous: " + id);
                return matches[0];
            }).ToArray();
            var weapon = -5; var accessory = 10000;
            return LoadoutManager.ComposeExactLoadout(current, selected.Select(x => new ih { equipment = x.equipment, id = x.id,
                level = x.level, name = x.name, slot = x.equipment.type == part.Head ? -1 : x.equipment.type == part.Chest ? -2
                    : x.equipment.type == part.Legs ? -3 : x.equipment.type == part.Boots ? -4
                    : x.equipment.type == part.Weapon ? weapon-- : accessory++ }));
        }

        internal SupportSelection SelectActionSupport(int[] beards, params int[] diggers)
        {
            var context = CaptureSupportContext();
            var plan = SupportProjection.Plan(context, beards, diggers.Select(id => new DiggerBudgetRequest {
                Id = id, Weight = 1, Maximum = context.Diggers.Single(x => x.Id == id).Maximum }).ToArray(), 1);
            if (!plan.Feasible) throw new InvalidOperationException(plan.Reason);
            return plan.Selection;
        }

        private Func<bool, bool> CaptureFiniteActionBoundary(FiniteAction action)
        {
            var model = Character; var epoch = new LoadoutManager.SnapshotEpoch();
            var objects = ResourceWriter.CaptureResourceContext(model);
            var allocations = ResourceWriter.CaptureAllocations(model);
            var coupled = (action.Footprint & (ExecutionDomain.Gear | ExecutionDomain.Support)) != 0;
            var lanes = coupled ? 7 : (int)action.Footprint & 7;
            var gear = (action.Footprint & ExecutionDomain.Gear) != 0 ? LoadoutManager.CaptureExactLoadout() : null;
            var material = gear?.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var support = (action.Footprint & ExecutionDomain.Support) != 0 ? CaptureSupportState().StableKey : null;
            var baseGold = model.machine.realBaseGold;
            var nativeSettings = model.settings;
            var recorded = false;
            return after => {
                if (!ReferenceEquals(Character, model) || !epoch.Matches()) return false;
                var currentObjects = ResourceWriter.CaptureResourceContext(model);
                var contextOwned = currentObjects.Length == objects.Length && !currentObjects.Where((x, i) => !ReferenceEquals(x, objects[i])).Any();
                var observed = ResourceWriter.CaptureAllocations(model);
                var foreignLanes = allocations.Where(pair => (lanes & (1 << ResourceWriter.Lane(pair.Key))) != 0
                    && (pair.Value == null ? observed[pair.Key] != null : observed[pair.Key] == null || !pair.Value.SequenceEqual(observed[pair.Key])))
                    .Aggregate(0, (mask, pair) => mask | 1 << ResourceWriter.Lane(pair.Key));
                if (!contextOwned) foreignLanes |= lanes;
                var incomeOwned = model.machine.realBaseGold == baseGold;
                var gearOwned = gear == null || LoadoutManager.ExactLoadoutMatches(gear)
                    && !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment)
                        != (after && action.ChangesEquipmentMaterial ? action.ExpectedEquipmentMaterial?.Invoke(x.equipment) ?? material[i] : material[i])).Any();
                var supportOwned = support == null || CaptureSupportState().StableKey == support;
                var settingsOwned = (action.Footprint & ExecutionDomain.NativeSettings) == 0 || ReferenceEquals(model.settings, nativeSettings);
                if (foreignLanes == 0 && gearOwned && supportOwned && incomeOwned && settingsOwned) return true;
                if (!recorded)
                {
                    recorded = true;
                    if (foreignLanes != 0) _resourceSetupExecutor.Application.RecordUnresolved(model, new ResourceMutationResult {
                        Disposition = MutationDisposition.ForeignConflict, AffectedLanes = foreignLanes,
                        Reason = "Native allocated bars/model changed across finite action " + action.Identity.Kind + "." });
                    if (!gearOwned) RecordSelectedSetupConflict(model, model.stats.rebirthNumber, "gear", "Equipment changed across finite action " + action.Identity.Kind + ".");
                    if (!supportOwned || !incomeOwned) RecordSelectedSetupConflict(model, model.stats.rebirthNumber, "support", "Support/income changed across finite action " + action.Identity.Kind + ".");
                }
                return false;
            };
        }
    }
}
