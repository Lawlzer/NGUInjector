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
        private bool _customPreRebirthPrepRunning;
        private bool _customPreRebirthPrepCompleted;
        private bool _manualRebirthPending;
        private ExecutionClaim _fightPreparationClaim;
        private SelectedSetupRequirements _fightPreparationSetup;
        private ih[] _fightPreparationGear;
        private SupportSelection _fightPreparationSupport;
        private bool _fightPreparationUpgradesDone;
        private FiniteActionRecord _fightPreparationRecord;
        private FiniteActionRecord _completedFightPreparationRecord;
        private bool _fightPreparationStarted;
        private string _manualRebirthOpportunity;
        private long? _manualRebirthNumber;
        private sealed class ManualFightPreparationRequest
        {
            internal FiniteActionIdentity Identity;
            internal FiniteActionRecord Previous, Retirement;
            internal CustomPlan Plan;
            internal ih[] Gear;
            internal string[] Material;
            internal SupportSelection Support;
            internal object Settings, BossProcess, TitanProcess, CombatProcess;
            internal string PlanFile, Entry;
            internal double Started;
            internal bool CancellationRequested;
            internal readonly LoadoutManager.SnapshotEpoch Epoch = new LoadoutManager.SnapshotEpoch();
        }
        private ManualFightPreparationRequest _manualFightPreparationRequest;
        internal ExecutionClaim FightPreparationClaim => _fightPreparationClaim;
        internal SelectedSetupRequirements FightPreparationSetup => _fightPreparationSetup;
        internal string FightPreparationIdentity => _fightPreparationRecord?.Identity.Key;
        internal bool FightPreparationVerified => _fightPreparationClaim != null && _fightPreparationSetup != null
            && FiniteActionSetupVerified(_fightPreparationClaim, _fightPreparationSetup);
        internal void CompleteFightPreparation()
        {
            if (!BossPushRunning && !Character.bossController.isFighting && !Character.bossController.nukeBoss)
                ReleaseFightPreparation();
        }

        private bool ObserveCustomRebirthReadiness(CustomPlan plan)
        {
            // Confirmation and retirement must run even when an automatic stage owns
            // the callback. Neither observation admits a rebirth or waives stage work.
            var ready = Settings.UseCustomRebirthPlan && plan != null
                && new CustomPlanRebirth(plan).RebirthAvailable();
            if (!ready && !_manualRebirthPending && _manualFightPreparationRequest == null && (_customPreRebirthPrepRunning || _customPreRebirthPrepCompleted))
                ResetCustomPreRebirthPrep();
            return ready;
        }

        internal bool RunCustomPreRebirthPrepIfNeeded(CustomPlan plan)
        {
            if (!Settings.UseCustomRebirthPlan || plan == null)
                return false;
            if (_manualFightPreparationRequest != null) return true;

            if (_customPreRebirthPrepCompleted)
                return false;

            if (_customPreRebirthPrepRunning)
            {
                if (BossPushRunning || CustomBossFightActive())
                    return true;

                _customPreRebirthPrepRunning = false;
                _customPreRebirthPrepCompleted = true;
                ReleaseFightPreparation();
                Log("Custom pre-rebirth fight prep complete");
                return false;
            }

            CancelManualTitan("automatic rebirth preparation");
            RunFightPrep(plan, true, true, "Automatic rebirth prep");
            if (!_fightPreparationStarted) return true;
            _customPreRebirthPrepRunning = BossPushRunning;
            _customPreRebirthPrepCompleted = !_customPreRebirthPrepRunning;
            return _customPreRebirthPrepRunning;
        }

        internal string RunManualFightPrep()
        {
            if (string.IsNullOrEmpty(CurrentExecutionCommandId)) return "Fight preparation requires an identified explicit request.";
            if (_manualFightPreparationRequest != null) return ContinueManualFightPreparation();
            var plan = CustomPlanStore?.Load(Settings.CustomPlanFile) ?? new CustomPlan();
            var identity = ActionIdentity("fight-preparation", "fight-preparation", CurrentExecutionCommandId);
            if (_fightPreparationRecord?.Identity.Key == identity.Key)
                return RunFightPrep(plan, false, false, "Manual fight prep", opportunity: CurrentExecutionCommandId);
            if (FiniteActions.Find(identity) != null || FiniteActions.Records.Any(x => x.Identity.Kind == "preparation-handoff" && x.Identity.Opportunity == identity.Key))
                return "Manual fight prep: retained request requires selected-setup review; no replacement was searched or fight repeated.";
            try
            {
                // Reject missing/unusable choices before changing the previous intent.
                var policy = SetupForFocus(CustomFocusGoal.Fight);
                var gear = SelectSetupGear((GearOptimizationGoal)policy.Stage.GearGoal, (FightScoreMode)plan.FightScoreMode);
                var support = SelectFocusSupport(policy);
                var failure = LoadoutManager.ValidateExactLoadout(gear);
                if (failure == null && !LoadoutManager.ExactLoadoutMatches(gear))
                    failure = GearOptimizer.CheckExactSwapCapacity(gear, Character.curEnergy, Character.res3.curRes3);
                if (failure != null) return "Manual fight prep: setup waiting: " + failure;
                if (Character.buttons.beards.interactable && !support.Beards.Contains(BeardFuManchu))
                    return "Manual fight prep: setup waiting: required fight beard does not fit.";
                PreflightExactSupport(support, ActivityPolicy.Current.DiggerBudgetFraction, true);
                if (_fightPreparationClaim != null && (!ExecutionClaimCurrent(_fightPreparationClaim) || !ExecutionClaimSetupOwned(_fightPreparationClaim)))
                    return "Manual fight prep: setup waiting: previous preparation authority requires review.";
                var request = new ManualFightPreparationRequest { Identity = identity, Plan = plan, Gear = gear, Support = support,
                    Material = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray(), Previous = _fightPreparationRecord ?? _completedFightPreparationRecord,
                    BossProcess = _bossPushProcess, TitanProcess = _manualTitanProcess, CombatProcess = _combatPrepRun,
                    Settings = Settings, PlanFile = Settings.CustomPlanFile, Entry = ExecutionPersistenceEntry ?? "",
                    Started = Character.rebirthTime.totalseconds };
                var retirementId = new FiniteActionIdentity("preparation-handoff", identity.Owner, identity.Acquisition,
                    request.Previous?.Identity.Key ?? _bossPushIdentity ?? "no-predecessor", identity.Key);
                request.Retirement = FiniteActions.Find(retirementId);
                if (request.Retirement?.State == FiniteActionState.Unknown || request.Retirement?.State == FiniteActionState.Invocation)
                    return "Manual fight prep: previous handoff outcome requires review; no process was repeated.";
                if (request.Retirement == null)
                {
                    var evidence = new JSONObject(); evidence["successor"] = identity.Key; evidence["started"] = request.Started;
                    evidence["plan"] = request.PlanFile; evidence["entry"] = request.Entry; evidence["support"] = support.StableKey;
                    var items = new JSONArray();
                    foreach (var item in gear)
                    {
                        var row = new JSONObject(); row["slot"] = item.slot; row["id"] = item.id;
                        row["material"] = LoadoutPresetPrimitiveKey(item.equipment); items.Add(row);
                    }
                    evidence["gear"] = items;
                    request.Retirement = new FiniteActionRecord { Identity = retirementId, State = FiniteActionState.Prepared,
                        Before = evidence.ToString(), After = "selected", Epoch = identity.Acquisition,
                        Domains = (int)(ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.NativeSettings | ExecutionDomain.Inventory) };
                }
                _manualFightPreparationRequest = request;
                return ContinueManualFightPreparation();
            }
            catch (Exception error) { return "Manual fight prep: preparation blocked: " + error.Message; }
        }

        private string ContinueManualFightPreparation()
        {
            var request = _manualFightPreparationRequest;
            if (request == null) return "Manual fight prep: no identified request is pending.";
            try
            {
                if (request.CancellationRequested)
                {
                    ResetCustomPreRebirthPrep();
                    return "Manual fight prep: pending request cancelled.";
                }
                if (!request.Epoch.Matches() || !ReferenceEquals(request.Settings, Settings) || request.PlanFile != Settings.CustomPlanFile
                    || request.Gear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != request.Material[i]).Any())
                    return "Manual fight prep: acquired request changed; review is required.";
                if (request.Retirement.Dirty) FiniteActions.Save(request.Retirement);
                if (request.Retirement.State == FiniteActionState.Prepared)
                {
                    if (request.BossProcess == null && (Character.bossController.isFighting || Character.bossController.nukeBoss))
                        return "Manual fight prep: waiting for the current native boss owner to retire.";
                    if (!FiniteActions.FlushDependentResults(request.Identity, request.Identity.Acquisition, request.Retirement.Domains)
                        || FiniteActions.HasUnresolvedDependencies(request.Identity, request.Identity.Acquisition, request.Retirement.Domains))
                        return "Manual fight prep: previous action evidence requires storage/review.";
                    FiniteActions.Save(request.Retirement);
                    request.Retirement.State = FiniteActionState.Invocation;
                    request.Retirement.After = "retirement-authorized";
                    FiniteActions.Save(request.Retirement);
                }
                if (request.Retirement.State == FiniteActionState.Invocation)
                {
                    if (_bossPushProcess != null && !ReferenceEquals(_bossPushProcess, request.BossProcess))
                        return "Manual fight prep: a different boss process owns the current prerequisites.";
                    if (_manualTitanActive && !ReferenceEquals(_manualTitanProcess, request.TitanProcess)
                        || CombatPrepActive && !ReferenceEquals(_combatPrepRun, request.CombatProcess))
                        return "Manual fight prep: a different preparation owns the current prerequisites.";
                    CancelManualTitan("manual fight preparation");
                    EndCombatPrep("cancelled", "Manual fight prep preempted combat prep.", false);
                    StopBossPush();
                    if (BossPushRunning || _manualTitanActive || CombatPrepActive || Character.bossController.isFighting
                        || Character.bossController.nukeBoss || Character.adventure.zone != -1)
                        return "Manual fight prep: waiting for exact previous process retirement.";
                    if (ReferenceEquals(_fightPreparationRecord, request.Previous)) ReleaseFightPreparation();
                    if (_fightPreparationClaim == null)
                    {
                        // Reserve the now-retired footprint while result storage is
                        // pending. No setup is applied before the tombstone is durable.
                        ExecutionClaim next = null;
                        next = BeginExecutionClaim(request.Identity.Owner, request.Identity.Acquisition,
                            ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.FiniteAction | ExecutionDomain.Inventory,
                            () => next == null || ReferenceEquals(_fightPreparationClaim, next)
                                && (ReferenceEquals(_manualFightPreparationRequest, request) || _fightPreparationRecord?.Identity.Key == request.Identity.Key));
                        _fightPreparationClaim = next;
                    }
                    if (request.Previous != null)
                    {
                        request.Previous.State = FiniteActionState.Committed;
                        request.Previous.After = "superseded:" + request.Identity.Key;
                        FiniteActions.Save(request.Previous);
                    }
                    request.Retirement.State = FiniteActionState.Committed;
                    request.Retirement.After = "retired";
                    FiniteActions.Save(request.Retirement);
                }
                if (!PreparationRetirementDurable(request.Retirement)) return "Manual fight prep: handoff outcome requires review.";
                // The original request is now durably superseded, not a completed
                // rebirth instruction which could be resurrected after reload.
                _manualRebirthPending = false; _manualRebirthNumber = null;
                _customPreRebirthPrepRunning = _customPreRebirthPrepCompleted = false;
                if (_customSustainedFocusGoal.HasValue || _focusSetupSelection != null) ClearPersistedExecutionSelection("focus");
                ClearCustomSustainedFocus();
                _fightPreparationGear = request.Gear; _fightPreparationSupport = request.Support; _fightPreparationUpgradesDone = true;
                return RunFightPrep(request.Plan, false, false, "Manual fight prep", () => _manualFightPreparationRequest = null, request.Identity.Opportunity);
            }
            catch (Exception error) { return "Manual fight prep: handoff/setup storage is pending: " + error.Message; }
        }

        internal string RunManualRebirth()
        {
            if (_manualFightPreparationRequest != null) return "Manual fight preparation handoff is pending; no rebirth was admitted.";
            if (!_manualRebirthPending)
            {
                if (string.IsNullOrEmpty(CurrentExecutionCommandId)) return "Manual rebirth requires an identified explicit request.";
                _manualRebirthOpportunity = CurrentExecutionCommandId;
                _manualRebirthNumber = Character.stats.rebirthNumber;
            }
            CancelManualTitan("manual rebirth");
            EndCombatPrep("cancelled", "Manual rebirth preempted combat prep.", false);
            ClearCustomSustainedFocus();
            _manualRebirthPending = true;
            return ContinueManualRebirth();
        }

        private string ContinueManualRebirth()
        {
            if (_manualRebirthNumber.HasValue && Character.stats.rebirthNumber != _manualRebirthNumber.Value)
            {
                var acquisition = "rebirth:" + ActionNumber(_manualRebirthNumber.Value);
                var identity = new FiniteActionIdentity("rebirth", "rebirth", acquisition, acquisition, "engage");
                try
                {
                    var result = FiniteActions.Find(identity);
                    if (result?.State == FiniteActionState.Committed && result.Dirty) FiniteActions.Save(result);
                    if (!KnownRebirthResultCanFinish(result, _manualRebirthNumber.Value, Character.stats.rebirthNumber))
                        return "Manual rebirth outcome requires review; the original native engagement will not be repeated.";
                    CommitExecutionRebirthCheckpoint();
                    ResetCustomPreRebirthPrep();
                    ResetCustomAutomationRuntime();
                    return "Manual rebirth engaged";
                }
                catch (Exception error) { return "Manual rebirth result/checkpoint storage is pending: " + error.Message; }
            }
            var plan = CustomPlanStore?.Load(Settings.CustomPlanFile) ?? new CustomPlan();

            if (_customPreRebirthPrepRunning)
            {
                if (BossPushRunning || CustomBossFightActive())
                    return "Manual rebirth prep is still fighting bosses; rebirth will happen automatically when boss push finishes.";

                _customPreRebirthPrepRunning = false;
                _customPreRebirthPrepCompleted = true;
                ReleaseFightPreparation();
                Log("Manual rebirth prep complete");
            }

            if (!_customPreRebirthPrepCompleted)
            {
                var prepSummary = RunFightPrep(plan, true, true, "Manual rebirth prep", opportunity: _manualRebirthOpportunity);
                if (!_fightPreparationStarted) return prepSummary;
                _customPreRebirthPrepRunning = BossPushRunning;
                _customPreRebirthPrepCompleted = !_customPreRebirthPrepRunning;

                if (_customPreRebirthPrepRunning)
                    return prepSummary + "; boss push running, rebirth will happen automatically when it stops.";
            }

            return TryEngageManualRebirth();
        }

        private string TryEngageManualRebirth()
        {
            if (Character.bossController.isFighting || Character.bossController.nukeBoss)
                return "Manual rebirth waiting for the boss fight to fully stop.";

            var rebirth = new ManualRebirth();
            if (!rebirth.RebirthAvailable())
            {
                _manualRebirthPending = false;
                ResetCustomPreRebirthPrep();
                return "Manual rebirth unavailable; minimum rebirth time or challenge state is blocking it.";
            }

            if (_profile.DoRebirth(rebirth))
            {
                CommitExecutionRebirthCheckpoint();
                _manualRebirthPending = false;
                ResetCustomPreRebirthPrep();
                ResetCustomAutomationRuntime();
                return "Manual rebirth engaged";
            }

            return "Manual rebirth delayed by final pre-rebirth checks; will retry automatically.";
        }

        private void ProcessPendingManualRebirth()
        {
            if (_manualFightPreparationRequest != null)
            {
                settingsForm?.ReportCustomStatus(ContinueManualFightPreparation());
                return;
            }
            if (!_manualRebirthPending)
                return;

            var message = ContinueManualRebirth();
            settingsForm?.ReportCustomStatus(message);
        }

        internal void ResetCustomPreRebirthPrep()
        {
            if (_manualFightPreparationRequest != null)
            {
                var request = _manualFightPreparationRequest;
                request.CancellationRequested = true;
                if (request.Retirement.State == FiniteActionState.Prepared)
                {
                    request.Retirement.State = FiniteActionState.Rejected;
                    request.Retirement.After = "cancelled-before-retirement";
                    FiniteActions.Save(request.Retirement);
                }
                else
                {
                    // Withdraw the desired request, never erase unresolved native
                    // retirement evidence or convert it back into an uninvoked action.
                    var id = new FiniteActionIdentity("preparation-handoff-cancel", request.Identity.Owner,
                        request.Identity.Acquisition, request.Retirement.Identity.Key, request.Identity.Key);
                    FiniteActions.Save(new FiniteActionRecord { Identity = id, State = FiniteActionState.Committed,
                        Before = request.Retirement.Identity.Key, After = "cancelled", Epoch = request.Identity.Acquisition,
                        Domains = request.Retirement.Domains });
                }
                _manualFightPreparationRequest = null;
            }
            _manualRebirthPending = false;
            _manualRebirthNumber = null;
            _customPreRebirthPrepRunning = false;
            _customPreRebirthPrepCompleted = false;
            StopBossPush();
            if (!Character.bossController.isFighting && !Character.bossController.nukeBoss)
                ReleaseFightPreparation();
        }

        private string RunFightPrep(CustomPlan plan, bool upgradeDiggers, bool consumePowerFruits, string summaryPrefix,
            Action onBossPushStarted = null, string opportunity = null)
        {
            plan = plan ?? new CustomPlan();
            try
            {
                var requested = ActionIdentity("fight-preparation", "fight-preparation", opportunity ?? "automatic:" + Settings.CustomPlanFile);
                if (_fightPreparationRecord != null && _fightPreparationRecord.Identity.Key != requested.Key)
                    return summaryPrefix + ": the previous identified preparation must retire first.";
                if (_fightPreparationRecord == null)
                {
                    var existing = FiniteActions.Find(requested);
                    if (existing != null && !PreparationAcquisitionCanRetry(existing))
                        return summaryPrefix + ": retained preparation requires reviewed setup rebind; no fight or fruit was repeated.";
                    if (existing != null) _fightPreparationRecord = existing;
                    else
                    {
                        var commitment = new JSONObject(); commitment["version"] = 1;
                        commitment["started"] = _manualFightPreparationRequest?.Identity.Key == requested.Key
                            ? _manualFightPreparationRequest.Started : Character.rebirthTime.totalseconds;
                        commitment["entry"] = _manualFightPreparationRequest?.Identity.Key == requested.Key
                            ? _manualFightPreparationRequest.Entry : ExecutionPersistenceEntry ?? "";
                        commitment["plan"] = Settings.CustomPlanFile;
                        commitment["manualRebirth"] = _manualRebirthPending;
                        commitment["rebirthNumber"] = ActionNumber(Character.stats.rebirthNumber);
                        commitment["opportunity"] = requested.Opportunity;
                        commitment["consumePowerFruits"] = consumePowerFruits;
                        var fruitHarvests = new JSONObject();
                        if (consumePowerFruits)
                            foreach (var fruit in new[] { YggdrasilManager.FruitOfPowerAlphaId, YggdrasilManager.FruitOfPowerBetaId })
                            {
                                if (Character.yggdrasil?.fruits == null || fruit >= Character.yggdrasil.fruits.Count)
                                    throw new InvalidOperationException("Original preparation fruit identities are unavailable.");
                                fruitHarvests[ActionNumber(fruit)] = Character.yggdrasil.fruits[fruit].harvests;
                            }
                        commitment["fruitHarvests"] = fruitHarvests;
                        commitment["upgradeDiggers"] = upgradeDiggers;
                        commitment["requireMaxedFruit"] = plan.PrepRequireMaxedFruit;
                        commitment["fightWindowSeconds"] = BossPushFightWindow;
                        commitment["gearGoal"] = SetupForFocus(CustomFocusGoal.Fight).Stage.GearGoal;
                        commitment["fightScoreMode"] = plan.FightScoreMode;
                        _fightPreparationRecord = new FiniteActionRecord { Identity = requested, State = FiniteActionState.Prepared,
                            Before = commitment.ToString(), After = "acquired", Epoch = requested.Acquisition,
                            Domains = (int)(ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.Inventory) };
                        FiniteActions.Save(_fightPreparationRecord);
                    }
                }
                var acquired = JsonFiles.ParseObject(_fightPreparationRecord.Before);
                PreparationContinuesRebirth(_fightPreparationRecord);
                if (_fightPreparationRecord.Dirty) FiniteActions.Save(_fightPreparationRecord);
                if (_fightPreparationSetup == null)
                {
                    if (!_fightPreparationUpgradesDone)
                    {
                        if (acquired["upgradeDiggers"].AsBool) PurchaseActionDiggers(true, 0d);
                        _fightPreparationUpgradesDone = true;
                    }
                    ResetCustomIronPillPrep();
                    if (_fightPreparationGear == null)
                        _fightPreparationGear = SelectSetupGear((GearOptimizationGoal)acquired["gearGoal"].AsInt, (FightScoreMode)acquired["fightScoreMode"].AsInt);
                    if (_fightPreparationSupport == null) _fightPreparationSupport = SelectFocusSupport(SetupForFocus(CustomFocusGoal.Fight));
                    if (Character.buttons.beards.interactable && !_fightPreparationSupport.Beards.Contains(BeardFuManchu))
                    {
                        _fightPreparationSupport = null;
                        return summaryPrefix + ": required fight beard does not fit.";
                    }
                    if (_fightPreparationClaim == null)
                        _fightPreparationClaim = BeginExecutionClaim(requested.Owner, requested.Acquisition, ExecutionDomain.Gear | ExecutionDomain.Support
                            | ExecutionDomain.Route | ExecutionDomain.FiniteAction | ExecutionDomain.Inventory, () => _fightPreparationRecord != null);
                    _fightPreparationSetup = CreateFiniteActionSetup(_fightPreparationClaim, _fightPreparationGear, _fightPreparationSupport, true, true,
                        Character.buttons.beards.interactable ? new[] { BeardFuManchu } : new int[0]);
                }
                if (!FightPreparationVerified || _fightPreparationRecord.After == "acquired" || _fightPreparationRecord.After == "selected")
                {
                    // Selection is retained even when storage or native preflight fails.
                    // The ledger stage is durable before any native setup mutation.
                    PersistExecutionSetupSelection("fight-prep", _fightPreparationRecord.Before, _fightPreparationSetup);
                    _fightPreparationRecord.After = "selected"; FiniteActions.Save(_fightPreparationRecord);
                    ApplyExecutionClaim(_fightPreparationClaim, _fightPreparationSetup);
                    _fightPreparationRecord.After = "setup-ready"; FiniteActions.Save(_fightPreparationRecord);
                    if (!FightPreparationVerified) return summaryPrefix + ": waiting for verified exact fight setup.";
                }
                if (!_fightPreparationStarted && acquired["consumePowerFruits"].AsBool)
                {
                    if (!acquired["fruitHarvests"].IsObject)
                        return summaryPrefix + ": original fruit identities require review; no new fruit opportunity was acquired.";
                    foreach (var fruit in new[] { YggdrasilManager.FruitOfPowerAlphaId, YggdrasilManager.FruitOfPowerBetaId })
                    {
                        var controller = Character.yggdrasilController.fruits[0];
                        if (controller.harvestTier(fruit) < 1 || acquired["requireMaxedFruit"].AsBool && !controller.fruitMaxxed(fruit)) continue;
                        var count = acquired["fruitHarvests"][ActionNumber(fruit)];
                        if (!count.IsNumber || count.AsDouble < 0d || count.AsDouble > int.MaxValue || count.AsDouble != count.AsInt)
                            return summaryPrefix + ": original fruit identity is invalid; boss entry withheld.";
                        if (!RunFruitHarvestAction(fruit, acquired["requireMaxedFruit"].AsBool, true, _fightPreparationClaim, _fightPreparationSetup,
                            PreparationFruitOpportunity(_fightPreparationRecord.Identity, fruit), count.AsInt))
                            return summaryPrefix + ": fruit action is unresolved; boss entry withheld.";
                    }
                }
                if (!FightPreparationVerified) return summaryPrefix + ": fight prerequisites changed.";
                if (!_fightPreparationStarted)
                {
                    _fightPreparationRecord.After = "fight-requested";
                    FiniteActions.Save(_fightPreparationRecord);
                    StartBossPush();
                    if (!BossPushRunning) return summaryPrefix + ": boss process admission is pending.";
                    _fightPreparationStarted = true;
                    onBossPushStarted?.Invoke();
                }
                return summaryPrefix + ": boss push running.";
            }
            catch (Exception e)
            {
                return summaryPrefix + ": preparation blocked: " + e.Message;
            }
        }

        private void ReleaseFightPreparation()
        {
            var claim = _fightPreparationClaim;
            if (claim != null) EndExecutionClaim(claim, _manualFightPreparationRequest == null);
            if (_fightPreparationRecord != null && _fightPreparationStarted)
            {
                _fightPreparationRecord.State = FiniteActionState.Committed;
                _fightPreparationRecord.After = "completed";
                FiniteActions.Save(_fightPreparationRecord);
                _completedFightPreparationRecord = _fightPreparationRecord;
            }
            _fightPreparationClaim = null; _fightPreparationSetup = null;
            _fightPreparationGear = null; _fightPreparationSupport = null; _fightPreparationUpgradesDone = false;
            _fightPreparationRecord = null; _fightPreparationStarted = false;
        }

        internal JSONObject ExportFightPreparationCommitment()
        {
            var record = _fightPreparationRecord ?? _completedFightPreparationRecord;
            if (record == null) return new JSONObject();
            var id = record.Identity;
            var result = new JSONObject(); result["version"] = 1;
            result["kind"] = id.Kind; result["owner"] = id.Owner; result["acquisition"] = id.Acquisition;
            result["entry"] = id.Entry; result["opportunity"] = id.Opportunity;
            result["state"] = record.State.ToString(); result["stage"] = record.After;
            result["original"] = JsonFiles.ParseObject(record.Before);
            return result;
        }

        internal void ImportReviewedFightPreparation(JSONObject commitment, SelectedSetupRequirements setup)
        {
            if (commitment == null || commitment["version"].AsInt != 1 || setup == null)
                throw new InvalidOperationException("Fight preparation semantic commitment is unavailable.");
            var id = new FiniteActionIdentity(commitment["kind"].Value, commitment["owner"].Value,
                commitment["acquisition"].Value, commitment["entry"].Value, commitment["opportunity"].Value);
            var record = FiniteActions.Find(id);
            if (id.Kind != "fight-preparation" || record == null || record.Before != commitment["original"].ToString()
                || setup.Owner != id.Owner || setup.Acquisition != id.Acquisition
                || id.Acquisition != "rebirth:" + ActionNumber(Character.stats.rebirthNumber))
                throw new InvalidOperationException("Fight preparation history or selected acquisition changed.");
            if (record.After.StartsWith("superseded:", StringComparison.Ordinal)
                || FiniteActions.Records.Any(x => PreparationSupersessionBlocksImport(x, id)))
                throw new InvalidOperationException("Original preparation was superseded or its handoff requires review; old rebirth intent was not resumed.");
            var original = JsonFiles.ParseObject(record.Before);
            var continuesRebirth = PreparationContinuesRebirth(record);
            if (original["plan"].Value != Settings.CustomPlanFile
                || original["entry"].Value != (ExecutionPersistenceEntry ?? "")
                || !(original["started"].AsDouble >= 0d && original["started"].AsDouble <= Character.rebirthTime.totalseconds))
                throw new InvalidOperationException("Fight preparation original entry/time does not match; no new budget was created.");
            if (record.State == FiniteActionState.Unknown || record.State == FiniteActionState.Invocation || record.After == "fight-requested")
                throw new InvalidOperationException("Started fight preparation requires route-history rebind; entry fight will not be repeated.");
            if (record.State == FiniteActionState.Committed)
            {
                _completedFightPreparationRecord = record;
                _manualRebirthPending = original["manualRebirth"].AsBool;
                _manualRebirthOpportunity = original["opportunity"].Value;
                _manualRebirthNumber = _manualRebirthPending ? Character.stats.rebirthNumber : (long?)null;
                _customPreRebirthPrepRunning = false;
                _customPreRebirthPrepCompleted = continuesRebirth;
                return;
            }
            if (_fightPreparationClaim != null) throw new InvalidOperationException("A fight preparation is already admitted.");
            ExecutionClaim rebound = null;
            rebound = BeginExecutionClaim(id.Owner, id.Acquisition, ExecutionDomain.Gear | ExecutionDomain.Support
                | ExecutionDomain.Route | ExecutionDomain.FiniteAction | ExecutionDomain.Inventory,
                () => rebound == null || ReferenceEquals(_fightPreparationClaim, rebound) && ReferenceEquals(_fightPreparationRecord, record));
            _fightPreparationRecord = record;
            _fightPreparationClaim = rebound;
            setup.Authorized = () => ExecutionClaimCurrent(_fightPreparationClaim);
            _fightPreparationSetup = setup;
            _fightPreparationGear = setup.Gear; _fightPreparationSupport = setup.Support; _fightPreparationUpgradesDone = true;
            _manualRebirthPending = original["manualRebirth"].AsBool;
            _manualRebirthOpportunity = original["opportunity"].Value;
            _manualRebirthNumber = _manualRebirthPending ? Character.stats.rebirthNumber : (long?)null;
            _fightPreparationStarted = _customPreRebirthPrepRunning = _customPreRebirthPrepCompleted = false;
        }
    }
}
