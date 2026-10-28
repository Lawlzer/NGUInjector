using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private bool _customTitanGoldActive, _customTitanFightActive, _customTitanGoldUsesGoldGear;
        private RouteProcess _customTitanGoldProcess, _customTitanFightProcess;
        private int _customTitanGoldIndex = -1, _customTitanFightIndex = -1;
        private string _customTitanGoldReadiness, _customTitanFightReadiness;
        private DateTime _customTitanGoldStartedAt, _customTitanFightStartedAt;
        private Dictionary<int, double> _customTitanGoldSpawns = new Dictionary<int, double>();
        private DateTime[] _customTitanAutokillCooldownUntil = new DateTime[0];
        private string[] _customTitanAutokillCooldownReason = new string[0];
        private const double CustomTitanAutokillFailureCooldownSeconds = 3600;
        private int _customTitanGoldPendingIndex = -1;

        private bool TryRouteCustomTitanGoldKill()
        {
            if (_customTitanGoldSpawns == null) _customTitanGoldSpawns = new Dictionary<int, double>();
            if (_customTitanGoldActive)
            {
                if (_customTitanGoldPendingIndex >= 0)
                {
                    if ((DateTime.UtcNow - _customTitanGoldStartedAt).TotalSeconds > 25)
                    {
                        MarkTitanAutokillFailed(_customTitanGoldPendingIndex, "retained upgrade reached original stall deadline");
                        CancelCustomTitanGoldKill("retained upgrade timed out");
                        return _customTitanGoldActive;
                    }
                    if (!ApplyRouteProcess(_customTitanGoldProcess)) return true;
                    if (!_customTitanGoldSpawns.Keys.Concat(new[] { _customTitanGoldPendingIndex }).All(ZoneHelpers.TitanAutokillAchieved))
                    { CancelCustomTitanGoldKill("native upgraded obligations did not verify"); return _customTitanGoldActive; }
                    _customTitanGoldSpawns[_customTitanGoldPendingIndex] = ZoneHelpers.TitanSpawnElapsedSeconds(_customTitanGoldPendingIndex);
                    _customTitanGoldPendingIndex = -1;
                }
                if (!RouteProcessReady(_customTitanGoldProcess)) { CancelCustomTitanGoldKill("setup authority lost"); return _customTitanGoldActive; }
                foreach (var spawn in _customTitanGoldSpawns.ToArray())
                {
                    if (!Settings.ManageGoldLoadouts || Settings.TitanGoldTargets == null
                        || spawn.Key >= Settings.TitanGoldTargets.Length || !Settings.TitanGoldTargets[spawn.Key]
                        || !ZoneHelpers.TitanAutokillAvailable(spawn.Key))
                    { _customTitanGoldSpawns.Remove(spawn.Key); continue; }
                    if (!ZoneHelpers.TitanAutokillAchieved(spawn.Key))
                    { MarkTitanAutokillFailed(spawn.Key, "autokill check stopped passing"); _customTitanGoldSpawns.Remove(spawn.Key); continue; }
                    if (ZoneHelpers.TitanSpawnElapsedSeconds(spawn.Key) < spawn.Value)
                    {
                        if (_customTitanGoldUsesGoldGear) ZoneHelpers.MarkTitanMoneyDone(spawn.Key);
                        _customTitanGoldSpawns.Remove(spawn.Key);
                        _customTitanGoldStartedAt = DateTime.UtcNow;
                    }
                    else if (!ZoneHelpers.TitanGoldTargetActive(spawn.Key)) _customTitanGoldSpawns.Remove(spawn.Key);
                    else if (!TitanAutokillRouteStillSafe(spawn.Key, _customTitanGoldStartedAt, out var reason))
                    { MarkTitanAutokillFailed(spawn.Key, reason); _customTitanGoldSpawns.Remove(spawn.Key); }
                }
                TrackPreparingTitanGoldSpawns();
                TryUpgradeTitanGoldPreparation();
                if (_customTitanGoldSpawns.Count == 0) { CancelCustomTitanGoldKill("prepared spawns finished"); return _customTitanGoldActive; }
                SetCustomAutomationStatus($"Titan gold auto-kill: waiting in Safe Zone for {_customTitanGoldSpawns.Count} prepared titan(s).");
                return true;
            }
            var index = FindCustomTitanGoldPreparation();
            if (index < 0) return false;
            if (!TrySelectTitanAutokillGear(index, out var gear, out var gold, out var readiness))
            { MarkTitanAutokillFailed(index, readiness); return false; }
            if (!RetireRoutesForSuccessor()) return true;
            _customTitanGoldProcess = BeginRouteProcess("titan-gold", gear, selectedSupport: SelectTitanGoldSupport());
            _customTitanGoldActive = true; _customTitanGoldIndex = index;
            _customTitanGoldStartedAt = DateTime.UtcNow;
            _customTitanGoldReadiness = readiness; _customTitanGoldUsesGoldGear = gold;
            try
            {
                if (!ApplyRouteProcess(_customTitanGoldProcess) || !ZoneHelpers.TitanAutokillAchieved(index))
                    throw new InvalidOperationException("Selected gear did not pass native autokill verification.");
                if (Character.adventure.zone != -1 && !_combManager.MoveToZone(-1, RouteProcessGrant(_customTitanGoldProcess),
                    () => ObserveRouteProcess(_customTitanGoldProcess))) return true;
                _customTitanGoldSpawns[index] = ZoneHelpers.TitanSpawnElapsedSeconds(index);
                TrackPreparingTitanGoldSpawns();
                return true;
            }
            catch (Exception error)
            {
                MarkTitanAutokillFailed(index, "selected setup failed: " + error.Message);
                CancelCustomTitanGoldKill("selected setup failed");
                throw;
            }
        }

        private SupportSelection SelectTitanGoldSupport()
        {
            var current = CaptureSupportState().Selection;
            var context = CaptureSupportContext();
            return new SupportSelection(context.BeardsAvailable && !context.BeardsDisabled
                ? BeardManager.SelectBeards(new[] { BeardGold }, false) : current.Beards, current.Diggers);
        }

        private int FindCustomTitanGoldPreparation()
        {
            for (var index = ZoneHelpers.GetPreparingTitanGoldTarget(); index >= 0 && index < ZoneHelpers.TitanZones.Length; index++)
                if (!ManualTitanBlocksZone(ZoneHelpers.TitanZones[index]) && ZoneHelpers.TitanGoldTargetPreparing(index)
                    && !TitanAutokillOnCooldown(index, out _)) return index;
            return -1;
        }

        private void TrackPreparingTitanGoldSpawns()
        {
            for (var index = 0; index < ZoneHelpers.TitanZones.Length; index++)
                if (!ManualTitanBlocksZone(ZoneHelpers.TitanZones[index]) && !_customTitanGoldSpawns.ContainsKey(index)
                    && ZoneHelpers.TitanGoldTargetPreparing(index) && !TitanAutokillOnCooldown(index, out _)
                    && ZoneHelpers.TitanAutokillAchieved(index))
                    _customTitanGoldSpawns[index] = ZoneHelpers.TitanSpawnElapsedSeconds(index);
        }

        private void TryUpgradeTitanGoldPreparation()
        {
            if (_customTitanGoldPendingIndex >= 0) return;
            var index = Enumerable.Range(0, ZoneHelpers.TitanZones.Length).Where(x => !_customTitanGoldSpawns.ContainsKey(x)
                && !ManualTitanBlocksZone(ZoneHelpers.TitanZones[x]) && ZoneHelpers.TitanGoldTargetPreparing(x)
                && !TitanAutokillOnCooldown(x, out _) && !ZoneHelpers.TitanAutokillAchieved(x)).DefaultIfEmpty(-1).First();
            if (index < 0) return;
            var obligations = _customTitanGoldSpawns.Keys.Concat(new[] { index }).ToArray();
            if (!TrySelectTitanRequirementGear(obligations, out var selected))
            { MarkTitanAutokillFailed(index, "No pure selection preserves all admitted titan obligations"); return; }
            var material = selected.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var setup = _customTitanGoldProcess.Setup.Copy();
            setup.Gear = selected;
            setup.MaterialValid = () => !selected.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != material[i]).Any();
            _customTitanGoldPendingIndex = index;
            _customTitanGoldUsesGoldGear = false;
            _customTitanGoldProcess.Setup = setup;
            _customTitanGoldProcess.SelectedMaterial = material;
            _customTitanGoldProcess.Applied = false;
            // The same acquired definition is retried after bounded partial/render failures.
            ApplyRouteProcess(_customTitanGoldProcess);
        }

        private bool TryRouteCustomTitanFightKill()
        {
            if (_customTitanFightActive)
            {
                if (!RouteProcessReady(_customTitanFightProcess) || !ZoneHelpers.TitanSpawnWindowActive(_customTitanFightIndex))
                { CancelCustomTitanFightKill("spawn finished or setup authority lost"); return _customTitanFightActive; }
                if (!TitanAutokillRouteStillSafe(_customTitanFightIndex, _customTitanFightStartedAt, out var reason))
                { MarkTitanAutokillFailed(_customTitanFightIndex, reason); CancelCustomTitanFightKill(reason); return _customTitanFightActive; }
                SetCustomAutomationStatus("Titan autokill route: " + _customTitanFightReadiness);
                if (!_combManager.SetIdleAttack(false, RouteProcessGrant(_customTitanFightProcess),
                    () => ObserveRouteProcess(_customTitanFightProcess))) return true;
                _combManager.MoveToZone(ZoneHelpers.TitanZones[_customTitanFightIndex], RouteProcessGrant(_customTitanFightProcess),
                    () => ObserveRouteProcess(_customTitanFightProcess));
                return true;
            }
            var index = ZoneHelpers.GetSpawningTitanFightTarget();
            for (; index >= 0 && index < ZoneHelpers.TitanZones.Length; index++)
                if (!ManualTitanBlocksZone(ZoneHelpers.TitanZones[index]) && !ZoneHelpers.TitanGoldTargetActive(index)
                    && ZoneHelpers.TitanSpawnWindowActive(index) && ZoneHelpers.TitanAutokillAvailable(index)
                    && !ZoneHelpers.TitanAutokillAchieved(index) && !TitanAutokillOnCooldown(index, out _)) break;
            if (index < 0 || index >= ZoneHelpers.TitanZones.Length) return false;
            if (!TrySelectTitanAutokillGear(index, out var gear, out _, out var readiness))
            { MarkTitanAutokillFailed(index, readiness); return false; }
            if (!RetireRoutesForSuccessor()) return true;
            _customTitanFightProcess = BeginRouteProcess("titan-autokill", gear, selectedSupport: SelectTitanGoldSupport());
            _customTitanFightActive = true; _customTitanFightIndex = index;
            _customTitanFightReadiness = readiness; _customTitanFightStartedAt = DateTime.UtcNow;
            try
            {
                if (!ApplyRouteProcess(_customTitanFightProcess) || !ZoneHelpers.TitanAutokillAchieved(index))
                    throw new InvalidOperationException("Selected gear did not pass native autokill verification.");
                return TryRouteCustomTitanFightKill();
            }
            catch (Exception error)
            {
                MarkTitanAutokillFailed(index, "selected setup failed: " + error.Message);
                CancelCustomTitanFightKill("selected setup failed");
                throw;
            }
        }

        // Selection is pure. No speculative gold equip, rollback, or retry search after mutation.
        private bool TrySelectTitanAutokillGear(int index, out ih[] gear, out bool gold, out string readiness)
        {
            gear = null; gold = false;
            var requirements = ZoneHelpers.TitanAutokillRequirements(index);
            readiness = "Native autokill requirements unavailable";
            if (requirements == null) return false;
            var margin = (float[])requirements.Clone();
            for (var stat = 0; stat < margin.Length; stat++)
                if (margin[stat] > 0 && margin[stat] < 1e36f)
                    margin[stat] = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(margin[stat]), 0) + 8), 0);
            Func<double, double, double> fitness;
            try { fitness = CaptureTitanAutokillFitness(margin); }
            catch (InvalidOperationException error) { readiness = error.Message; return false; }
            foreach (var goal in new[] { GearOptimizationGoal.Gold, GearOptimizationGoal.Fight })
            {
                if (goal == GearOptimizationGoal.Fight)
                {
                    if (!TrySelectTitanRequirementGear(new[] { index }, out gear)) continue;
                    readiness = "required-stat selection with eight-ULP headroom; native verification required";
                    return true;
                }
                var result = GearOptimizer.GetCachedOrOptimize(goal, GetActiveFightScoreMode());
                if (!result.Success) { readiness = result.Message; continue; }
                var selected = SelectedGearMap(result);
                double power = 0, toughness = 0;
                foreach (var item in selected.Where(x => x.id != 0))
                {
                    var factor = item.slot == -6 ? Controller.weapon2Factor() : 1f;
                    power += Controller.equipAttackBonus(item.equipment) * factor;
                    toughness += Controller.equipDefenseBonus(item.equipment) * factor;
                }
                var nativeShortcut = ZoneHelpers.TitanAutokillAchieved(index)
                    && new[] { Character.totalAdvAttack(), Character.totalAdvDefense(), Character.totalAdvHPRegen() }
                        .Where((value, stat) => value < requirements[stat]).Any();
                if (fitness(power, toughness) < 1 && !nativeShortcut) continue;
                if (GearOptimizer.CheckExactSwapCapacity(selected, Character.curEnergy, Character.res3.curRes3) != null) continue;
                gear = selected; gold = goal == GearOptimizationGoal.Gold;
                readiness = (gold ? "gold" : "required-stat") + " selection; native verification required";
                return true;
            }
            readiness = "No allocation-preserving projected autokill loadout found";
            return false;
        }

        private bool TrySelectTitanRequirementGear(int[] obligations, out ih[] selected)
        {
            selected = null;
            var combined = new float[3];
            var actual = new[] { Character.totalAdvAttack(), Character.totalAdvDefense(), Character.totalAdvHPRegen() };
            foreach (var index in obligations)
            {
                var requirement = ZoneHelpers.TitanAutokillRequirements(index);
                if (requirement == null) return false;
                if (ZoneHelpers.TitanAutokillAchieved(index) && actual.Where((value, stat) => value < requirement[stat]).Any()) continue;
                for (var stat = 0; stat < combined.Length; stat++) combined[stat] = Math.Max(combined[stat], requirement[stat]);
            }
            for (var stat = 0; stat < combined.Length; stat++)
                if (combined[stat] > 0 && combined[stat] < 1e36f)
                    combined[stat] = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(combined[stat]), 0) + 8), 0);
            if (combined[0] == 0) combined[0] = 1;
            if (combined[1] == 0) combined[1] = 1;
            Func<double, double, double> fitness;
            try { fitness = CaptureTitanAutokillFitness(combined); }
            catch (InvalidOperationException) { return false; }
            var result = GearOptimizer.SelectForAutokill(fitness);
            if (!result.Success) return false;
            var gear = SelectedGearMap(result);
            if (GearOptimizer.CheckExactSwapCapacity(gear, Character.curEnergy, Character.res3.curRes3) != null) return false;
            selected = gear;
            return true;
        }

        private Func<double, double, double> CaptureTitanAutokillFitness(float[] requirements)
        {
            if (Character.inventory.disabled || Character.challenges.noEquipmentChallenge.inChallenge)
                throw new InvalidOperationException("Equipment bonuses are disabled.");
            var p = CaptureLoadoutPresetAdventure(true); var t = CaptureLoadoutPresetAdventure(false);
            if (p.Concat(t).Any(x => !LoadoutPresetPlanner.Finite(x) || x < 0)) throw new InvalidOperationException("Invalid adventure multipliers.");
            var attack = Character.adventure.attack; var defense = Character.adventure.defense; var regen = Character.adventure.regen;
            var cubeP = Character.inventory.cubePower; var cubeT = Character.inventory.cubeToughness;
            Func<double, double> power = gear => LoadoutPresetPlanner.AdventureStat(attack, cubeP, gear, p, p[4]);
            Func<double, double> tough = gear => LoadoutPresetPlanner.AdventureStat(defense, cubeT, gear, t, t[4]);
            Func<double, double> heal = gear => LoadoutPresetPlanner.AdventureRegen(defense, regen, cubeT, gear, t);
            if (!LoadoutPresetPlanner.AdventureParity(power(Controller.attackBonus()), Character.totalAdvAttack())
                || !LoadoutPresetPlanner.AdventureParity(tough(Controller.defenseBonus()), Character.totalAdvDefense())
                || !LoadoutPresetPlanner.AdventureRegenParity(heal(Controller.defenseBonus()), Character.totalAdvHPRegen()))
                throw new InvalidOperationException("Forward adventure stats do not match native getters.");
            return (gp, gt) => Math.Min(1, Math.Min(power(gp) / requirements[0],
                Math.Min(tough(gt) / requirements[1], requirements[2] > 0 ? heal(gt) / requirements[2] : 1)));
        }

        private bool TitanAutokillRouteStillSafe(int index, DateTime started, out string reason)
        {
            reason = !ZoneHelpers.TitanAutokillAchieved(index) ? "autokill check stopped passing"
                : (DateTime.UtcNow - started).TotalSeconds > 25 ? "autokill route timed out"
                : Character.adventure.zone == ZoneHelpers.TitanZones[index] && Character.adventureController.currentEnemy != null
                    ? "titan enemy appeared instead of autokilling" : null;
            return reason == null;
        }

        private void EnsureTitanAutokillCooldownArrays()
        {
            Array.Resize(ref _customTitanAutokillCooldownUntil, ZoneHelpers.TitanZones.Length);
            Array.Resize(ref _customTitanAutokillCooldownReason, ZoneHelpers.TitanZones.Length);
        }

        private bool TitanAutokillOnCooldown(int index, out string warning)
        {
            EnsureTitanAutokillCooldownArrays(); warning = null;
            if (index < 0 || index >= _customTitanAutokillCooldownUntil.Length || _customTitanAutokillCooldownUntil[index] <= DateTime.UtcNow) return false;
            warning = $"Titan autokill cooldown: {_customTitanAutokillCooldownReason[index]}";
            return true;
        }

        private void MarkTitanAutokillFailed(int index, string reason)
        {
            EnsureTitanAutokillCooldownArrays();
            if (index < 0 || index >= _customTitanAutokillCooldownUntil.Length) return;
            _customTitanAutokillCooldownUntil[index] = DateTime.UtcNow.AddSeconds(CustomTitanAutokillFailureCooldownSeconds);
            _customTitanAutokillCooldownReason[index] = reason;
            var warning = $"WARNING: Titan autokill failed for {ZoneName(ZoneHelpers.TitanZones[index])}; retry in one hour. {reason}";
            Log(warning); SetCustomAutomationStatus(warning); settingsForm?.ReportCustomError(warning);
        }

        private static void StopAdventureAutoattack()
        {
            var main = reference;
            var process = main._customTitanFightProcess ?? main._customTitanGoldProcess;
            main._combManager.SetIdleAttack(false, process == null ? main.ObserveRouteGrant(-1) : main.RouteProcessGrant(process),
                process == null ? (Action)null : () => main.ObserveRouteProcess(process));
        }

        internal bool ServiceCustomTitanRestoration()
        {
            if (_customTitanGoldActive && _customTitanGoldPendingIndex < 0 && !RouteProcessReady(_customTitanGoldProcess)) CancelCustomTitanGoldKill("setup waiting or lost");
            if (_customTitanFightActive && !RouteProcessReady(_customTitanFightProcess)) CancelCustomTitanFightKill("setup waiting or lost");
            return _customTitanGoldActive && _customTitanGoldPendingIndex < 0 && !RouteProcessReady(_customTitanGoldProcess)
                || _customTitanFightActive && !RouteProcessReady(_customTitanFightProcess);
        }

        private void CancelCustomTitanGoldKill(string reason)
        {
            if (!_customTitanGoldActive || !EndRouteProcess(_customTitanGoldProcess)) return;
            _customTitanGoldActive = false; _customTitanGoldProcess = null; _customTitanGoldIndex = -1;
            _customTitanGoldSpawns?.Clear(); _customTitanGoldReadiness = null; _customTitanGoldUsesGoldGear = false;
            _customTitanGoldPendingIndex = -1;
            Log("Titan gold auto-kill ended: " + reason);
        }

        private void CancelCustomTitanFightKill(string reason)
        {
            if (!_customTitanFightActive || !EndRouteProcess(_customTitanFightProcess)) return;
            _customTitanFightActive = false; _customTitanFightProcess = null; _customTitanFightIndex = -1; _customTitanFightReadiness = null;
            Log("Titan autokill route ended: " + reason);
        }
    }
}
