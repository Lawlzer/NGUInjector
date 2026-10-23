using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private ExecutionClaim _quickExecutionClaim;
        private SelectedSetupRequirements _reviewedExactSetupPending;

        private void ServiceReviewedExactSetup()
        {
            if (_reviewedExactSetupPending == null || _executionLifecycle != ExecutionLifecycle.Running) return;
            var setup = _reviewedExactSetupPending;
            if (setup.Claim != null) ApplyExecutionClaim(setup.Claim, setup);
            else ApplySelectedSetup(setup);
            _reviewedExactSetupPending = null;
        }

        // Durable tombstone, not deletion: stopped/superseded intent must not be
        // offered as an active acquisition after a later restart.
        private void ClearPersistedExecutionSelection(string kind)
        {
            if (string.IsNullOrEmpty(_dir)) return;
            var path = kind == "dashboard" ? StandaloneSelectionPath : ExecutionSetupPath(kind);
            if (!File.Exists(path)) return;
            var saved = JsonFiles.ParseObject(File.ReadAllText(path));
            saved["active"] = false;
            JsonFiles.WriteAtomic(path, saved.ToString(2));
            _executionExactPending?.Remove(kind);
            if (kind == "dashboard") _standaloneSelectedPolicy = _standaloneSelectionIssue = null;
        }

        private string ExecutionSetupPath(string kind)
        {
            if (string.IsNullOrEmpty(kind) || kind.Length > 64 || kind.Any(x => !char.IsLetterOrDigit(x) && x != '-'))
                throw new FormatException("Invalid execution selection kind.");
            return Path.Combine(_dir, "execution-" + kind + "-selection.json");
        }

        private void ApplyProbeSupportSelection(int[] beards, SupportDiggerLevel[] diggers)
        {
            var selection = new SupportSelection(beards, diggers);
            PreflightExactSupport(selection, 1d, false);
            var claim = BeginExecutionClaim("probe-support", Guid.NewGuid().ToString("N"), ExecutionDomain.Support, () => true);
            try
            {
                ApplyExecutionClaim(claim, new SelectedSetupRequirements { ControlsGear = false,
                    Gear = LoadoutManager.CaptureExactLoadout(), Support = selection, ChangeSupport = true,
                    Budget = 1, EnforceSupportBudget = false, Recall = GearRecallFootprint.None,
                    RouteRetirementAuthorized = () => false });
            }
            finally { EndExecutionClaim(claim, false); }
        }

        internal GearOptimizerResult ApplyManualGearSelectionIngress(GearOptimizationGoal goal, FightScoreMode mode)
        {
            if (_executionLifecycle != ExecutionLifecycle.Running) throw new InvalidOperationException("Execution is quiesced.");
            return ApplyManualGearSelection(goal, mode);
        }

        internal string RunManualPreparationIngress(bool rebirth)
        {
            if (_executionLifecycle != ExecutionLifecycle.Running) throw new InvalidOperationException("Execution is quiesced.");
            var previous = _currentExecutionCommandId;
            // Only an explicit UI/probe invocation creates an opportunity. Nested
            // external commands retain their audited identity, including on failure.
            _currentExecutionCommandId = string.IsNullOrEmpty(previous) ? Guid.NewGuid().ToString("N") : previous;
            try { return rebirth ? RunManualRebirth() : RunManualFightPrep(); }
            finally { _currentExecutionCommandId = previous; }
        }

        private void ToggleQuickExecutionClaim()
        {
            EnsureExecutionExactLoaded();
            if (_executionExactPending.ContainsKey("quick")) throw new InvalidOperationException("Review the retained Quick selection before toggling it.");
            if (_quickExecutionClaim != null)
            {
                var ending = _quickExecutionClaim;
                ClearPersistedExecutionSelection("quick");
                _quickExecutionClaim = null;
                EndExecutionClaim(ending); // Current successor only; there is no historical return target.
                Log("Quick claim ended.");
                return;
            }
            var ids = Settings.QuickLoadout ?? new int[0];
            var diggerIds = Settings.QuickDiggers ?? new int[0];
            if (ids.Length == 0 && diggerIds.Length == 0) return;
            var original = LoadoutManager.CaptureExactLoadout();
            var originalSupport = CaptureSupportState().Selection;
            var originalMaterial = original.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var instances = LoadoutPresetInstances();
            var destinations = new List<ih>(); var weapon = -5; var accessory = 10000;
            foreach (var id in ids)
            {
                var matches = instances.Where(x => x.Value != null && x.Value.id == id).ToArray();
                if (id <= 0 || matches.Length != 1)
                    throw new InvalidOperationException("Quick gear is missing or ambiguous; no replacement was selected.");
                var item = matches[0].Value; int slot;
                switch (item.type)
                {
                    case part.Head: slot = -1; break;
                    case part.Chest: slot = -2; break;
                    case part.Legs: slot = -3; break;
                    case part.Boots: slot = -4; break;
                    case part.Weapon: slot = weapon--; break;
                    case part.Accessory: slot = accessory++; break;
                    default: throw new InvalidOperationException("Quick selection contains non-equipment.");
                }
                destinations.Add(item.GetInventoryHelper(slot));
            }
            var gear = LoadoutManager.ComposeExactLoadout(original, destinations);
            var context = CaptureSupportContext();
            if (diggerIds.Distinct().Count() != diggerIds.Length) throw new InvalidOperationException("Quick diggers contain duplicate IDs.");
            var diggers = diggerIds.Select(id => {
                var current = context.Diggers.SingleOrDefault(x => x.Id == id);
                if (current == null || current.Maximum <= 0) throw new InvalidOperationException("Quick digger is unavailable.");
                return new SupportDiggerLevel(id, current.Maximum);
            }).ToArray();
            var support = new SupportSelection(context.CurrentBeards, diggerIds.Length == 0 ? context.CurrentSelection.Diggers : diggers);
            PreflightExactSupport(support, 1d, false);
            var acquisition = Guid.NewGuid().ToString("N");
            var material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var domains = (ids.Length == 0 ? ExecutionDomain.None : ExecutionDomain.Gear)
                | (diggerIds.Length == 0 ? ExecutionDomain.None : ExecutionDomain.Support);
            var settings = Settings; var definition = string.Join(",", ids) + "|" + string.Join(",", diggerIds);
            var claim = BeginExecutionClaim("quick:F8", acquisition, domains, () => ReferenceEquals(Settings, settings)
                && definition == string.Join(",", Settings.QuickLoadout ?? new int[0]) + "|" + string.Join(",", Settings.QuickDiggers ?? new int[0]));
            var setup = new SelectedSetupRequirements { Owner = claim.Owner, Acquisition = acquisition,
                Gear = gear, ControlsGear = ids.Length != 0, Support = support, ChangeSupport = diggerIds.Length != 0,
                Recall = GearRecallFootprint.None, Budget = 1d, EnforceSupportBudget = false,
                Authorized = () => ReferenceEquals(_quickExecutionClaim, claim), RouteRetirementAuthorized = () => false,
                MaterialValid = () => !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any() };
            var persisted = false;
            try
            {
                SetExecutionReturnBaseline(claim, new SelectedSetupRequirements { Gear = original, ControlsGear = ids.Length != 0,
                    Support = originalSupport, ControlsSupport = diggerIds.Length != 0, ChangeSupport = diggerIds.Length != 0,
                    Budget = 1, EnforceSupportBudget = false, Recall = GearRecallFootprint.None,
                    MaterialValid = () => !original.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != originalMaterial[i]).Any(),
                    RouteRetirementAuthorized = () => false });
                _quickExecutionClaim = claim;
                PersistExecutionSetupSelection("quick", definition, setup);
                persisted = true;
                ApplyExecutionClaim(claim, setup);
            }
            catch
            {
                if (!persisted) { _quickExecutionClaim = null; EndExecutionClaim(claim, false); }
                // A failed/partial actuator retains its claim and exact selection for
                // explicit release; it must not turn a retry into a new acquisition.
                throw;
            }
        }

        // Semantic exact-setup producer hook. Call before native mutation. Original
        // definitions/paused relationship survive independently of session evidence.
        private void PersistExecutionSetupSelection(string kind, string definition, SelectedSetupRequirements setup)
        {
            var saved = ExportExecutionSetupSelection(kind, definition, setup);
            if (kind == "quick" && _quickExecutionClaim?.ExplicitReturnBaseline != null)
                saved["returnBaseline"] = ExportExecutionSetupSelection("quick-return", definition, _quickExecutionClaim.ExplicitReturnBaseline);
            JsonFiles.WriteAtomic(ExecutionSetupPath(kind), saved.ToString(2));
        }

        private JSONObject ExportExecutionSetupSelection(string kind, string definition, SelectedSetupRequirements setup)
        {
            if (string.IsNullOrEmpty(_dir)) throw new IOException("Execution selection directory is unavailable.");
            var saved = new JSONObject(); saved["version"] = 2; saved["schema"] = "selected-exact-setup-v2"; saved["active"] = true;
            saved["kind"] = kind; saved["definition"] = definition; saved["owner"] = setup.Owner;
            saved["acquisition"] = setup.Acquisition; saved["controlsGear"] = setup.ControlsGear;
            saved["changeSupport"] = setup.ChangeSupport; saved["budget"] = setup.Budget;
            saved["enforceSupportBudget"] = setup.EnforceSupportBudget;
            saved["controlsSupport"] = setup.ControlsSupport;
            saved["recall"] = (int)setup.Recall;
            saved["retireRoute"] = setup.RetireRoute;
            saved["requiredBeards"] = LoadoutPresetNumbers((setup.RequiredBeards ?? new int[0]).Select(x => (double)x));
            var equipment = new JSONObject(); equipment["version"] = 1; equipment["materialSchema"] = "equipment-primitives-v1";
            // The descriptor codec has a GUID-shaped identity; this deterministic
            // encoding is NOT a new process/acquisition and never grants authority.
            var descriptorIdentity = Guid.TryParseExact(setup.Acquisition, "N", out _) ? setup.Acquisition
                : JsonFiles.ComputeRevision(System.Text.Encoding.UTF8.GetBytes(setup.Acquisition)).Substring(0, 32);
            equipment["acquisition"] = descriptorIdentity; var rows = new JSONArray();
            foreach (var item in setup.Gear)
            {
                var row = new JSONObject(); row["targetSlot"] = item.slot; row["sourceSlotHint"] = item.slot;
                row["id"] = item.id; row["material"] = item.id == 0 ? "null" : LoadoutPresetPrimitiveKey(item.equipment); rows.Add(row);
            }
            equipment["equipment"] = rows; saved["equipment"] = equipment;
            saved["beards"] = LoadoutPresetNumbers(setup.Support.Beards.Select(x => (double)x));
            var diggers = new JSONArray();
            foreach (var d in setup.Support.Diggers)
            {
                var row = new JSONObject(); row["id"] = d.Id; row["level"] = d.Level.ToString(CultureInfo.InvariantCulture); diggers.Add(row);
            }
            saved["diggers"] = diggers; saved["plan"] = Settings.CustomPlanFile ?? "";
            saved["pausedPlan"] = Settings.UseCustomRebirthPlan;
            saved["stageEntry"] = ExecutionPersistenceEntry ?? "";
            saved["rebirth"] = Character.stats.rebirthNumber.ToString(CultureInfo.InvariantCulture);
            saved["origin"] = Character.totalPlaytime.totalseconds - Character.rebirthTime.totalseconds;
            saved["elapsed"] = Character.rebirthTime.totalseconds; saved["total"] = Character.totalPlaytime.totalseconds;
            var commitments = new JSONObject();
            if (kind == "focus")
            {
                commitments["goal"] = _customRequestedFocusGoal?.ToString() ?? definition;
                commitments["augmentIndex"] = _customFocusAugmentIndex;
                commitments["augmentUsesUpgrade"] = _customFocusAugmentUsesUpgrade;
                commitments["ritualIndex"] = _customFocusBloodRitualIndex;
                commitments["energyNguWeights"] = LoadoutPresetNumbers((_customFocusEnergyNguWeights ?? Settings.EnergyNguWeights).Select(x => (double)x));
                commitments["magicNguWeights"] = LoadoutPresetNumbers((_customFocusMagicNguWeights ?? Settings.MagicNguWeights).Select(x => (double)x));
                commitments["os"] = (int)Character.wandoos98.os;
                var targets = new JSONArray(); foreach (var target in Character.advancedTraining.levelTarget) targets.Add(target.ToString(CultureInfo.InvariantCulture));
                commitments["atTargets"] = targets;
                commitments["resourceDomains"] = (int)setup.Recall & 7;
            }
            else if (kind == "fight-prep") commitments = ExportFightPreparationCommitment();
            saved["commitments"] = commitments;
            return saved;
        }
    }
}
