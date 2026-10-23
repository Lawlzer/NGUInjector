using System;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private const float BossPushFightWindow = 3.0f;
        private const float BossPushTickSeconds = 0.1f;
        private bool _bossPushRunning, _bossPushNukeStarted, _bossPushStopping;
        private float _bossPushFightSecondsRemaining;
        private RouteProcess _bossPushProcess;
        private string _bossPushIdentity;
        private int _bossPushFightOrdinal;

        internal enum RouteActionKind { BossNuke, BossFight, CombatOperatingSystem }
        internal sealed class RouteActionResult
        {
            internal string Identity;
            internal RouteActionKind Kind;
            internal bool Invoked;
            internal MutationDisposition Disposition;
            internal Exception Error;
            internal bool Committed => Disposition == MutationDisposition.AppliedVerified
                || Disposition == MutationDisposition.ModelAppliedRenderFailed;
        }
        private sealed class NativeRouteAction : FiniteAction
        {
            internal Character Model;
            internal object Boss, Wandoos;
            internal RouteActionKind Kind;
            internal int Os, BossId;
            internal double Bootup;
            internal string Accounting;
            internal bool Invoked;
            internal override bool Prerequisites() => ReferenceEquals(Character, Model)
                && ReferenceEquals(Model.bossController, Boss) && ReferenceEquals(Model.wandoos98, Wandoos)
                && Model.adventure.zone == -1 && !Model.bossController.isFighting && !Model.bossController.nukeBoss;
            internal override string Observe() => Model.bossID + ":" + Model.bossController.isFighting + ":"
                + Model.bossController.nukeBoss + ":" + (int)Model.wandoos98.os + ":"
                + ActionNumber(Model.wandoos98.bootupTime.totalseconds) + ":" + ResourceWriter.AllocatedResourceKey();
            internal override void Invoke()
            {
                Invoked = true;
                if (Kind == RouteActionKind.BossNuke) Model.bossController.startNuke();
                else if (Kind == RouteActionKind.BossFight) Model.bossController.beginFight();
                else ResourceWriter.Execute(() => Model.wandoos98.changeOS((OSType)Os));
            }
            internal override MutationDisposition Classify(string before, string after, Exception error)
            {
                var same = ReferenceEquals(Character, Model) && ReferenceEquals(Model.bossController, Boss)
                    && ReferenceEquals(Model.wandoos98, Wandoos);
                var effect = Kind == RouteActionKind.BossNuke ? Model.bossController.nukeBoss || Model.bossID > BossId
                    : Kind == RouteActionKind.BossFight ? Model.bossController.isFighting || Model.bossID > BossId
                    : (int)Model.wandoos98.os == Os && Model.wandoos98.bootupTime.totalseconds == Bootup
                        && !CombatPrepHasNativeWandoosWork() && ResourceWriter.AllocatedResourceKey() == Accounting;
                return same && effect ? error == null ? MutationDisposition.AppliedVerified
                    : MutationDisposition.ModelAppliedRenderFailed : MutationDisposition.UnknownOutcome;
            }
        }

        // Finite-action integration contract: caller supplies stable process/action identity;
        // this leaf owns typed native invocation and action-specific effect proof, never replay.
        private RouteActionResult InvokeRouteAction(string identity, ExecutionGrant grant, RouteActionKind kind, int os = 0)
        {
            if (string.IsNullOrEmpty(identity)) throw new ArgumentException("Route actions require stable identity.");
            if (grant == null) return new RouteActionResult { Identity = identity, Kind = kind, Disposition = MutationDisposition.RejectedUnchanged };
            PersistRouteExecutionCommitments();
            var action = new NativeRouteAction { Kind = kind, Os = os, Model = Character, Boss = Character.bossController,
                Wandoos = Character.wandoos98, BossId = Character.bossID, Bootup = Character.wandoos98.bootupTime.totalseconds,
                Accounting = ResourceWriter.AllocatedResourceKey(), Footprint = grant.Domains,
                Identity = new FiniteActionIdentity("route-" + kind, grant.Owner, grant.Acquisition, identity, identity) };
            var result = RunFiniteAction(action, grant);
            return new RouteActionResult { Identity = identity, Kind = kind,
                Invoked = action.Invoked || result.Committed || result.Disposition == MutationDisposition.UnknownOutcome,
                Disposition = result.Committed && !result.Verified ? MutationDisposition.UnknownOutcome : result.Disposition,
                Error = result.Reason == null ? null : new InvalidOperationException(result.Reason) };
        }

        internal bool BossPushRunning => _bossPushRunning;

        internal void StartBossPush()
        {
            if (_bossPushRunning || _bossPushProcess != null) return;
            if (_routeCommitmentIssue != null) throw new InvalidOperationException(_routeCommitmentIssue);
            CancelManualTitan("boss push");
            if (_manualTitanActive) throw new InvalidOperationException("Manual titan route must retire before boss push.");
            ResetManualAdventureSetup();
            ResetCustomIronPillPrep();
            if (FightPreparationClaim != null)
            {
                if (!FightPreparationVerified || Character.adventure.zone != -1)
                    throw new InvalidOperationException("Fight preparation has not verified its exact retired setup.");
                var process = new RouteProcess { Owner = "boss-push", Claim = FightPreparationClaim, Setup = FightPreparationSetup,
                    Zone = -1, Objects = CombatPrepAdventureRouteObjects(), AutoAttack = Character.adventure.autoattacking,
                    BeastMode = Character.adventure.beastModeOn, Applied = true, Admitted = true, BorrowedPreparation = true };
                _bossPushProcess = process;
                process.SelectedMaterial = process.Setup.Gear == null ? null
                    : Array.ConvertAll(process.Setup.Gear, item => LoadoutPresetPrimitiveKey(item.equipment));
                process.SettingsClaim = BeginExecutionClaim("boss-settings", FightPreparationClaim.Acquisition,
                    ExecutionDomain.NativeSettings, () => ReferenceEquals(_bossPushProcess, process)
                        && ReferenceEquals(FightPreparationClaim, process.Claim) && ReferenceEquals(FightPreparationSetup, process.Setup));
                _bossPushIdentity = FightPreparationIdentity;
            }
            else
            {
                var gear = LoadoutManager.CaptureExactLoadout();
                var support = SelectFocusSupport(SetupForFocus(CustomFocusGoal.Fight));
                if (!RetireRoutesForSuccessor()) return;
                _bossPushProcess = BeginRouteProcess("boss-push", gear, false,
                    ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.NativeSettings | ExecutionDomain.FiniteAction,
                    support);
                _bossPushIdentity = _bossPushProcess.Claim.Acquisition;
            }
            _bossPushFightOrdinal = 0;
            _bossPushRunning = true; _bossPushStopping = _bossPushNukeStarted = false;
            _bossPushProcess.Permission = RoutePermissionKey("boss-push");
            _bossPushFightSecondsRemaining = BossPushFightWindow;
            PersistRouteExecutionCommitments();
            Log("Starting boss push");
        }

        internal void StopBossPush()
        {
            var active = _bossPushRunning || _bossPushProcess != null;
            _bossPushStopping = true;
            if (!EndRouteProcess(_bossPushProcess)) return;
            if (_bossPushProcess?.SettingsClaim != null) EndExecutionClaim(_bossPushProcess.SettingsClaim, false);
            var preparation = _bossPushProcess?.BorrowedPreparation == true;
            _bossPushProcess = null;
            _bossPushRunning = _bossPushNukeStarted = _bossPushStopping = false;
            _bossPushFightSecondsRemaining = 0;
            if (preparation) CompleteFightPreparation();
            if (active) PersistRouteExecutionCommitments();
        }

        private void BossPushTick()
        {
            try
            {
                if (!_bossPushRunning) return;
                if (_bossPushStopping) { StopBossPush(); return; }
                var boss = Character.bossController;
                if (!_bossPushProcess.Applied && !ApplyRouteProcess(_bossPushProcess)) return;
                if (!RouteProcessReady(_bossPushProcess)) { StopBossPush(); return; }
                if (!_bossPushNukeStarted)
                {
                    if (boss.isFighting || boss.nukeBoss) return;
                    var result = InvokeRouteAction(_bossPushIdentity + ":nuke", RouteProcessGrant(_bossPushProcess), RouteActionKind.BossNuke);
                    if (!result.Committed) { StopBossPush(); return; }
                    _bossPushNukeStarted = true;
                    PersistRouteExecutionCommitments();
                    return;
                }
                if (boss.nukeBoss) return;
                if (Character.curHP <= 0 || _bossPushFightSecondsRemaining <= 0)
                {
                    StopBossPush();
                    if (!_bossPushRunning) ProcessPendingManualRebirth();
                    return;
                }
                _bossPushFightSecondsRemaining -= BossPushTickSeconds;
                if (boss.isFighting) return;
                var fight = InvokeRouteAction(_bossPushIdentity + ":fight:" + _bossPushFightOrdinal,
                    RouteProcessGrant(_bossPushProcess), RouteActionKind.BossFight);
                if (!fight.Committed) { StopBossPush(); return; }
                _bossPushFightOrdinal++;
                PersistRouteExecutionCommitments();
            }
            catch (Exception error)
            {
                LogError("BossPushTick", error);
                StopBossPush();
                if (!_bossPushRunning) ProcessPendingManualRebirth();
            }
        }
    }
}
