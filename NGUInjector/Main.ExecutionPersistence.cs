using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private sealed class ExecutionRebindReview
        {
            internal string Token, Revision, Selection, Owner, Material, Native, Permissions;
            internal double Expires;
            internal GlobalPresetPolicy Policy;
            internal ExecutionGrant Grant;
            internal long Conflict;
            internal string Entry;
            internal string Kind, Path;
            internal JSONObject Exact;
            internal SelectedSetupRequirements Setup;
            internal JSONNode Process;
            internal Equipment[] ProcessInstances;
            internal ExecutionDomain RecoveryDomains;
            internal Dictionary<ResourceTarget, long[]> Resources;
            internal object[] ResourceObjects;
        }
        private ExecutionRebindReview _executionRebindReview;
        private string _standaloneSelectedPolicy;
        private string _standaloneSelectionIssue;
        private string _standaloneSelectionRevision;
        private bool _standaloneSelectionLoaded;
        private string ExecutionPersistenceEntry => _protectedScheduler == null ? null : AutomaticRecoveryEntry;

        private static int SelectionInt(JSONNode value, int minimum, int maximum)
        {
            if (!EquipmentDescriptorInteger(value, minimum, maximum)) throw new FormatException("Invalid selected-policy integer.");
            return value.AsInt;
        }

        private static long SelectionLong(JSONNode value)
        {
            long result;
            if (value == null || !value.IsString || !long.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out result))
                throw new FormatException("Invalid selected-policy exact amount.");
            return result;
        }

        private static double[] SelectionNumbers(JSONNode value, int length)
        {
            if (value == null || !value.IsArray || value.Count != length || value.Children.Any(x => !FiniteCheckpointNumber(x, true)))
                throw new FormatException("Invalid selected-policy numeric vector.");
            return value.Children.Select(x => x.AsDouble).ToArray();
        }

        private string ExecutionRebindNativeKey() => Character.adventure.zone + ":" + Character.adventure.itopodStart
            + ":" + Character.adventure.itopodEnd + ":" + Character.adventure.autoattacking
            + ":" + (int)Character.wandoos98.os + ":" + Character.advancedTraining.autoAdvance
            + ":" + string.Join(",", Character.advancedTraining.levelTarget.Select(x => x.ToString(CultureInfo.InvariantCulture)))
            + ":" + CaptureSupportState().StableKey;

        private string ExecutionRebindPermissions() => Settings.ManageEnergy + ":" + Settings.ManageMagic + ":" + Settings.ManageR3
            + ":" + Settings.ManageGear + ":" + Settings.ManageDiggers + ":" + Settings.UseCustomRebirthPlan
            + ":" + Settings.CustomPlanFile + ":" + Settings.ManualAdventureEnabled + ":" + _customRequestedFocusGoal;

        private ExecutionDomain PresetExecutionReviewDomains(GlobalPresetPolicy policy, LoadoutPresetCapture capture)
        {
            var candidate = ReplayGlobalPreset(policy, policy.Preset, capture);
            var domains = ExecutionDomain.Gear | ExecutionDomain.Support;
            if (candidate.ControlsEnergy) domains |= ExecutionDomain.Energy;
            if (candidate.ControlsMagic) domains |= ExecutionDomain.Magic;
            if (GlobalPresetRequiresNativeTargets(policy.Stage)) domains |= ExecutionDomain.NativeSettings;
            var changesSetup = !LoadoutManager.ExactLoadoutMatches(policy.Items.Select((item, i) =>
                (item ?? new Equipment()).GetInventoryHelper(policy.Slots[i])).ToArray())
                || policy.ExecutionPreset.SupportMode == "planned" && CaptureSupportState().Selection.StableKey != new SupportSelection(
                    policy.Candidate.Support.Beards, policy.Candidate.Support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()).StableKey;
            if (changesSetup || policy.ExecutionPreset.Weights.Skip(3).Take(3).Any(x => x > 0)) domains |= ExecutionDomain.Route;
            return domains;
        }

        private bool ExecutionScopeNeedsReview(ExecutionDomain domains)
        {
            if ((_resourceSetupExecutor.Application.BlockedLanes & (int)domains & 7) != 0) return true;
            return SelectedSetupConflicts.TryGetValue(Character, out var conflict) && conflict.Rebirth == Character.stats.rebirthNumber
                && ((domains & ExecutionDomain.Gear) != 0 && conflict.Gear != null
                    || (domains & ExecutionDomain.Support) != 0 && conflict.Support != null
                    || (domains & ExecutionDomain.Route) != 0 && conflict.Route != null
                    || (domains & ExecutionDomain.NativeSettings) != 0 && conflict.NativeSettings != null);
        }

        private static ExecutionDomain PresetResourceReviewDomains(CustomLoadoutPreset preset)
        {
            var weights = preset.Weights;
            return (weights[0] + weights[6] + weights[7] + weights[8] + weights[9] > 0 ? ExecutionDomain.Energy : 0)
                | (weights[1] + weights[2] + weights[6] + weights[8] > 0 ? ExecutionDomain.Magic : 0);
        }

        private void CaptureExecutionReviewResources(ExecutionRebindReview review)
        {
            review.ResourceObjects = ResourceWriter.CaptureResourceContext(Character);
            review.Resources = ResourceWriter.CaptureAllocations(Character)
                .Where(x => ((int)review.RecoveryDomains & (1 << ResourceWriter.Lane(x.Key))) != 0)
                .ToDictionary(x => x.Key, x => x.Value?.ToArray());
        }

        private bool ExecutionReviewResourcesMatch(ExecutionRebindReview review)
        {
            var objects = ResourceWriter.CaptureResourceContext(Character);
            if (objects.Length != review.ResourceObjects.Length || objects.Where((item, i) => !ReferenceEquals(item, review.ResourceObjects[i])).Any()) return false;
            var current = ResourceWriter.CaptureAllocations(Character);
            return review.Resources.All(x => current.TryGetValue(x.Key, out var actual)
                && (x.Value == null ? actual == null : actual != null && x.Value.SequenceEqual(actual)));
        }

        private void ReauthorizeReviewedExecution(ExecutionRebindReview review)
        {
            // Last admission boundary: no clearing, intent publication or native write
            // is permitted from a stale token or a changed reviewed native lane.
            Func<bool> current = () => ReferenceEquals(_executionRebindReview, review)
                && review.Entry == ExecutionPersistenceEntry && review.Permissions == ExecutionRebindPermissions()
                && review.Conflict == SelectedSetupConflictVersion(Character) && LoadoutPresetClock < review.Expires
                && ExecutionReviewResourcesMatch(review) && ExecutionReviewNativeSettingsOwned(review)
                && ValidateExecutionGrant(review.Grant, review.RecoveryDomains);
            if (!current() || review.RecoveryDomains != ExecutionDomain.None
                && !ReauthorizeExecutionDomains(review.Grant, review.RecoveryDomains, current))
                throw new InvalidOperationException("Reviewed execution scope changed; preview again.");
            _executionRebindReview = null; // Also spends metadata-only tokens without clearing any domain.
        }

        private bool ExecutionReviewNativeSettingsOwned(ExecutionRebindReview review)
        {
            if ((review.RecoveryDomains & ExecutionDomain.NativeSettings) == 0) return true;
            if (_globalPresetNativeTargetConflict != null && _globalPresetNativeTargetConflict != review.Owner) return false;
            return !SelectedSetupConflicts.TryGetValue(Character, out var conflict) || conflict.Rebirth != Character.stats.rebirthNumber
                || conflict.NativeSettings == null || conflict.NativeSettingsOwner == review.Owner;
        }

        private static void WriteExecutionReviewScope(JSONObject result, ExecutionRebindReview review)
        {
            result["domains"] = review.RecoveryDomains.ToString();
            var resources = new JSONObject();
            foreach (var pair in review.Resources)
            {
                if (pair.Value == null) { resources[pair.Key.ToString()] = JSONNull.CreateOrGet(); continue; }
                var values = new JSONArray(); foreach (var value in pair.Value) values.Add(value.ToString(CultureInfo.InvariantCulture));
                resources[pair.Key.ToString()] = values;
            }
            result["observedResources"] = resources;
        }

        // Reconstruct only saved decisions. Capture is read-only; neither Plan nor any
        // optimizer/search is called. Native references and all authority are session-local.
        private GlobalPresetPolicy ImportExecutionSelection(JSONNode saved, out LoadoutPresetCapture capture)
        {
            if (saved == null || !saved.IsObject || SelectionInt(saved["version"], 1, 1) != 1
                || saved["schema"].Value != "selected-preset-policy-v1"
                || !saved["acquisition"].IsString || !Guid.TryParseExact(saved["acquisition"].Value, "N", out _)
                || !saved["definition"].IsString || !saved["owner"].IsString)
                throw new FormatException("Selected policy has an invalid schema or acquisition.");
            var original = CustomLoadoutPreset.Parse(saved["preset"]);
            var resolved = CustomLoadoutPreset.Parse(saved["executionPreset"]);
            CustomLoadoutPreset.Validate(original); CustomLoadoutPreset.Validate(resolved);
            if (saved["definition"].Value != GlobalPresetDefinition(original) || resolved.AdventureDynamicWeight != 0
                || original.Id != resolved.Id || original.SupportMode != resolved.SupportMode)
                throw new InvalidOperationException("Acquired original/resolved definitions are incompatible.");
            var horizon = SelectionInt(saved["horizon"], 1, 604800);
            CustomPlanStage stage = null;
            var stageNode = saved["stage"];
            if (!stageNode.IsObject) throw new FormatException("Selected policy stage targets are invalid.");
            if (stageNode.Count != 0)
                stage = new CustomPlanStage { CompletionMode = SelectionInt(stageNode["completionMode"], 0, 1),
                    TargetAdvancedTrainingPower = SelectionLong(stageNode["power"]),
                    TargetAdvancedTrainingToughness = SelectionLong(stageNode["toughness"]) };
            if (stage != null && (stage.TargetAdvancedTrainingPower > 9007199254740991L || stage.TargetAdvancedTrainingToughness > 9007199254740991L))
                throw new FormatException("Acquired AT commitment exceeds the exact supported range.");
            if (saved["owner"].Value == ProtectedPresetOwner)
            {
                var issue = ValidateProtectedPresetProvenance(saved);
                if (issue != null) throw new InvalidOperationException(issue);
                var current = _protectedPlan.Stages[_protectedScheduler.StageIndex];
                if (StageGlobalPresetId(_protectedPlan, current) != original.Id
                    || GlobalPresetStageTargets(current) != GlobalPresetStageTargets(stage))
                    throw new InvalidOperationException("Acquired policy does not match this stage's preset/AT commitment.");
            }
            else
            {
                if (stage != null || saved["owner"].Value != "dashboard")
                    throw new InvalidOperationException("Selected policy owner is not supported by this review.");
                if (!saved["rebirth"].IsString || saved["rebirth"].Value != Character.stats.rebirthNumber.ToString(CultureInfo.InvariantCulture)
                    || !FiniteCheckpointNumber(saved["origin"], false)
                    || Math.Abs(saved["origin"].AsDouble - (Character.totalPlaytime.totalseconds - Character.rebirthTime.totalseconds)) > 5
                    || !FiniteCheckpointNumber(saved["elapsed"], true) || saved["elapsed"].AsDouble > Character.rebirthTime.totalseconds + .1
                    || !FiniteCheckpointNumber(saved["total"], true) || saved["total"].AsDouble > Character.totalPlaytime.totalseconds + .1)
                    throw new InvalidOperationException("Dashboard selection does not match this save context.");
            }
            var expectedResolved = ResolveAdventurePreset(original, out var taskKey, out _);
            if (GlobalPresetDefinition(expectedResolved) != GlobalPresetDefinition(resolved)
                || (saved["adventureTaskKey"].IsNull ? null : saved["adventureTaskKey"].Value) != taskKey)
                throw new InvalidOperationException("Acquired adventure activity changed; the saved selection was not replaced.");
            var observed = CaptureLoadoutPresetDefinition(original, stage: stage);
            capture = observed;
            capture.Horizon = horizon;
            if (GlobalPresetDefinition(capture.ExecutionPreset) != GlobalPresetDefinition(resolved))
                throw new InvalidOperationException("Resolved acquired definition changed during observation.");
            var slots = capture.Snapshot.Slots;
            var equipment = ResolveGlobalPresetEquipmentSelection(saved["equipment"], saved["acquisition"].Value, slots, capture.Instances);
            var items = equipment.Select((item, i) => item == null
                ? new PresetItem { SourceSlot = slots[i], Group = LoadoutPresetPlanner.Group(slots[i]), Name = "Empty" }
                : observed.Snapshot.Items.Single(x => ReferenceEquals(observed.Instances[x.SourceSlot], item))).ToArray();
            var energy = SelectionNumbers(saved["energyNguWeights"], Settings.EnergyNguWeights.Length);
            var magic = SelectionNumbers(saved["magicNguWeights"], Settings.MagicNguWeights.Length);
            if (energy.Concat(magic).Any(x => x > int.MaxValue || x != Math.Floor(x))
                || !energy.SequenceEqual(Settings.EnergyNguWeights.Select(x => (double)x))
                || !magic.SequenceEqual(Settings.MagicNguWeights.Select(x => (double)x)))
                throw new InvalidOperationException("Misc NGU weights changed; acquired zero exclusions were preserved.");
            var norms = SelectionNumbers(saved["normalization"], LoadoutPresetPlanner.Keys.Length);
            var references = saved["references"];
            if (!references.IsArray || references.Count != norms.Length) throw new FormatException("Frozen scoring references are invalid.");
            var plan = new PresetPlan { Normalization = norms, ReferenceKeys = new string[norms.Length], ReferenceProduction = new double?[norms.Length] };
            for (var i = 0; i < norms.Length; i++)
            {
                var row = references[i];
                if (!row.IsObject || row["objective"].Value != LoadoutPresetPlanner.Keys[i]
                    || !row["key"].IsNull && !row["key"].IsString
                    || !row["production"].IsNull && !FiniteCheckpointNumber(row["production"], true))
                    throw new FormatException("Frozen scoring reference is invalid.");
                plan.ReferenceKeys[i] = row["key"].IsNull ? null : row["key"].Value;
                plan.ReferenceProduction[i] = row["production"].IsNull ? (double?)null : row["production"].AsDouble;
                if (norms[i] != (plan.ReferenceProduction[i] ?? 0)) throw new FormatException("Frozen normalization does not match its reference.");
            }
            var decision = saved["decision"];
            if (!decision.IsObject || !decision["key"].IsString || string.IsNullOrEmpty(decision["key"].Value)
                || !decision["label"].IsString) throw new FormatException("Selected candidate identity is invalid.");
            var supportNode = decision["support"];
            if (!supportNode.IsObject || !supportNode["beards"].IsArray || supportNode["beards"].Count > 7
                || !supportNode["diggers"].IsArray || supportNode["diggers"].Count > 12)
                throw new FormatException("Selected support shape is invalid.");
            var beards = supportNode["beards"].Children.Select(x => SelectionInt(x, 0, 6)).ToArray();
            var diggers = supportNode["diggers"].Children.Select(x => {
                var id = SelectionInt(x["id"], 0, 11); var level = SelectionLong(x["level"]);
                var native = observed.Snapshot.Diggers.SingleOrDefault(d => d.Id == id);
                if (native == null || level <= 0 || level > native.Maximum) throw new InvalidOperationException("Acquired digger is unavailable.");
                return new SupportDigger { Id = id, Level = level, Maximum = native.Maximum, BaseDrain = native.BaseDrain,
                    Growth = native.Growth, ConsumesGold = native.ConsumesGold, StartBonus = native.StartBonus, PerLevel = native.PerLevel };
            }).ToArray();
            var support = new SupportSelection(beards, diggers.Select(d => new SupportDiggerLevel(d.Id, d.Level)).ToArray());
            var supportEvaluation = SupportProjection.Evaluate(capture.Snapshot, support, resolved.DiggerBudgetPercent / 100d, resolved.SupportMode == "planned");
            if (!supportEvaluation.Feasible) throw new InvalidOperationException(supportEvaluation.Reason);
            if (resolved.SupportMode == "current" && support.StableKey != capture.Snapshot.CurrentSelection.StableKey)
                throw new InvalidOperationException("Keep-current support changed; review cannot silently select different support.");
            var candidate = new PresetCandidate { Key = decision["key"].Value, Label = decision["label"].Value, Items = items,
                BloodPercent = SelectionInt(decision["bloodPercent"], 0, 100), Ritual = SelectionInt(decision["ritual"], -1, 100),
                Zone = SelectionInt(decision["zone"], -1, 1000), Floor = SelectionInt(decision["floor"], -1, 10000),
                RegularEligibilityZone = SelectionInt(decision["regularEligibilityZone"], -1, 1000),
                BoostEligibilityZone = SelectionInt(decision["boostEligibilityZone"], -1, 1000),
                Support = new PresetSupport { Beards = beards, Diggers = diggers,
                    DiggerWeights = SelectionNumbers(supportNode["diggerWeights"], supportNode["diggerWeights"].Count), Notes = new string[0] } };
            if (candidate.Support.DiggerWeights.Length != 0 && candidate.Support.DiggerWeights.Length != 12)
                throw new FormatException("Acquired digger weights are invalid.");
            if ((candidate.Zone == 1000 ? candidate.Floor < 0 : candidate.Floor != -1)
                || candidate.Zone >= 0 && candidate.Zone != 1000 && (!CombatManager.IsZoneUnlocked(candidate.Zone) || ZoneHelpers.ZoneIsTitan(candidate.Zone)))
                throw new InvalidOperationException("Acquired route is no longer eligible.");
            if (candidate.Zone == 1000 && candidate.Floor > Math.Min(Character.adventureController.maxItopodLevel(), Character.adventure.highestItopodLevel - 1)
                || candidate.RegularEligibilityZone >= 0 && PresetRouteRecommendation(candidate.RegularEligibilityZone) == null
                || candidate.BoostEligibilityZone >= 0 && PresetRouteRecommendation(candidate.BoostEligibilityZone) == null)
                throw new InvalidOperationException("Acquired route eligibility witness is unavailable; no alternate route was searched.");
            var activities = decision["activities"];
            if (!activities.IsObject) throw new FormatException("Acquired activity decisions are invalid.");
            if (activities.Count != 0)
            {
                var amounts = activities["fixedLegacyAmounts"];
                if (!amounts.IsArray || amounts.Count != 11) throw new FormatException("Acquired activity amounts are invalid.");
                var values = amounts.Children.Select(SelectionLong).ToArray();
                var os = SelectionInt(activities["os"], 0, 2);
                var aug = SelectionInt(activities["augmentId"], -1, 100);
                if (os != capture.Snapshot.WandoosOs || aug >= 0 && !capture.Snapshot.Augments.Any(x => x.Id == aug))
                    throw new InvalidOperationException("Acquired AUG/OS is unavailable; no new choice was made.");
                candidate.Activities = new PresetActivityAllocation { Os = os, AugmentId = aug,
                    TimeMachineEnergy = values[0], TimeMachineMagic = values[1], WandoosEnergy = values[2], WandoosMagic = values[3],
                    AugmentEnergy = values[4], UpgradeEnergy = values[5], AT = values.Skip(6).ToArray() };
                // Validate overflow without treating fixed semantic amounts as committed accounting.
                _ = checked(candidate.Activities.EnergyTotal + candidate.Activities.MagicTotal);
            }
            plan.Candidates = new[] { candidate }; plan.Specialists = new PresetCandidate[0];
            var policy = new GlobalPresetPolicy { Acquisition = saved["acquisition"].Value, Definition = saved["definition"].Value,
                Preset = original, ExecutionPreset = resolved, Horizon = horizon, Stage = stage,
                AdventureTaskKey = taskKey, AdventureTask = saved["adventureTask"].IsNull ? null : saved["adventureTask"].Value,
                AdventureObservation = taskKey, Epoch = new LoadoutManager.SnapshotEpoch(), EpochObjects = capture.Epoch.ToArray(),
                Candidate = candidate, Plan = plan, Items = equipment, Slots = slots.ToArray(), Material = equipment.Select(LoadoutPresetPrimitiveKey).ToArray(),
                EnergyWeights = energy.Select(x => (int)x).ToArray(), MagicWeights = magic.Select(x => (int)x).ToArray() };
            var replay = ReplayGlobalPreset(policy, original, capture);
            if (!replay.Feasible) throw new InvalidOperationException("Acquired setup is not currently feasible: " + string.Join(" ", replay.Warnings));
            return policy;
        }

        internal JSONObject PreviewExecutionRebind(JSONNode args)
        {
            if (args != null && (!args.IsObject || args.Count != 0)) throw new FormatException("Preview rebind accepts no arguments.");
            if (_protectedOuterCheckpointIssue != null) throw new InvalidOperationException(_protectedOuterCheckpointIssue);
            EnsureStandaloneSelectionLoaded();
            // Route quarantine blocks adventure independently of the selected preset.
            if (_importedRouteCommitments != null) return PreviewRouteExecutionRebind();
            var scheduled = Settings.UseCustomRebirthPlan
                && (_protectedSelectedPolicyBlockedEntry == ExecutionPersistenceEntry && _protectedSelectedPolicyIssue != null
                    || _protectedScheduler != null && !_protectedScheduler.Finished && _protectedSelectedPolicy != null);
            var selection = scheduled ? _protectedSelectedPolicy : _standaloneSelectedPolicy;
            if (!scheduled && FocusSetupCurrent(_focusSetupSelection)) return PreviewExactExecutionRebind("focus");
            if (!scheduled && _standaloneSelectionIssue == null && _executionExactPending?.Count > 0)
                return PreviewExactExecutionRebind(null);
            if (selection == null || !scheduled && _standaloneSelectionIssue == null && _globalPresetManualId == null)
                throw new InvalidOperationException("No acquired selection requires review.");
            var saved = JsonFiles.ParseObject(selection);
            var policy = ImportExecutionSelection(saved, out var capture);
            var owner = saved["owner"].Value;
            if (!scheduled && Settings.UseCustomRebirthPlan) throw new InvalidOperationException("Pause the plan before reviewing the dashboard selection.");
            var revision = scheduled ? JsonFiles.ComputeRevision(File.ReadAllBytes(ProtectedCheckpointPath)) : _standaloneSelectionRevision;
            var review = new ExecutionRebindReview { Token = Guid.NewGuid().ToString("N"), Revision = revision, Selection = selection,
                Owner = owner, Policy = policy, Material = capture.Material, Native = ExecutionRebindNativeKey(),
                Permissions = ExecutionRebindPermissions(),
                Entry = ExecutionPersistenceEntry, Conflict = SelectedSetupConflictVersion(Character), Expires = LoadoutPresetClock + 120 };
            review.RecoveryDomains = PresetExecutionReviewDomains(policy, capture);
            CaptureExecutionReviewResources(review);
            review.Grant = ResolveExecutionGrant(owner, policy.Acquisition, review.RecoveryDomains,
                () => ReferenceEquals(_executionRebindReview, review) && review.Entry == ExecutionPersistenceEntry
                    && review.Conflict == SelectedSetupConflictVersion(Character));
            _executionRebindReview = review;
            if (!ValidateExecutionGrant(review.Grant, review.Grant.Domains)) { _executionRebindReview = null; throw new InvalidOperationException("Current execution authority is unavailable."); }
            var result = new JSONObject(); result["token"] = review.Token; result["checkpointRevision"] = revision;
            result["owner"] = owner; result["acquisition"] = policy.Acquisition; result["preset"] = policy.Preset.ToJson();
            result["equipment"] = ExportGlobalPresetEquipmentSelection(policy); result["entry"] = review.Entry;
            result["horizon"] = policy.Horizon; result["support"] = saved["decision"]["support"];
            result["expiresInSeconds"] = 120;
            WriteExecutionReviewScope(result, review);
            return result;
        }

        internal string ApplyExecutionRebind(JSONNode args)
        {
            GlobalPresetArgs(args, "token", "checkpointRevision");
            var review = _executionRebindReview;
            if (review == null || review.Token != args["token"].Value || review.Revision != args["checkpointRevision"].Value
                || LoadoutPresetClock >= review.Expires || ExecutionRebindPermissions() != review.Permissions
                || !ValidateExecutionGrant(review.Grant, review.Grant.Domains))
                throw new InvalidOperationException("Selection review expired or authority changed; preview again.");
            if (review.Exact != null) return ApplyExactExecutionRebind(review);
            if (review.Kind == "restart") return ApplyExecutionRestart(review);
            if (review.Kind == "routes")
            {
                if (JsonFiles.ComputeRevision(File.ReadAllBytes(review.Path)) != review.Revision || ExactExecutionMaterialKey() != review.Material
                    || ExecutionRebindNativeKey() != review.Native || !review.ProcessInstances.SequenceEqual(CurrentExecutionEquipmentInstances()))
                    throw new InvalidOperationException("Route review evidence changed; preview again.");
                _executionRebindReview = null;
                ImportRouteExecutionCommitments(review.Process);
                if (!RebindRouteExecutionCommitments()) throw new InvalidOperationException(_routeCommitmentIssue);
                if (Path.GetFileName(review.Path).StartsWith("lifecycle-", StringComparison.Ordinal))
                { var ended = JsonFiles.ParseObject(review.Process.ToString()); ended["active"] = false; JsonFiles.WriteAtomic(review.Path, ended.ToString(2)); }
                return "Acquired route process and original history reviewed for this session.";
            }
            var scheduled = review.Owner == ProtectedPresetOwner;
            var path = scheduled ? ProtectedCheckpointPath : StandaloneSelectionPath;
            if (path == null || JsonFiles.ComputeRevision(File.ReadAllBytes(path)) != review.Revision
                || (scheduled ? _protectedSelectedPolicy : _standaloneSelectedPolicy) != review.Selection)
                throw new InvalidOperationException("Checkpoint changed; preview the current selection again.");
            var policy = ImportExecutionSelection(JsonFiles.ParseObject(review.Selection), out var capture);
            if (capture.Material != review.Material || ExecutionRebindNativeKey() != review.Native
                || !policy.Items.SequenceEqual(review.Policy.Items) || !ValidateExecutionGrant(review.Grant, review.Grant.Domains)
                || PresetExecutionReviewDomains(policy, capture) != review.RecoveryDomains)
                throw new InvalidOperationException("Native selection/material changed; preview again.");
            var receipt = _globalPresetReceipt;
            // Resource uncertainty does not erase still-owned native gear/support
            // evidence. Keep that live evidence only when the reviewed SAME selection
            // independently matches it; a restart never imports a synthetic receipt.
            var retainLiveSetup = receipt != null && receipt.Owner == review.Owner && receipt.Id == policy.Preset.Id
                && GlobalPresetEpochMatches(receipt.Policy) && !receipt.Policy.MaterialPersistencePending
                && receipt.Policy.Acquisition == policy.Acquisition && receipt.Policy.Definition == policy.Definition
                && receipt.Policy.Horizon == policy.Horizon && receipt.Policy.Items.SequenceEqual(policy.Items)
                && GlobalPresetGearMatches(receipt) && CurrentGlobalPresetSupportMatches(receipt)
                && LoadoutManager.ExactLoadoutMatches(policy.Items.Select((item, i) => (item ?? new Equipment()).GetInventoryHelper(policy.Slots[i])).ToArray())
                && CaptureSupportState().Selection.StableKey == new SupportSelection(policy.Candidate.Support.Beards,
                    policy.Candidate.Support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()).StableKey;
            ReauthorizeReviewedExecution(review);
            _globalPresetPolicies[policy.Preset.Id] = retainLiveSetup ? receipt.Policy : policy;
            _globalPresetApplyingPolicy = retainLiveSetup ? null : policy;
            _globalPresetApplyingEvent = retainLiveSetup ? null : review.Owner + ":" + policy.Preset.Id;
            if (scheduled) _protectedSelectedPolicyBlockedEntry = _protectedSelectedPolicyIssue = null;
            else { _standaloneSelectionIssue = null; _globalPresetManualId = policy.Preset.Id; }
            _customEquipmentApplicationInvalidated = !retainLiveSetup;
            _resourceReconciliationPending = true;
            return "Acquired selection approved for this session; entry timing and history preserved.";
        }

        // Called only after the producer has causally approved material on the SAME
        // retained instances. Failure withholds dependent work; it never grants search.
        private void PersistApprovedExecutionMaterialRevision(GlobalPresetPolicy policy, string owner)
        {
            if (policy == null || !GlobalPresetEpochMatches(policy)
                || policy.Items.Where((item, i) => LoadoutPresetPrimitiveKey(item) != policy.Material[i]).Any())
                throw new InvalidOperationException("Approved material revision is not current.");
            PersistProtectedPresetSelection(policy, owner);
        }
    }
}
