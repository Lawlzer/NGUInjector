using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private string StandaloneSelectionPath => string.IsNullOrEmpty(_dir) ? null : Path.Combine(_dir, "execution-selection.json");
        private Dictionary<string, JSONObject> _executionExactPending;
        private bool _executionExactLoaded;
        private string _executionRoutePath;

        private Equipment[] CurrentExecutionEquipmentInstances() => LoadoutPresetInstances().OrderBy(x => x.Key)
            .Where(x => x.Value != null && x.Value.id > 0).Select(x => x.Value).ToArray();

        private void ValidateReviewedRouteGear(JSONNode rows, string acquisition)
        {
            var descriptor = new JSONObject(); descriptor["version"] = 1; descriptor["materialSchema"] = "equipment-primitives-v1";
            descriptor["acquisition"] = acquisition; var equipment = new JSONArray();
            foreach (var item in rows.Children)
            {
                var row = new JSONObject(); row["targetSlot"] = item["slot"]; row["sourceSlotHint"] = item["slot"];
                row["id"] = item["id"]; row["material"] = item["id"].AsInt == 0 ? "null" : item["material"].Value; equipment.Add(row);
            }
            descriptor["equipment"] = equipment;
            ResolveGlobalPresetEquipmentSelection(descriptor, acquisition, LoadoutManager.CaptureExactLoadout().Select(x => x.slot).ToArray(), LoadoutPresetInstances());
        }

        private JSONObject PreviewRouteExecutionRebind()
        {
            var saved = _importedRouteCommitments;
            if (saved == null || _executionRoutePath == null) throw new InvalidOperationException("No route commitment requires review.");
            foreach (var row in saved["processes"].Children)
            {
                ValidateReviewedRouteGear(row["gear"], row["acquisition"].Value);
                var support = new SupportSelection(row["beards"].Children.Select(x => SelectionInt(x, 0, 6)).ToArray(),
                    row["diggers"].Children.Select(x => new SupportDiggerLevel(SelectionInt(x["id"], 0, 11), SelectionLong(x["level"]))).ToArray());
                PreflightExactSupport(support, 1, false);
            }
            var review = new ExecutionRebindReview { Kind = "routes", Path = _executionRoutePath,
                Token = Guid.NewGuid().ToString("N"), Revision = JsonFiles.ComputeRevision(File.ReadAllBytes(_executionRoutePath)),
                Process = JsonFiles.ParseObject(saved.ToString()), Owner = "route-review", Entry = ExecutionPersistenceEntry,
                ProcessInstances = CurrentExecutionEquipmentInstances(),
                Material = ExactExecutionMaterialKey(), Native = ExecutionRebindNativeKey(), Permissions = ExecutionRebindPermissions(),
                Conflict = SelectedSetupConflictVersion(Character), Expires = LoadoutPresetClock + 120 };
            _executionRebindReview = review;
            var domains = ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.NativeSettings | ExecutionDomain.Inventory;
            review.Grant = ResolveExecutionGrant(review.Owner, review.Revision, domains,
                () => ReferenceEquals(_executionRebindReview, review) && review.Entry == ExecutionPersistenceEntry && review.Conflict == SelectedSetupConflictVersion(Character));
            if (!ValidateExecutionGrant(review.Grant, domains)) throw new InvalidOperationException("Route review authority is unavailable.");
            var result = new JSONObject(); result["token"] = review.Token; result["checkpointRevision"] = review.Revision;
            result["owner"] = "Route processes"; result["commitments"] = saved; return result;
        }

        private JSONObject ExportAcceptedExecutionProcess()
        {
            var process = new JSONObject();
            if (_combatPrepRun != null)
            {
                process["id"] = _combatPrepRun.Id; process["acquisition"] = _combatPrepRun.Setup?.Acquisition ?? _combatPrepRun.Id;
                process["entry"] = _combatPrepRun.OwnerEntry ?? "manual";
                process["duration"] = _combatPrepRun.Duration; process["deadlineRebirthSeconds"] = _combatPrepRun.DeadlineRebirthSeconds;
            }
            else if (FightPreparationIdentity != null) { process["id"] = FightPreparationIdentity; process["acquisition"] = FightPreparationClaim?.Acquisition ?? ""; }
            else { process["id"] = _currentExecutionCommandId; process["acquisition"] = _focusSetupSelection?.Desired.Acquisition ?? _globalPresetReceipt?.Policy.Acquisition ?? _currentExecutionCommandId; }
            return process;
        }

        private void EnsureExecutionExactLoaded()
        {
            if (_executionExactLoaded || string.IsNullOrEmpty(_dir)) return;
            _executionExactLoaded = true;
            _executionExactPending = new Dictionary<string, JSONObject>(StringComparer.Ordinal);
            foreach (var name in new[] { "lifecycle-route-commitments-v1.json", "route-execution-v1.json" })
            {
                var path = Path.Combine(_dir, name);
                if (!File.Exists(path)) continue;
                var routes = JsonFiles.ParseObject(File.ReadAllText(path));
                if (routes["active"].IsBoolean && !routes["active"].AsBool) continue;
                if (routes["processes"].Count == 0 && routes["manual"].Count == 0 && routes["towerDeaths"].AsInt == 0) continue;
                ImportRouteExecutionCommitments(routes); _executionRoutePath = path; break;
            }
            foreach (var path in Directory.EnumerateFiles(_dir, "execution-*-selection.json"))
            {
                var kind = Path.GetFileName(path).Substring(10);
                kind = kind.Substring(0, kind.Length - "-selection.json".Length);
                try
                {
                    var saved = JsonFiles.ParseObject(File.ReadAllText(path));
                    if (saved["active"].IsBoolean && !saved["active"].AsBool) continue;
                    _executionExactPending[kind] = saved;
                }
                catch { _executionExactPending[kind] = new JSONObject(); }
            }
        }

        private SelectedSetupRequirements ImportExactExecutionSelection(JSONObject saved)
        {
            if (!EquipmentDescriptorInteger(saved["version"], 1, 2)
                || saved["schema"].Value != "selected-exact-setup-v" + saved["version"].AsInt
                || !saved["owner"].IsString || string.IsNullOrEmpty(saved["owner"].Value)
                || !saved["acquisition"].IsString || string.IsNullOrEmpty(saved["acquisition"].Value)
                || !saved["definition"].IsString || !saved["controlsGear"].IsBoolean || !saved["changeSupport"].IsBoolean
                || !saved["enforceSupportBudget"].IsBoolean || !FiniteCheckpointNumber(saved["budget"], true) || saved["budget"].AsDouble > 1
                || saved["rebirth"].Value != Character.stats.rebirthNumber.ToString(CultureInfo.InvariantCulture)
                || !FiniteCheckpointNumber(saved["origin"], false)
                || Math.Abs(saved["origin"].AsDouble - (Character.totalPlaytime.totalseconds - Character.rebirthTime.totalseconds)) > 5
                || saved["plan"].Value != Settings.CustomPlanFile)
                throw new InvalidOperationException("Exact selected commitment does not match this save/plan or schema.");
            if (saved["stageEntry"].Value.Length > 0 && _protectedScheduler == null && CustomPlanStore != null
                && File.Exists(ProtectedCheckpointForPlan(Settings.CustomPlanFile)))
                InitializeProtectedStageProgress(CustomPlanStore.Load(Settings.CustomPlanFile));
            if (saved["stageEntry"].Value.Length > 0 && saved["stageEntry"].Value != (ExecutionPersistenceEntry ?? ""))
                throw new InvalidOperationException("Paused plan entry changed; no acquired setup was substituted.");
            var slots = LoadoutManager.CaptureExactLoadout().Select(x => x.slot).ToArray();
            var descriptor = saved["equipment"];
            var identity = saved["version"].AsInt == 1 ? saved["acquisition"].Value
                : Guid.TryParseExact(saved["acquisition"].Value, "N", out _) ? saved["acquisition"].Value
                    : JsonFiles.ComputeRevision(System.Text.Encoding.UTF8.GetBytes(saved["acquisition"].Value)).Substring(0, 32);
            var items = ResolveGlobalPresetEquipmentSelection(descriptor, identity, slots, LoadoutPresetInstances());
            var gear = items.Select((item, i) => (item ?? new Equipment()).GetInventoryHelper(slots[i])).ToArray();
            var material = items.Select(LoadoutPresetPrimitiveKey).ToArray();
            var beards = saved["beards"].Children.Select(x => SelectionInt(x, 0, 6)).ToArray();
            if (!saved["beards"].IsArray || !saved["diggers"].IsArray || saved["diggers"].Count > 12)
                throw new FormatException("Exact selected support is invalid.");
            var support = new SupportSelection(beards, saved["diggers"].Children.Select(x => new SupportDiggerLevel(
                SelectionInt(x["id"], 0, 11), SelectionLong(x["level"]))).ToArray());
            var evaluation = SupportProjection.Evaluate(CaptureSupportContext(), support, saved["budget"].AsDouble, saved["enforceSupportBudget"].AsBool);
            if ((saved["changeSupport"].AsBool || saved["controlsSupport"].AsBool) && !evaluation.Feasible)
                throw new InvalidOperationException(evaluation.Reason);
            var epoch = new LoadoutManager.SnapshotEpoch(); var model = Character;
            return new SelectedSetupRequirements { Owner = saved["owner"].Value, Acquisition = saved["acquisition"].Value,
                Gear = gear, ControlsGear = saved["controlsGear"].AsBool, Support = support,
                ChangeSupport = saved["changeSupport"].AsBool, ControlsSupport = saved["controlsSupport"].AsBool,
                RequiredBeards = saved["requiredBeards"].Children.Select(x => SelectionInt(x, 0, 6)).ToArray(),
                Budget = saved["budget"].AsDouble, EnforceSupportBudget = saved["enforceSupportBudget"].AsBool,
                Recall = saved["recall"].IsNumber ? (GearRecallFootprint)SelectionInt(saved["recall"], 0, 7) : GearRecallFootprint.None,
                RetireRoute = saved["retireRoute"].IsBoolean && saved["retireRoute"].AsBool,
                Authorized = () => ReferenceEquals(Character, model) && epoch.Matches(),
                MaterialValid = () => !items.Where((item, i) => LoadoutPresetPrimitiveKey(item) != material[i]).Any(),
                RouteRetirementAuthorized = () => Character.adventure.zone == -1 };
        }

        private JSONObject PreviewExactExecutionRebind(string kind)
        {
            EnsureExecutionExactLoaded();
            if (kind != null && !_executionExactPending.ContainsKey(kind) && File.Exists(ExecutionSetupPath(kind)))
            {
                var current = JsonFiles.ParseObject(File.ReadAllText(ExecutionSetupPath(kind)));
                if (!current["active"].IsBoolean || current["active"].AsBool) _executionExactPending[kind] = current;
            }
            if (kind == null) kind = _executionExactPending.Keys.OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
            if (kind == null || !_executionExactPending.TryGetValue(kind, out var saved))
                throw new InvalidOperationException("No exact acquired selection requires review.");
            var path = ExecutionSetupPath(kind); var setup = ImportExactExecutionSelection(saved);
            ValidateExactExecutionCommitments(kind, saved, setup);
            var domains = ExactExecutionReviewDomains(kind, saved, setup);
            var review = new ExecutionRebindReview { Token = Guid.NewGuid().ToString("N"), Kind = kind, Path = path,
                Revision = JsonFiles.ComputeRevision(File.ReadAllBytes(path)), Selection = saved.ToString(), Owner = setup.Owner,
                Exact = saved, Setup = setup, Material = ExactExecutionMaterialKey(), Native = ExecutionRebindNativeKey(),
                Permissions = ExecutionRebindPermissions(), Entry = ExecutionPersistenceEntry,
                Conflict = SelectedSetupConflictVersion(Character), Expires = LoadoutPresetClock + 120 };
            review.RecoveryDomains = domains;
            CaptureExecutionReviewResources(review);
            if (kind == "fight-prep") review.Process = CurrentFightPreparationCommitment(saved["commitments"] as JSONObject);
            _executionRebindReview = review;
            review.Grant = ResolveExecutionGrant(setup.Owner, setup.Acquisition, domains,
                () => ReferenceEquals(_executionRebindReview, review) && review.Entry == ExecutionPersistenceEntry && review.Conflict == SelectedSetupConflictVersion(Character));
            if (!ValidateExecutionGrant(review.Grant, domains)) { _executionRebindReview = null; throw new InvalidOperationException("Current exact-setup authority is unavailable."); }
            var result = new JSONObject(); result["token"] = review.Token; result["checkpointRevision"] = review.Revision;
            result["owner"] = setup.Owner; result["acquisition"] = setup.Acquisition; result["equipment"] = saved["equipment"];
            result["commitments"] = saved["commitments"]; result["entry"] = review.Entry ?? "";
            if (review.Process != null) result["commitments"] = review.Process;
            var support = new JSONObject(); support["beards"] = saved["beards"]; support["diggers"] = saved["diggers"]; result["support"] = support;
            WriteExecutionReviewScope(result, review);
            return result;
        }

        private ExecutionDomain ExactExecutionReviewDomains(string kind, JSONObject saved, SelectedSetupRequirements setup)
        {
            if (kind == "fight-prep" && CurrentFightPreparationCommitment(saved["commitments"] as JSONObject)["state"].Value == FiniteActionState.Committed.ToString())
                return ExecutionDomain.None;
            var domains = (setup.ControlsGear ? ExecutionDomain.Gear : 0)
                | (setup.ChangeSupport || setup.ControlsSupport ? ExecutionDomain.Support : 0);
            if (setup.ControlsGear && !LoadoutManager.ExactLoadoutMatches(setup.Gear)) domains |= (ExecutionDomain)(int)setup.Recall;
            if (setup.RetireRoute || Character.adventure.zone != -1 && (!LoadoutManager.ExactLoadoutMatches(setup.Gear)
                || setup.ChangeSupport && CaptureSupportState().Selection.StableKey != setup.Support.StableKey)) domains |= ExecutionDomain.Route;
            if (kind == "focus" && saved["commitments"]["resourceDomains"].IsNumber)
                domains |= (ExecutionDomain)SelectionInt(saved["commitments"]["resourceDomains"], 0, 7);
            return domains;
        }

        private void ValidateExactExecutionCommitments(string kind, JSONObject saved, SelectedSetupRequirements setup)
        {
            if (kind == "focus")
            {
                var commitment = saved["commitments"];
                if (!Enum.TryParse(commitment["goal"].Value, out CustomFocusGoal goal) || !SustainedFocusEnabled(goal)
                    || Settings.UseCustomRebirthPlan) throw new InvalidOperationException("Pause the plan before reviewing the original sustained focus.");
                SelectionInt(commitment["augmentIndex"], -1, 6); SelectionInt(commitment["ritualIndex"], -1, 100);
                var energy = SelectionNumbers(commitment["energyNguWeights"], Settings.EnergyNguWeights.Length);
                var magic = SelectionNumbers(commitment["magicNguWeights"], Settings.MagicNguWeights.Length);
                if (energy.Concat(magic).Any(x => x > int.MaxValue || x != Math.Floor(x))
                    || !energy.SequenceEqual(Settings.EnergyNguWeights.Select(x => (double)x))
                    || !magic.SequenceEqual(Settings.MagicNguWeights.Select(x => (double)x))
                    || commitment["os"].AsInt != (int)Character.wandoos98.os
                    || !commitment["atTargets"].Children.Select(x => x.Value).SequenceEqual(Character.advancedTraining.levelTarget.Select(x => x.ToString(CultureInfo.InvariantCulture))))
                    throw new InvalidOperationException("Acquired focus targets/OS or exclusions changed.");
            }
            else if (kind == "fight-prep")
            {
                var commitment = CurrentFightPreparationCommitment(saved["commitments"] as JSONObject);
                var original = commitment["original"];
                if (setup.Owner != commitment["owner"].Value || setup.Acquisition != commitment["acquisition"].Value
                    || setup.Acquisition != "rebirth:" + ActionNumber(Character.stats.rebirthNumber)
                    || original["plan"].Value != Settings.CustomPlanFile || original["entry"].Value != (ExecutionPersistenceEntry ?? "")
                    || !FiniteCheckpointNumber(original["started"], true) || original["started"].AsDouble > Character.rebirthTime.totalseconds
                    || FightPreparationClaim != null)
                    throw new InvalidOperationException("Original fight-preparation process/entry/time is not available for reviewed admission.");
            }
            else if (kind != "quick" && kind != "manual" && kind != "manual-gear")
                throw new InvalidOperationException("This process requires its semantic process importer.");
            if (kind == "quick" && saved["returnBaseline"].IsObject) ImportExactExecutionSelection((JSONObject)saved["returnBaseline"]);
        }

        private JSONObject CurrentFightPreparationCommitment(JSONObject saved)
        {
            if (saved == null || !EquipmentDescriptorInteger(saved["version"], 1, 1))
                throw new InvalidOperationException("Fight preparation lacks its semantic process commitment; no process was fabricated.");
            var id = new FiniteActionIdentity(saved["kind"].Value, saved["owner"].Value, saved["acquisition"].Value,
                saved["entry"].Value, saved["opportunity"].Value);
            var record = FiniteActions.Find(id);
            if (id.Kind != "fight-preparation" || record == null || record.Before != saved["original"].ToString())
                throw new InvalidOperationException("Original fight-preparation identity or history changed.");
            if (record.Dirty) throw new InvalidOperationException("Fight-preparation history is waiting for durable storage; native proof was retained.");
            // The ledger can be newer than the pre-effect selection checkpoint. Its
            // durable stage is evidence, not permission to rerun a preparation effect.
            var current = JsonFiles.ParseObject(saved.ToString());
            current["stage"] = record.After; current["state"] = record.State.ToString();
            var effectHistory = new JSONArray();
            if (current["original"]["consumePowerFruits"].AsBool && record.State != FiniteActionState.Committed)
            {
                var originalHarvests = current["original"]["fruitHarvests"];
                if (!originalHarvests.IsObject) throw new InvalidOperationException("Original preparation effect opportunities are unavailable; no fruit was repeated.");
                foreach (var fruitId in new[] { YggdrasilManager.FruitOfPowerAlphaId, YggdrasilManager.FruitOfPowerBetaId })
                {
                    if (fruitId < 0 || fruitId >= Character.yggdrasil.fruits.Count) throw new InvalidOperationException("Original preparation fruit is unavailable.");
                    var harvests = SelectionInt(originalHarvests[ActionNumber(fruitId)], 0, int.MaxValue);
                    var observed = Character.yggdrasil.fruits[fruitId].harvests;
                    var actionId = ActionIdentity("fruit-harvest:" + fruitId, id.Owner, PreparationFruitOpportunity(id, fruitId));
                    var action = FiniteActions.Find(actionId);
                    if (action?.Dirty == true || action?.State == FiniteActionState.Unknown || action?.State == FiniteActionState.Invocation)
                        throw new InvalidOperationException("Original preparation fruit effect is unresolved; no effect or process was repeated.");
                    var history = new JSONObject(); history["id"] = fruitId; history["opportunity"] = actionId.Opportunity;
                    history["state"] = action?.State.ToString() ?? "uninvoked"; history["observedHarvests"] = observed.ToString(CultureInfo.InvariantCulture);
                    history["before"] = action?.Before ?? ""; history["after"] = action?.After ?? ""; effectHistory.Add(history);
                    if (observed == harvests && action?.State != FiniteActionState.Committed) continue;
                    if ((long)observed < (long)harvests + 1 || action?.State != FiniteActionState.Committed)
                        throw new InvalidOperationException("Preparation fruit history cannot safely resume this opportunity; no new fruit effect was admitted.");
                }
            }
            current["observedEffectHistory"] = effectHistory;
            return current;
        }

        private string ExactExecutionMaterialKey() => string.Join("|", LoadoutPresetInstances().OrderBy(x => x.Key)
            .Select(x => x.Key + ":" + LoadoutPresetPrimitiveKey(x.Value)));

        private string ApplyExactExecutionRebind(ExecutionRebindReview review)
        {
            if (JsonFiles.ComputeRevision(File.ReadAllBytes(review.Path)) != review.Revision || ExactExecutionMaterialKey() != review.Material
                || ExecutionRebindNativeKey() != review.Native) throw new InvalidOperationException("Checkpoint/native selection changed; preview again.");
            var setup = ImportExactExecutionSelection(review.Exact);
            ValidateExactExecutionCommitments(review.Kind, review.Exact, setup);
            if (!setup.Gear.Select(x => x.equipment.id == 0 ? null : x.equipment).SequenceEqual(review.Setup.Gear.Select(x => x.equipment.id == 0 ? null : x.equipment)))
                throw new InvalidOperationException("Exact selected instance changed; preview again.");
            if (ExactExecutionReviewDomains(review.Kind, review.Exact, setup) != review.RecoveryDomains)
                throw new InvalidOperationException("Reviewed exact setup footprint changed; preview again.");
            if (review.Kind == "fight-prep")
            {
                var commitment = CurrentFightPreparationCommitment(review.Exact["commitments"] as JSONObject);
                if (commitment.ToString() != review.Process.ToString()) throw new InvalidOperationException("Fight-preparation phase history changed; preview again.");
                if (commitment["state"].Value == FiniteActionState.Unknown.ToString() || commitment["state"].Value == FiniteActionState.Invocation.ToString()
                    || commitment["stage"].Value == "fight-requested")
                    throw new InvalidOperationException("Started fight preparation requires original route-history review; no entry fight or preparation effect was repeated.");
            }
            ReauthorizeReviewedExecution(review);
            if (review.Kind == "focus")
            {
                if (Settings.UseCustomRebirthPlan) throw new InvalidOperationException("Pause the plan before resuming acquired focus.");
                var commitment = review.Exact["commitments"];
                if (!Enum.TryParse(commitment["goal"].Value, out CustomFocusGoal goal) || !SustainedFocusEnabled(goal))
                    throw new InvalidOperationException("Focus process requires its route-specific semantic review.");
                var aug = SelectionInt(commitment["augmentIndex"], -1, 6); var ritual = SelectionInt(commitment["ritualIndex"], -1, 100);
                var energyWeights = SelectionNumbers(commitment["energyNguWeights"], Settings.EnergyNguWeights.Length).Select(x => checked((int)x)).ToArray();
                var magicWeights = SelectionNumbers(commitment["magicNguWeights"], Settings.MagicNguWeights.Length).Select(x => checked((int)x)).ToArray();
                if (!energyWeights.SequenceEqual(Settings.EnergyNguWeights) || !magicWeights.SequenceEqual(Settings.MagicNguWeights)
                    || commitment["os"].AsInt != (int)Character.wandoos98.os
                    || !commitment["atTargets"].Children.Select(x => x.Value).SequenceEqual(Character.advancedTraining.levelTarget.Select(x => x.ToString(CultureInfo.InvariantCulture))))
                    throw new InvalidOperationException("Acquired focus targets/OS or exclusions changed.");
                var selection = new FocusSetupSelection { Goal = goal, Desired = setup, Settings = Settings, PlanFile = Settings.CustomPlanFile,
                    Material = setup.Gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray(),
                    ObservedGear = LoadoutManager.CaptureExactLoadout(), ObservedSupport = CaptureSupportState().StableKey };
                selection.ObservedMaterial = selection.ObservedGear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                setup.Authorized = () => FocusSetupCurrent(selection);
                setup.MaterialValid = () => !setup.Gear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != selection.Material[i]).Any();
                _focusSetupSelection = selection; _customRequestedFocusGoal = goal; _customSustainedFocusGoal = goal;
                _customAppliedFocusGoal = null; _customFocusStatus = "pending"; _customFocusActivationPending = false;
                _customFocusNextRetry = DateTime.MinValue; _customFocusLastResourceRefresh = DateTime.MinValue;
                _customFocusAugmentIndex = aug; _customFocusAugmentUsesUpgrade = commitment["augmentUsesUpgrade"].AsBool;
                _customFocusBloodRitualIndex = ritual; _customFocusEnergyNguWeights = energyWeights; _customFocusMagicNguWeights = magicWeights;
                PublishExecutionBaseline(setup);
            }
            else if (review.Kind == "quick")
            {
                var domains = (setup.ControlsGear ? ExecutionDomain.Gear : 0) | (setup.ChangeSupport ? ExecutionDomain.Support : 0);
                ExecutionClaim rebound = null;
                rebound = BeginExecutionClaim(setup.Owner, setup.Acquisition, domains, () => rebound == null || ReferenceEquals(_quickExecutionClaim, rebound));
                _quickExecutionClaim = rebound;
                setup.Claim = _quickExecutionClaim; _quickExecutionClaim.Setup = setup;
                if (review.Exact["returnBaseline"].IsObject)
                    SetExecutionReturnBaseline(_quickExecutionClaim, ImportExactExecutionSelection((JSONObject)review.Exact["returnBaseline"]));
                _reviewedExactSetupPending = setup;
            }
            else if (review.Kind == "manual-gear" || review.Kind == "manual") { PublishExecutionBaseline(setup); _reviewedExactSetupPending = setup; }
            else if (review.Kind == "fight-prep")
            {
                ImportReviewedFightPreparation((JSONObject)review.Process, setup);
                if (FightPreparationClaim != null)
                {
                    setup.Claim = FightPreparationClaim;
                    FightPreparationClaim.Setup = setup;
                    _reviewedExactSetupPending = setup;
                }
                else if (review.Process["state"].Value == FiniteActionState.Committed.ToString()) ClearPersistedExecutionSelection("fight-prep");
            }
            else throw new InvalidOperationException("This process requires its semantic process importer.");
            _executionExactPending.Remove(review.Kind); _executionRebindReview = null;
            _resourceReconciliationPending = true;
            return "Acquired selection approved; original process commitments retained.";
        }

        private void EnsureStandaloneSelectionLoaded()
        {
            EnsureExecutionExactLoaded();
            if (_standaloneSelectionLoaded || StandaloneSelectionPath == null) return;
            _standaloneSelectionLoaded = true;
            if (!File.Exists(StandaloneSelectionPath)) return;
            try
            {
                var bytes = File.ReadAllText(StandaloneSelectionPath);
                _standaloneSelectionRevision = JsonFiles.ComputeRevision(File.ReadAllBytes(StandaloneSelectionPath));
                var saved = JsonFiles.ParseObject(bytes);
                if (!EquipmentDescriptorInteger(saved["standaloneVersion"], 1, 1))
                    throw new FormatException("Unsupported standalone execution selection.");
                _standaloneSelectedPolicy = saved.ToString();
                _standaloneSelectionIssue = saved["active"].IsBoolean && !saved["active"].AsBool ? null : "Review the acquired dashboard selection before resuming.";
            }
            catch (Exception error)
            {
                // Never replace unreadable evidence simply because there is no live policy.
                _standaloneSelectionIssue = "Saved dashboard selection is unreadable; explicit Equip is required. " + error.Message;
            }
        }

        private void PersistStandalonePresetSelection(GlobalPresetPolicy policy, string owner)
        {
            if (owner != "dashboard") throw new InvalidOperationException("Unsupported persistent selection owner: " + owner);
            if (StandaloneSelectionPath == null) throw new IOException("Execution selection directory is unavailable.");
            // This is a standalone semantic record, not a synthetic scheduler stage.
            var saved = new JSONObject(); saved["standaloneVersion"] = 1; saved["version"] = 1; saved["active"] = true;
            saved["schema"] = "selected-preset-policy-v1"; saved["owner"] = owner;
            saved["acquisition"] = policy.Acquisition; saved["definition"] = policy.Definition;
            saved["preset"] = policy.Preset.ToJson(); saved["executionPreset"] = policy.ExecutionPreset.ToJson();
            saved["horizon"] = policy.Horizon;
            saved["adventureTaskKey"] = policy.AdventureTaskKey == null ? JSONNull.CreateOrGet() : (JSONNode)new JSONString(policy.AdventureTaskKey);
            saved["adventureTask"] = policy.AdventureTask == null ? JSONNull.CreateOrGet() : (JSONNode)new JSONString(policy.AdventureTask);
            saved["stage"] = new JSONObject(); saved["equipment"] = ExportGlobalPresetEquipmentSelection(policy);
            saved["energyNguWeights"] = LoadoutPresetNumbers(policy.EnergyWeights.Select(x => (double)x));
            saved["magicNguWeights"] = LoadoutPresetNumbers(policy.MagicWeights.Select(x => (double)x));
            saved["normalization"] = LoadoutPresetNumbers(policy.Plan.Normalization);
            var references = new JSONArray();
            for (var i = 0; i < policy.Plan.Normalization.Length; i++)
            {
                var row = new JSONObject(); row["objective"] = LoadoutPresetPlanner.Keys[i];
                row["key"] = policy.Plan.ReferenceKeys[i] == null ? JSONNull.CreateOrGet() : (JSONNode)new JSONString(policy.Plan.ReferenceKeys[i]);
                row["production"] = policy.Plan.ReferenceProduction[i].HasValue
                    ? (JSONNode)new JSONNumber(policy.Plan.ReferenceProduction[i].Value) : JSONNull.CreateOrGet();
                references.Add(row);
            }
            saved["references"] = references;
            var candidate = policy.Candidate; var decision = new JSONObject();
            decision["key"] = candidate.Key; decision["label"] = candidate.Label;
            decision["bloodPercent"] = candidate.BloodPercent; decision["ritual"] = candidate.Ritual;
            decision["zone"] = candidate.Zone; decision["floor"] = candidate.Floor;
            decision["regularEligibilityZone"] = candidate.RegularEligibilityZone; decision["boostEligibilityZone"] = candidate.BoostEligibilityZone;
            var support = new JSONObject(); support["beards"] = LoadoutPresetNumbers(candidate.Support.Beards.Select(x => (double)x));
            support["diggerWeights"] = LoadoutPresetNumbers(candidate.Support.DiggerWeights);
            var diggers = new JSONArray();
            foreach (var d in candidate.Support.Diggers)
            {
                var row = new JSONObject(); row["id"] = d.Id; row["level"] = d.Level.ToString(CultureInfo.InvariantCulture); diggers.Add(row);
            }
            support["diggers"] = diggers; decision["support"] = support;
            var activities = new JSONObject(); var activity = candidate.Activities;
            if (activity != null)
            {
                activities["os"] = activity.Os; activities["augmentId"] = activity.AugmentId;
                var amounts = new JSONArray();
                foreach (var amount in new[] { activity.TimeMachineEnergy, activity.TimeMachineMagic, activity.WandoosEnergy,
                    activity.WandoosMagic, activity.AugmentEnergy, activity.UpgradeEnergy }.Concat(activity.AT))
                    amounts.Add(amount.ToString(CultureInfo.InvariantCulture));
                activities["fixedLegacyAmounts"] = amounts;
            }
            decision["activities"] = activities; saved["decision"] = decision;
            // Keep the paused relationship without importing its stage as dashboard authority.
            var paused = new JSONObject(); paused["plan"] = Settings.CustomPlanFile ?? "";
            paused["generation"] = _protectedGeneration ?? "";
            paused["entry"] = _protectedScheduler?.Entry.ToString(CultureInfo.InvariantCulture) ?? "";
            saved["pausedPlan"] = paused;
            saved["rebirth"] = Character.stats.rebirthNumber.ToString(CultureInfo.InvariantCulture);
            saved["origin"] = Character.totalPlaytime.totalseconds - Character.rebirthTime.totalseconds;
            saved["elapsed"] = Character.rebirthTime.totalseconds; saved["total"] = Character.totalPlaytime.totalseconds;
            var text = saved.ToString(2);
            JsonFiles.WriteAtomic(StandaloneSelectionPath, text);
            _standaloneSelectionLoaded = true; _standaloneSelectedPolicy = saved.ToString();
            _standaloneSelectionRevision = JsonFiles.ComputeRevision(File.ReadAllBytes(StandaloneSelectionPath)); _standaloneSelectionIssue = null;
        }

        private JSONObject GetExecutionPersistenceRuntime()
        {
            var result = new JSONObject();
            var stageReview = Settings?.UseCustomRebirthPlan == true && _protectedSelectedPolicyIssue != null
                && _protectedSelectedPolicyBlockedEntry == ExecutionPersistenceEntry;
            var currentPolicy = _globalPresetReceipt?.Policy ?? _globalPresetApplyingPolicy;
            var presetConflict = currentPolicy != null && GlobalPresetEpochMatches(currentPolicy)
                && ExecutionScopeNeedsReview(PresetResourceReviewDomains(currentPolicy.ExecutionPreset) | ExecutionDomain.Gear | ExecutionDomain.Support);
            var focusConflict = FocusSetupCurrent(_focusSetupSelection)
                && ExecutionScopeNeedsReview((ExecutionDomain)(int)_focusSetupSelection.Desired.Recall | ExecutionDomain.Gear | ExecutionDomain.Support);
            result["reviewRequired"] = _protectedOuterCheckpointIssue != null || stageReview || _standaloneSelectionIssue != null || _executionExactPending?.Count > 0 || _importedRouteCommitments != null || presetConflict || focusConflict;
            result["canPreview"] = _protectedOuterCheckpointIssue == null
                && (stageReview && _protectedSelectedPolicy != null || Settings?.UseCustomRebirthPlan == false && _standaloneSelectedPolicy != null && _standaloneSelectionIssue != null || _executionExactPending?.Count > 0 || _importedRouteCommitments != null || presetConflict || focusConflict);
            result["reason"] = _protectedOuterCheckpointIssue ?? _routeCommitmentIssue
                ?? (stageReview ? _protectedSelectedPolicyIssue : _standaloneSelectionIssue) ?? "";
            if (result["reason"].Value.Length == 0) result["reason"] = _routeCommitmentIssue ?? (_executionExactPending?.Count > 0 ? "Review the acquired selection before resuming." : "");
            result["owner"] = _protectedOuterCheckpointIssue == null && _importedRouteCommitments != null ? "Route processes"
                : stageReview ? ProtectedPresetOwner ?? "" : _standaloneSelectionIssue == null ? "" : "dashboard";
            if (_protectedOuterCheckpointIssue != null && _protectedPlan != null)
            {
                var recovery = new JSONObject(); recovery["checkpointRevision"] = JsonFiles.ComputeRevision(File.ReadAllBytes(ProtectedCheckpointPath));
                recovery["planRevision"] = _protectedPlan.RuntimeRevision; var rows = new JSONArray();
                for (var i = 0; i < _protectedPlan.Stages.Length; i++)
                { var row = new JSONObject(); row["stageIndex"] = i; row["name"] = _protectedPlan.Stages[i].Name; rows.Add(row); }
                recovery["stages"] = rows; result["recovery"] = recovery;
            }
            return result;
        }
    }
}
