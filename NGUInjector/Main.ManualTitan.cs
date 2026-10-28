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
        private sealed class ManualTitanSpawn
        {
            internal double Elapsed = double.NaN;
            internal bool Started, Disarmed, ResetSeen;
            internal int Deaths, Limit, Kills, Phase;
            internal double OwnedSeconds;
            internal string Outcome;
            internal ih[] Gear;
            internal string[] Material;
            internal SupportSelection Support;
            internal int Loadout;
            internal bool CapacityTransition;
            internal GearRecallFootprint ResourceRecall;
            internal string Acquisition;
        }

        private Dictionary<int, ManualTitanSpawn> _manualTitanSpawns = new Dictionary<int, ManualTitanSpawn>();
        private LoadoutManager.SnapshotEpoch _manualTitanEpoch;
        private bool _manualTitanActive, _manualTitanCombatOwned;
        private int _manualTitanIndex = -1;
        private RouteProcess _manualTitanProcess;
        private bool _manualTitanRecovering;
        private string _manualTitanCleanupReason, _manualTitanRecoveryError;
        private List<string> _manualTitanLog;
        private string _manualTitanLastEvent, _manualTitanEnemyName, _manualTitanConfig;
        private DateTime _manualTitanLastTick;
        private string _manualTitanStatus = "Disabled";

        private bool TryRouteManualTitan()
        {
            if (!ReferenceEquals(reference, this) || _unloaded) return false;
            if (Character == null) { CancelManualTitan("character unavailable"); return _manualTitanActive; }
            ObserveManualTitanOpportunities();
            if (Character.bossController.isFighting || Character.bossController.nukeBoss) return _manualTitanActive;
            if (_manualTitanRecovering)
            {
                CancelManualTitan(_manualTitanCleanupReason);
                return _manualTitanActive;
            }

            if (_manualTitanActive)
            {
                if (!ManualTitanClaimCurrent()) return EndManualTitanSpawn("equipment ownership lost");
                if (ManualTitanGearChanged()) return EndManualTitanSpawn(_manualTitanStatus);
                var spawn = _manualTitanSpawns[_manualTitanIndex];
                if (!ObserveManualTitanDeaths()) return EndManualTitanSpawn("death log continuity unavailable");
                if (spawn.Deaths >= spawn.Limit) return EndManualTitanSpawn("death budget exhausted");
                var kills = ManualTitanNativeInt($"titan{_manualTitanIndex + 1}Kills");
                var phase = ManualTitanNativeInt("waldoDefeats");
                if (kills > spawn.Kills || spawn.ResetSeen || _manualTitanIndex == 4 && phase > spawn.Phase)
                {
                    var outcome = _manualTitanIndex == 4 && spawn.Phase < 4
                        ? "Walderp phase complete; find him and wait for the next spawn"
                        : kills > spawn.Kills ? "target killed" : "native spawn reset";
                    return EndManualTitanSpawn(outcome);
                }
            }

            var config = $"{Settings.ManualTitanZone}:{Settings.ManualTitanLoadout}:{Settings.ManualTitanRequireAT}:{Settings.ManualTitanMaxDeaths}";
            if (_manualTitanActive && _manualTitanConfig != config)
            {
                CancelManualTitan("configuration changed");
                return _manualTitanActive;
            }
            if (!Settings.ManualTitanEnabled)
            {
                CancelManualTitan("disabled");
                return _manualTitanActive;
            }

            var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null ? LoadRuntimeCustomPlan() : null;
            if (CombatPrepActive || (_questManager != null && _questManager.IsQuesting() >= 0)
                || CustomGoalBlocksAdventureGoldPush(plan, out _))
            {
                CancelManualTitan("quest, preparation, or rebirth owns combat");
                return _manualTitanActive;
            }

            var index = Array.IndexOf(ZoneHelpers.TitanZones, Settings.ManualTitanZone);
            if (index < 0 || Settings.ManualTitanLoadout < -1 || Settings.ManualTitanLoadout > 2 || Settings.ManualTitanMaxDeaths < 1)
            {
                CancelManualTitan("invalid configuration");
                return _manualTitanActive;
            }

            if (!_manualTitanSpawns.TryGetValue(index, out var current))
            {
                current = new ManualTitanSpawn { Elapsed = ZoneHelpers.TitanSpawnElapsedSeconds(index) };
                _manualTitanSpawns.Add(index, current);
            }
            if (current.Disarmed) { _manualTitanStatus = current.Outcome; return false; }
            if (!_manualTitanActive && current.Started
                && (ManualTitanNativeInt($"titan{index + 1}Kills") > current.Kills
                    || index == 4 && ManualTitanNativeInt("waldoDefeats") > current.Phase))
            {
                // Completion during an interruption is not permission to fight an unobserved
                // new spawn. Wait for an actual timer reset and its following due transition.
                current.Disarmed = true;
                current.Outcome = _manualTitanStatus = "Native target completed while interrupted; waiting for next observed spawn";
                return false;
            }

            var countdown = ZoneHelpers.TitanSpawnSecondsRemaining(index);
            if (double.IsNaN(countdown) || countdown > 0 || !ManualTitanUnlocked(index))
            {
                CancelManualTitan("waiting for native spawn or manual unlock");
                return _manualTitanActive;
            }
            if (!current.Started && Settings.ManualTitanRequireAT && !ManualTitanATReady())
            {
                _manualTitanStatus = "Waiting for positive AT power/toughness targets and completed levels";
                return false;
            }

            if (!_manualTitanActive && current.Gear == null)
            {
                try
                {
                    var selected = Settings.ManualTitanLoadout < 0 ? LoadoutManager.CaptureExactLoadout()
                        : SelectSetupGear(GearOptimizationGoal.Fight, (FightScoreMode)Settings.ManualTitanLoadout, true);
                    var support = SelectFocusSupport(SetupForManualAdventure());
                    current.Gear = selected;
                    current.Material = selected.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                    current.Support = support;
                    current.Loadout = Settings.ManualTitanLoadout;
                    current.CapacityTransition = true;
                    current.Acquisition = Guid.NewGuid().ToString("N");
                }
                catch (Exception error) { _manualTitanStatus = "Selection waiting: " + error.Message; return true; }
            }
            if (!_manualTitanActive && current.CapacityTransition)
            {
                var permitted = (Settings.ManageEnergy ? GearRecallFootprint.Energy : 0) | (Settings.ManageMagic ? GearRecallFootprint.Magic : 0);
                permitted &= (GearRecallFootprint)~_resourceSetupExecutor.Application.BlockedLanes;
                var capacity = GearOptimizer.PreflightCapacityTransition(current.Gear, permitted);
                if (capacity.Failure != null) { _manualTitanStatus = capacity.Failure; return true; }
                current.ResourceRecall = capacity.Recall;
            }

            try
            {
                if (!_manualTitanActive)
                {
                    var log = Character.adventureController?.log;
                    var entries = log?.GetType().GetField("Eventlog", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(log) as List<string>;
                    if (entries == null || current.Started && _manualTitanLog != null && !ReferenceEquals(entries, _manualTitanLog))
                    {
                        current.Disarmed = true;
                        current.Outcome = _manualTitanStatus = "Death log unavailable; current spawn disarmed";
                        return false;
                    }
                    if (!RetireRoutesForSuccessor())
                    { _manualTitanStatus = "Waiting for current route retirement"; return true; }
                    if (!current.Started) _manualTitanLastEvent = null;
                    _manualTitanLog = entries;
                    _manualTitanCombatOwned = false;
                    _manualTitanIndex = index;
                    if (!ObserveManualTitanDeaths()) return EndManualTitanSpawn("death log continuity unavailable");
                    if (current.Gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != current.Material[i]).Any())
                        return EndManualTitanSpawn("acquired equipment material changed; review required");
                    _manualTitanProcess = BeginRouteProcess($"manual-titan:{index}", current.Gear, current.Loadout >= 0,
                        selectedSupport: current.Support, capacityTransition: current.CapacityTransition,
                        resourceRecall: current.ResourceRecall, acquisition: current.Acquisition);
                    if (!current.Started)
                    {
                        current.Started = true;
                        current.Limit = Settings.ManualTitanMaxDeaths;
                        current.Kills = ManualTitanNativeInt($"titan{index + 1}Kills");
                        current.Phase = ManualTitanNativeInt("waldoDefeats");
                    }
                    current.Limit = Math.Min(current.Limit, Settings.ManualTitanMaxDeaths);
                    if (current.Deaths >= current.Limit) return EndManualTitanSpawn("death budget exhausted");
                    _manualTitanActive = true;
                    _manualTitanConfig = config;
                    _manualTitanLastTick = DateTime.UtcNow;
                }
                if (!_manualTitanProcess.Applied && !ApplyRouteProcess(_manualTitanProcess))
                    throw new InvalidOperationException("Selected capacity setup remains unverified.");

                if (!ManualTitanClaimCurrent())
                    return EndManualTitanSpawn("equipment ownership lost");
                if (ManualTitanGearChanged()) return EndManualTitanSpawn(_manualTitanStatus);
                var now = DateTime.UtcNow;
                current.OwnedSeconds += Math.Max(0, (now - _manualTitanLastTick).TotalSeconds);
                _manualTitanLastTick = now;
                // Bound a stuck spawn, healing/precast loop, or a fight without a provable result.
                // This budget survives interruptions, so toggling cannot extend it indefinitely.
                if (current.OwnedSeconds >= 600) return EndManualTitanSpawn("ten-minute attempt timeout; no confirmed result");
                var zone = Character.adventure.zone;
                if (_manualTitanCombatOwned && zone != -1 && zone != Settings.ManualTitanZone)
                    return EndManualTitanSpawn("combat moved to another zone");

                if (zone == Settings.ManualTitanZone && Character.adventureController.currentEnemy != null)
                    _manualTitanEnemyName = Character.adventureController.currentEnemy.name;
                _manualTitanStatus = $"Fighting {ZoneName(Settings.ManualTitanZone)}; {Math.Max(0, current.Limit - current.Deaths)} deaths remaining";
                SetCustomAutomationStatus("Manual titan: " + _manualTitanStatus);
                // Use the shared manual options, with recovery mandatory between actual deaths.
                // Receipt covers entry from Safe Zone too: native movement may enter, die,
                // and return to Safe Zone before ManualZone returns.
                _manualTitanCombatOwned = true;
                var combatGrant = RouteProcessGrant(_manualTitanProcess);
                if (!ValidateExecutionGrant(combatGrant, _manualTitanProcess.Claim.Domains))
                    return EndManualTitanSpawn("combat prerequisite authority is no longer current");
                _combManager.ManualZone(Settings.ManualTitanZone, false,
                    Settings.ManualAdventureRecoverHealth || current.Deaths > 0, Settings.ManualAdventurePrecastBuffs,
                    Settings.ManualAdventureFastCombat, Settings.ManualAdventureBeastMode,
                    grant: combatGrant, observe: () => ObserveRouteProcess(_manualTitanProcess));
                if (!ValidateExecutionGrant(combatGrant, _manualTitanProcess.Claim.Domains))
                    return EndManualTitanSpawn("combat prerequisite authority changed during native callback");
                if (!ManualTitanClaimCurrent() || !_manualTitanEpoch.Matches())
                    return EndManualTitanSpawn("equipment ownership lost during combat");
                if (ManualTitanGearChanged()) return EndManualTitanSpawn(_manualTitanStatus);
                // A native move can log death and select Safe Zone synchronously. Consume its
                // event while the old combat ownership is still intact, before retiring it.
                if (!ObserveManualTitanDeaths()) return EndManualTitanSpawn("death log continuity unavailable");
                if (current.Deaths >= current.Limit) return EndManualTitanSpawn("death budget exhausted");
                _manualTitanCombatOwned = Character.adventure.zone == Settings.ManualTitanZone;
                return true;
            }
            catch (Exception error)
            {
                if (_manualTitanProcess?.CapacityTransition == true && CapacityRouteOwnership(_manualTitanProcess)
                    && CapacityRoutePermissions(_manualTitanProcess) && ExecutionClaimSetupOwned(_manualTitanProcess.Claim)
                    && (!_manualTitanProcess.ResourceApplied || CapacityRouteEmpty(_manualTitanProcess))
                    && (error is SelectedSetupException selected && selected.Outcome.KnownProgressStillOwned()
                        || error is ResourceMutationException resource && resource.Result.Verified
                        || error is InvalidOperationException && !_manualTitanProcess.ResourceApplied))
                {
                    current.OwnedSeconds += Math.Max(0, (DateTime.UtcNow - _manualTitanLastTick).TotalSeconds);
                    _manualTitanLastTick = DateTime.UtcNow;
                    if (current.OwnedSeconds < 600)
                    { _manualTitanStatus = "Retained setup waiting: " + error.Message; return true; }
                }
                current.Disarmed = true;
                current.Outcome = "setup/combat failed: " + error.Message;
                CancelManualTitan(current.Outcome);
                LogError("Manual titan", error);
                return _manualTitanActive;
            }
        }

        private void ObserveManualTitanOpportunities()
        {
            if (_manualTitanEpoch != null && !_manualTitanEpoch.Matches()) ResetManualTitanRuntime();
            if (_manualTitanEpoch == null) _manualTitanEpoch = new LoadoutManager.SnapshotEpoch();
            if (_manualTitanSpawns == null) _manualTitanSpawns = new Dictionary<int, ManualTitanSpawn>();
            for (var index = 0; index < ZoneHelpers.TitanZones.Length; index++)
            {
                if (!_manualTitanSpawns.TryGetValue(index, out var spawn))
                    _manualTitanSpawns.Add(index, spawn = new ManualTitanSpawn());
                var elapsed = ZoneHelpers.TitanSpawnElapsedSeconds(index);
                var remaining = ZoneHelpers.TitanSpawnSecondsRemaining(index);
                if (double.IsNaN(elapsed) || double.IsNaN(remaining)) continue;
                // A first-observed due spawn may belong to an interrupted old session.
                // Only an observed native reset and later due transition supplies a budget.
                if (double.IsNaN(spawn.Elapsed) && remaining <= 0)
                {
                    spawn.Disarmed = true;
                    spawn.Outcome = "Waiting for next observed spawn after session start";
                }
                if (!double.IsNaN(spawn.Elapsed) && elapsed < spawn.Elapsed && remaining > 0)
                    spawn.ResetSeen = true;
                spawn.Elapsed = elapsed;
                if (!spawn.ResetSeen || remaining > 0 || _manualTitanActive && index == _manualTitanIndex) continue;
                spawn.Started = spawn.Disarmed = spawn.ResetSeen = false;
                spawn.Deaths = 0;
                spawn.OwnedSeconds = 0;
                spawn.Outcome = null;
                spawn.Gear = null; spawn.Material = null; spawn.Support = null;
                spawn.Acquisition = null; spawn.ResourceRecall = GearRecallFootprint.None; spawn.CapacityTransition = false;
            }
        }

        private bool ManualTitanATReady()
        {
            var at = Character.advancedTraining;
            if (at == null || at.levelTarget == null || at.level == null || at.levelTarget.Length < 2 || at.level.Length < 2)
                return false;
            if (at.levelTarget[0] > 0 && at.levelTarget[1] > 0)
                return at.level[0] >= at.levelTarget[0] && at.level[1] >= at.levelTarget[1];
            // A completed stage returns its borrowed menu targets; partial or explicit targets are not a release.
            return at.levelTarget[0] == 0 && at.levelTarget[1] == 0 && ManualTitanCompletedPlanATReady();
        }

        private int ManualTitanNativeInt(string name)
        {
            var value = Character.adventure.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(Character.adventure);
            return value is int number ? number : -1;
        }

        private bool ManualTitanUnlocked(int index)
        {
            if (!Character.buttons.adventure.IsInteractable() || !CombatManager.IsZoneUnlocked(ZoneHelpers.TitanZones[index])) return false;
            // manageFight/spawnEnemy allow early manual fights without autokill gear, settings,
            // Walderp's three final kills, or later native autokill stat/kill thresholds.
            if (index == 4)
            {
                var defeats = ManualTitanNativeInt("waldoDefeats");
                var finds = ManualTitanNativeInt("waldoFinds");
                return defeats >= 0 && defeats <= 4 && finds >= 0 && (defeats <= finds || finds >= 4);
            }
            if (index < 5) return true;
            var unlocked = Character.adventure.GetType().GetField($"titan{index + 1}Unlocked",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(Character.adventure);
            return unlocked is bool available && available && ZoneHelpers.TitanVersion(index) > 0;
        }

        private bool ObserveManualTitanDeaths()
        {
            if (_manualTitanLog == null) return false;
            var log = Character.adventureController?.log;
            var entries = log?.GetType().GetField("Eventlog", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(log);
            if (!ReferenceEquals(entries, _manualTitanLog)) return false;
            string newest = null;
            var deaths = 0;
            var baselineFound = _manualTitanLastEvent == null;
            const string suffix = " has defeated you!";
            for (var i = _manualTitanLog.Count - 1; i >= 0; i--)
            {
                var entry = _manualTitanLog[i];
                if (entry == null) continue;
                // PlayerLog retains only 30 events. Advance on every event, not just deaths.
                if (ReferenceEquals(entry, _manualTitanLastEvent)) { baselineFound = true; break; }
                if (newest == null) newest = entry;
                if (!entry.EndsWith(suffix, StringComparison.Ordinal)) continue;
                var ownedZone = ZoneHelpers.TitanZones[_manualTitanIndex];
                if (!_manualTitanCombatOwned || Character.adventure.zone != ownedZone && Character.adventure.zone != -1) continue;
                var name = entry.Substring(0, entry.Length - suffix.Length);
                var matches = name == _manualTitanEnemyName;
                var enemies = Character.adventureController.enemyList;
                var zone = ZoneHelpers.TitanZones[_manualTitanIndex];
                if (!matches && zone < enemies.Count)
                    foreach (var enemy in enemies[zone]) if (enemy.name == name) { matches = true; break; }
                if (matches) deaths++;
            }
            // A removed baseline makes the intervening death count unknowable. Never
            // recount remaining old strings or silently refresh a cleared/trimmed log.
            if (!baselineFound) return false;
            if (newest != null) _manualTitanLastEvent = newest;
            if (_manualTitanActive && _manualTitanCombatOwned && deaths > 0)
            {
                _manualTitanSpawns[_manualTitanIndex].Deaths += deaths;
                if (Character.adventure.zone == -1 && _manualTitanProcess != null
                    && _manualTitanProcess.Zone == ZoneHelpers.TitanZones[_manualTitanIndex]
                    && ExecutionClaimSetupOwned(_manualTitanProcess.Claim)) _manualTitanProcess.Zone = -1;
                _manualTitanCombatOwned = false;
                // Native death already moves to Safe Zone. If not yet reflected, force recovery
                // only after consuming the death event, not before checking the old route.
                if (ManualTitanClaimCurrent() && _manualTitanEpoch.Matches() && !ManualTitanGearChanged()
                    && Character.adventure.zone == ZoneHelpers.TitanZones[_manualTitanIndex])
                    _combManager.MoveToZone(-1, RouteProcessGrant(_manualTitanProcess), () => ObserveRouteProcess(_manualTitanProcess));
            }
            return true;
        }

        private bool ManualTitanClaimCurrent()
        {
            return _manualTitanProcess != null && ExecutionClaimCurrent(_manualTitanProcess.Claim);
        }

        private bool ManualTitanGearChanged()
        {
            // Claim identity alone cannot make externally changed equipment owned.
            if (!ManualTitanClaimCurrent() || !_manualTitanEpoch.Matches()) return false;
            if (ExecutionClaimSetupOwned(_manualTitanProcess.Claim)) return false;
            var spawn = _manualTitanSpawns[_manualTitanIndex];
            spawn.Disarmed = true;
            spawn.Outcome = _manualTitanStatus = "equipped gear changed externally; manual titan relinquished";
            return true;
        }

        private bool EndManualTitanSpawn(string reason)
        {
            var spawn = _manualTitanSpawns[_manualTitanIndex];
            spawn.Disarmed = true;
            spawn.Outcome = reason;
            CancelManualTitan(reason);
            return _manualTitanActive;
        }

        private void CancelManualTitan(string reason)
        {
            if (!ReferenceEquals(reference, this) || _unloaded) return;
            var active = _manualTitanActive;
            var currentEpoch = _manualTitanProcess?.Claim.Epoch.Matches() ?? _manualTitanEpoch?.Matches() ?? true;
            var capacityRelease = currentEpoch && _manualTitanProcess?.CapacityTransition == true && _manualTitanProcess.ResourceRecall != GearRecallFootprint.None;
            if (active && !_manualTitanRecovering && _manualTitanIndex >= 0)
            {
                _manualTitanSpawns[_manualTitanIndex].OwnedSeconds += Math.Max(0, (DateTime.UtcNow - _manualTitanLastTick).TotalSeconds);
                if (_manualTitanEpoch?.Matches() == true && !ObserveManualTitanDeaths())
                {
                    _manualTitanSpawns[_manualTitanIndex].Disarmed = true;
                    _manualTitanSpawns[_manualTitanIndex].Outcome = reason = "death log continuity unavailable";
                }
            }
            _manualTitanCombatOwned = false;
            if (!EndRouteProcess(_manualTitanProcess))
            {
                _manualTitanRecovering = true;
                _manualTitanCleanupReason = reason;
                _manualTitanRecoveryError = "Waiting for owned native route retirement";
                return;
            }
            _manualTitanActive = _manualTitanCombatOwned = _manualTitanRecovering = false;
            _manualTitanProcess = null;
            _manualTitanEnemyName = _manualTitanConfig = null;
            _manualTitanCleanupReason = _manualTitanRecoveryError = null;
            if (_manualTitanIndex >= 0 && _customEquipmentOwner == $"manual-titan:{_manualTitanIndex}") ResetCustomEquipmentSetup();
            if (active) InvalidateInlineStageApplication();
            _manualTitanIndex = -1;
            _manualTitanStatus = reason;
            if (active && capacityRelease && _executionLifecycle == ExecutionLifecycle.Running && !_routeHandoffPending)
            {
                // The common gear/hold handoff finished first. Ordinary funding
                // must now observe that the manual actor no longer owns equipment.
                _resourceReconciliationPending = true;
                ReconcileCustomResources();
            }
        }

        private bool ManualTitanBlocksZone(int zone)
        {
            // Lower routes must not bypass this coordinator's gates or death budget.
            // Recovery blocks all combat, even if the feature was disabled meanwhile.
            return zone != -1 && (_manualTitanActive && _manualTitanRecovering
                || Settings.ManualTitanEnabled && zone == Settings.ManualTitanZone
                    && Array.IndexOf(ZoneHelpers.TitanZones, zone) >= 0);
        }

        private void ResetManualTitanRuntime()
        {
            CancelManualTitan("save/rebirth epoch reset");
            if (_manualTitanActive) return; // Do not erase an unfulfilled same-epoch cleanup lease.
            _manualTitanSpawns?.Clear();
            _manualTitanEpoch = null;
        }

        private JSONObject GetManualTitanRuntime()
        {
            var state = new JSONObject();
            var selectedZone = Settings?.ManualTitanZone ?? 16;
            var index = _manualTitanActive ? _manualTitanIndex : Array.IndexOf(ZoneHelpers.TitanZones, selectedZone);
            ManualTitanSpawn spawn = null;
            _manualTitanSpawns?.TryGetValue(index, out spawn);
            state["active"] = _manualTitanActive;
            state["recovering"] = _manualTitanRecovering;
            state["recoveryError"] = _manualTitanRecoveryError ?? string.Empty;
            state["zone"] = _manualTitanActive ? ZoneHelpers.TitanZones[_manualTitanIndex] : selectedZone;
            state["status"] = _manualTitanStatus ?? "Disabled";
            state["deaths"] = spawn?.Deaths ?? 0;
            state["remainingDeaths"] = spawn != null && spawn.Started ? Math.Max(0, spawn.Limit - spawn.Deaths) : 0;
            state["disarmed"] = spawn?.Disarmed ?? false;
            state["awaitingNextSpawn"] = spawn?.Disarmed ?? false;
            return state;
        }
    }
}
