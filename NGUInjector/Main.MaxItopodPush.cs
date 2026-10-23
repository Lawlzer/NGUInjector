using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private int _maxItopodPushRemainingDeaths;
        private int _maxItopodPushFloor;
        private bool _maxItopodPushRouting;
        private string _maxItopodPushLastDeath;
        private List<string> _maxItopodPushLog;
        private int _maxItopodPushSavedStart;
        private int _maxItopodPushSavedEnd;
        private bool _maxItopodPushSavedShifter;
        private LoadoutManager.SnapshotEpoch _maxItopodPushEpoch;
        private bool _maxItopodPushFromAdventureMode;
        private RouteProcess _maxItopodProcess;
        private int _maxItopodOwnedStart, _maxItopodOwnedEnd;
        private bool _maxItopodOwnedShifter;
        private object _maxItopodBorrowedModel, _maxItopodBorrowedArbitrary;
        private bool _maxItopodHistoryLost;
        private ih[] _maxItopodSelectedGear;
        private string[] _maxItopodSelectedMaterial;
        private SupportSelection _maxItopodSelectedSupport;

        private void SynchronizeMaxItopodAdventureMode()
        {
            if ((_maxItopodPushFromAdventureMode || _maxItopodPushRouting || _maxItopodProcess != null) && _maxItopodPushRemainingDeaths <= 0)
            {
                // Retry an interrupted cancellation, never replenish a completed run.
                CancelMaxItopodPush();
                return;
            }

            if ((Settings.ManualAdventureEnabled || Settings.CustomAdventureEnabled)
                && Settings.CustomAdventureMode == CustomAdventureModeMaxItopodPush)
            {
                // Selecting this mode adopts an existing command-started run without resetting its budget.
                _maxItopodPushFromAdventureMode = true;
                if (_maxItopodPushRemainingDeaths > 0)
                    return;
                try { StartMaxItopodPush(Settings.CustomAdventureMaxItopodDeaths); }
                catch
                {
                    CancelMaxItopodPush();
                    throw;
                }
            }
            else if (_maxItopodPushFromAdventureMode)
            {
                CancelMaxItopodPush();
            }
        }

        private string StartMaxItopodPush(int deaths)
        {
            if (_routeCommitmentIssue != null) throw new InvalidOperationException(_routeCommitmentIssue);
            RequireCombatPrepIdle();
            if (deaths < 1 || deaths > 1000)
                throw new ArgumentOutOfRangeException(nameof(deaths), "Death limit must be between 1 and 1000.");
            if (_maxItopodPushRemainingDeaths > 0 || _maxItopodProcess != null)
                throw new InvalidOperationException("A max ITOPOD push is already active; cancel it before starting another.");
            if (!Settings.CustomAdventureEnabled && !Settings.ManualAdventureEnabled)
                throw new InvalidOperationException("Enable Adventure automation or Manual Adventure before starting a max ITOPOD push.");

            ObserveCustomRebirthEpoch();
            var controller = Character.adventureController;
            var log = controller.log;
            var entries = log?.GetType().GetField("Eventlog", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(log) as List<string>;
            if (entries == null)
                throw new InvalidOperationException("Adventure death log is unavailable; cannot safely limit the push.");

            var floor = Math.Min(controller.maxItopodLevel(), Math.Max(0, Character.adventure.highestItopodLevel - 1));
            var message = $"Max ITOPOD push started for {deaths} deaths; "
                + (_maxItopodPushFromAdventureMode ? "Adventure mode will switch to Optimal ITOPOD afterward." : "saved adventure settings are unchanged.");
            Log(message);
            _maxItopodPushLog = entries;
            _maxItopodPushLastDeath = null;
            _maxItopodHistoryLost = false;
            _maxItopodSelectedGear = null; _maxItopodSelectedSupport = null; _maxItopodSelectedMaterial = null;
            ObserveMaxItopodPushDeaths(); // Establish a baseline; old deaths never count.
            _maxItopodPushSavedStart = Character.adventure.itopodStart;
            _maxItopodPushSavedEnd = Character.adventure.itopodEnd;
            _maxItopodPushSavedShifter = Character.arbitrary.lazyITOPODOn;
            _maxItopodPushEpoch = new LoadoutManager.SnapshotEpoch();
            _maxItopodBorrowedModel = Character.adventure;
            _maxItopodBorrowedArbitrary = Character.arbitrary;
            _maxItopodPushRemainingDeaths = deaths;
            _maxItopodPushFloor = floor;
            PersistRouteExecutionCommitments();
            SetCustomAutomationStatus(message);
            return message;
        }

        private void ObserveMaxItopodPushDeaths()
        {
            if (_maxItopodPushLog == null)
                return;

            var deaths = 0;
            string newest = null;
            var log = Character.adventureController.log;
            var entries = log?.GetType().GetField("Eventlog", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(log);
            var found = _maxItopodPushLastDeath == null;
            if (!ReferenceEquals(entries, _maxItopodPushLog)) { _maxItopodHistoryLost = true; CancelMaxItopodPush(); return; }
            for (var i = _maxItopodPushLog.Count - 1; i >= 0; i--)
            {
                var entry = _maxItopodPushLog[i];
                if (entry == null) continue;
                // PlayerLog creates a fresh string for every death, even to the same enemy.
                if (ReferenceEquals(entry, _maxItopodPushLastDeath))
                { found = true; break; }
                if (newest == null)
                    newest = entry;
                if (entry.EndsWith(" has defeated you!", StringComparison.Ordinal)) deaths++;
            }
            if (!found) { _maxItopodHistoryLost = true; CancelMaxItopodPush(); return; }
            if (newest != null)
                _maxItopodPushLastDeath = newest;

            if (!_maxItopodPushRouting || Character.adventure.zone != 1000 && Character.adventure.zone != -1 || deaths == 0)
                return;

            _maxItopodPushRemainingDeaths = Math.Max(0, _maxItopodPushRemainingDeaths - deaths);
            PersistRouteExecutionCommitments();
            if (Character.adventure.zone == -1 && _maxItopodProcess?.Zone == 1000 && MaxItopodSettingsOwned()
                && ExecutionClaimSetupOwned(_maxItopodProcess.Claim)) _maxItopodProcess.Zone = -1;
            if (_maxItopodPushRemainingDeaths == 0)
            {
                var modeOwned = _maxItopodPushFromAdventureMode;
                CancelMaxItopodPush();
                SetCustomAutomationStatus(modeOwned
                    ? "Max ITOPOD push complete; Adventure mode switched to Optimal ITOPOD."
                    : "Max ITOPOD push complete; resuming selected adventure settings.");
            }
            else
            {
                // Re-enter at the newly unlocked frontier after healing, not the old start floor.
                PauseMaxItopodPush();
            }
        }

        private bool TryRouteMaxItopodPush()
        {
            if (_maxItopodPushRemainingDeaths <= 0)
                return false;
            if (_maxItopodPushEpoch == null || !_maxItopodPushEpoch.Matches())
            {
                CancelMaxItopodPush();
                return false;
            }
            if (!Settings.CustomAdventureEnabled && !Settings.ManualAdventureEnabled)
            {
                CancelMaxItopodPush();
                return false;
            }
            if (_maxItopodPushRouting && !MaxItopodSettingsOwned())
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "route", "Tower fields changed externally; prerequisites retained.");
                EndExecutionClaim(_maxItopodProcess?.Claim, false);
                _maxItopodProcess = null; _maxItopodPushRouting = false;
                _maxItopodPushRemainingDeaths = 0;
                return false;
            }

            var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null ? LoadRuntimeCustomPlan() : null;
            if ((_questManager != null && _questManager.IsQuesting() >= 0)
                || CustomGoalBlocksAdventureGoldPush(plan, out _))
            {
                PauseMaxItopodPush();
                return false;
            }

            var controller = Character.adventureController;
            if (!_maxItopodPushRouting)
            {
                _maxItopodPushFloor = Math.Min(controller.maxItopodLevel(), Math.Max(0, Character.adventure.highestItopodLevel - 1));
                if (_maxItopodSelectedGear == null)
                {
                    var selected = Settings.ManualAdventureEnabled && Settings.ManualAdventureLoadout >= 0
                        ? SelectSetupGear(GearOptimizationGoal.Fight, (FightScoreMode)Settings.ManualAdventureLoadout, true)
                        : LoadoutManager.CaptureExactLoadout();
                    var support = Settings.ManualAdventureEnabled ? SelectFocusSupport(SetupForManualAdventure()) : null;
                    _maxItopodSelectedGear = selected;
                    _maxItopodSelectedMaterial = selected.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                    _maxItopodSelectedSupport = support;
                }
                if (_maxItopodSelectedGear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != _maxItopodSelectedMaterial[i]).Any())
                    throw new InvalidOperationException("Acquired tower equipment changed; explicit review required.");
                var preflight = Settings.ManualAdventureEnabled && Settings.ManualAdventureLoadout >= 0
                    ? GearOptimizer.CheckExactSwapCapacity(_maxItopodSelectedGear, Character.curEnergy, Character.res3.curRes3) : null;
                if (preflight != null) { SetCustomAutomationStatus(preflight); return true; }
                if (_maxItopodProcess == null)
                {
                    if (!RetireRoutesForSuccessor()) return true;
                    _maxItopodProcess = BeginRouteProcess("max-itopod", _maxItopodSelectedGear, Settings.ManualAdventureEnabled && Settings.ManualAdventureLoadout >= 0,
                        Settings.ManualAdventureEnabled ? ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route | ExecutionDomain.NativeSettings
                            : ExecutionDomain.Route | ExecutionDomain.NativeSettings, _maxItopodSelectedSupport);
                }
                if (!ApplyRouteProcess(_maxItopodProcess)) return true;
                _maxItopodPushRouting = true;
                _maxItopodOwnedStart = Character.adventure.itopodStart;
                _maxItopodOwnedEnd = Character.adventure.itopodEnd;
                _maxItopodOwnedShifter = Character.arbitrary.lazyITOPODOn;
                ApplyMaxItopodSettings(_maxItopodPushFloor, controller.maxItopodLevel(), false);
                // Changing the floor inputs does not move a character already in the tower.
                if (Character.adventure.zone == 1000)
                    _combManager.MoveToZone(-1, RouteProcessGrant(_maxItopodProcess), () => ObserveRouteProcess(_maxItopodProcess));
            }

            if (Settings.ManualAdventureEnabled)
            {
                // Safe-Zone trips reset the game's ten-kill floor counter. Heal/precast on entry only.
                var enteringTower = Character.adventure.zone != 1000;
                _combManager.ManualZone(1000, false, enteringTower && Settings.ManualAdventureRecoverHealth,
                    enteringTower && Settings.ManualAdventurePrecastBuffs, Settings.ManualAdventureFastCombat, Settings.ManualAdventureBeastMode,
                    grant: RouteProcessGrant(_maxItopodProcess), observe: () => ObserveRouteProcess(_maxItopodProcess));
                _manualAdventureOverlayRouteZone = Character.adventure.zone == 1000 ? (int?)1000 : null;
            }
            else
            {
                _combManager.IdleZone(1000, false, true, RouteProcessGrant(_maxItopodProcess), () => ObserveRouteProcess(_maxItopodProcess));
            }
            var next = _maxItopodPushFromAdventureMode ? "switch Adventure mode to Optimal ITOPOD" : "resume selected adventure settings";
            SetCustomAutomationStatus($"Adventure: max ITOPOD push from floor {_maxItopodPushFloor}, {_maxItopodPushRemainingDeaths} deaths remaining; then {next}.");
            return true;
        }

        private void PauseMaxItopodPush()
        {
            if (!_maxItopodPushRouting)
            {
                // Acquisition may fail before the tower/borrowed fields are active.
                // That claim still needs retirement; it owns no field-return rights.
                if (!EndRouteProcess(_maxItopodProcess)) return;
                _maxItopodProcess = null;
                return;
            }
            if (_maxItopodPushEpoch == null || !_maxItopodPushEpoch.Matches())
            {
                // Drop restoration responsibility before cancellation re-enters this method.
                _maxItopodPushRouting = false;
                EndRouteProcess(_maxItopodProcess); _maxItopodProcess = null;
                CancelMaxItopodPush();
                return;
            }
            if (!MaxItopodSettingsOwned())
            {
                RecordSelectedSetupConflict(Character, Character.stats.rebirthNumber, "route", "Foreign tower fields retain their prerequisites.");
                EndExecutionClaim(_maxItopodProcess?.Claim, false);
                _maxItopodProcess = null; _maxItopodPushRouting = false; _maxItopodPushRemainingDeaths = 0;
                return;
            }
            if (Character.adventure.zone == 1000)
                if (!_combManager.MoveToZone(-1, RouteProcessGrant(_maxItopodProcess, true), () => ObserveRouteProcess(_maxItopodProcess))) return;
            if (!_routeHandoffPending && MaxItopodSettingsOwned() && RouteProcessMatches(_maxItopodProcess))
            {
                var successor = _executionBaseline;
                var hasSuccessor = successor?.Authorized?.Invoke() == true;
                var route = _globalPresetReceipt?.Execution;
                if (hasSuccessor && route != null && route.Zone == 1000)
                    ApplyMaxItopodSettings(route.Floor, route.Floor, _maxItopodPushSavedShifter
                        && route.Floor == ZoneHelpers.CalculateBestItopodLevel(), true);
                else if (!hasSuccessor && _maxItopodPushSavedStart > 0 && _maxItopodPushSavedEnd > 0)
                    ApplyMaxItopodSettings(_maxItopodPushSavedStart, _maxItopodPushSavedEnd, _maxItopodPushSavedShifter, true);
            }
            if (!EndRouteProcess(_maxItopodProcess)) return;
            _maxItopodProcess = null;
            _maxItopodPushRouting = false;
        }

        private bool MaxItopodSettingsOwned() => ReferenceEquals(_maxItopodBorrowedModel, Character.adventure)
            && ReferenceEquals(_maxItopodBorrowedArbitrary, Character.arbitrary)
            && Character.adventure.itopodStart == _maxItopodOwnedStart && Character.adventure.itopodEnd == _maxItopodOwnedEnd
            && Character.arbitrary.lazyITOPODOn == _maxItopodOwnedShifter;

        private void ApplyMaxItopodSettings(int start, int end, bool shifter, bool retirement = false)
        {
            var grant = RouteProcessGrant(_maxItopodProcess, retirement);
            var controller = Character.adventureController;
            var startText = controller.itopodStartInput.text; var endText = controller.itopodEndInput.text;
            Action require = () => { if (!ValidateExecutionGrant(grant, ExecutionDomain.Route | ExecutionDomain.NativeSettings)
                || !MaxItopodSettingsOwned() || controller.itopodStartInput.text != startText
                || controller.itopodEndInput.text != endText) throw new InvalidOperationException("Tower settings changed externally; return yielded."); };
            require(); startText = start.ToString(); controller.itopodStartInput.text = startText; require();
            endText = end.ToString(); controller.itopodEndInput.text = endText; require();
            controller.verifyItopodInputs();
            if (Character.adventure.itopodStart != start || Character.adventure.itopodEnd != end
                || Character.arbitrary.lazyITOPODOn != _maxItopodOwnedShifter
                || !ValidateExecutionGrant(grant, ExecutionDomain.Route | ExecutionDomain.NativeSettings))
                throw new InvalidOperationException("Tower input result is unverified.");
            _maxItopodOwnedStart = start; _maxItopodOwnedEnd = end;
            require();
            if (_maxItopodOwnedShifter != shifter)
            {
                controller.toggleShifter();
                if (Character.arbitrary.lazyITOPODOn != shifter || Character.adventure.itopodStart != start
                    || Character.adventure.itopodEnd != end || !ValidateExecutionGrant(grant, ExecutionDomain.Route | ExecutionDomain.NativeSettings))
                    throw new InvalidOperationException("Tower shifter result is unverified.");
                _maxItopodOwnedShifter = shifter;
            }
        }

        private string CancelMaxItopodPush()
        {
            var active = _maxItopodPushRouting || _maxItopodPushRemainingDeaths > 0 || _maxItopodPushFromAdventureMode;
            _maxItopodPushFromAdventureMode |= Settings.CustomAdventureMode == CustomAdventureModeMaxItopodPush;
            _maxItopodPushRemainingDeaths = 0;
            PauseMaxItopodPush();
            if (_maxItopodPushRouting || _maxItopodProcess != null) return "Max ITOPOD push is waiting for owned route retirement.";
            // Also disarm a mode selection cancelled before its first combat tick.
            if (Settings.CustomAdventureMode == CustomAdventureModeMaxItopodPush)
                Settings.CustomAdventureMode = CustomAdventureModeOptimalItopod;
            _maxItopodPushFromAdventureMode = false;
            _maxItopodPushFloor = 0;
            _maxItopodPushLog = null;
            _maxItopodPushLastDeath = null;
            _maxItopodPushEpoch = null;
            if (active) PersistRouteExecutionCommitments();
            const string message = "Max ITOPOD push stopped; resuming selected adventure settings.";
            SetCustomAutomationStatus(message);
            return message;
        }

        private JSONObject GetMaxItopodPushRuntime()
        {
            var state = new JSONObject();
            state["active"] = _maxItopodPushRemainingDeaths > 0;
            state["remainingDeaths"] = _maxItopodPushRemainingDeaths;
            state["historyLost"] = _maxItopodHistoryLost;
            state["floor"] = _maxItopodPushRouting && Character.adventure.zone == 1000
                ? Character.adventureController.itopodLevel : _maxItopodPushFloor;
            return state;
        }
    }
}
