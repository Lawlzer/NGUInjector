using System;
using System.Reflection;
using NGUInjector.Managers;
using Runtime = NGUInjector.Main;

namespace NGUInjector
{
    internal static partial class CombatChecks
    {
        // ZoneHelpers is compiled from production by CombatChecks.csproj. Native evidence:
        // AdventureController.decompiled.cs manageFight(), lines 836-1334: dispatch gates and early stats;
        // lines 3876-3909 and 4058-4336: later achieved methods own stats and kill-count shortcuts.
        // The native-method fakes only record dispatch and return an explicit boundary result.
        private static void CheckTitanEligibility()
        {
            CheckTitanCountdown();
            CheckTitanPreparation();
            CheckTitanDispatchGates();
            CheckEarlyTitanThresholds();
            CheckNumericTitanRequirements();
            CheckTitanVersionDispatch();
        }

        private static Fight TitanFight(int index = 0)
        {
            var fight = new Fight();
            fight.Character.adventure = new TitanAdventure { boss5Kills = 3 };
            fight.Character.adventureController = new TitanAdventureController(fight.Calls);
            fight.Character.buttons.adventure.interactable = true;
            fight.Character.settings.autoKillTitans = true;
            fight.Character.inventory.itemList.itemMaxxed[135] = true;
            fight.Character.AdventureAttack = 20000000;
            fight.Character.AdventureDefense = 10000000;
            fight.Character.AdventureRegen = 200000;
            Runtime.Settings.ManageGoldLoadouts = true;
            Runtime.Settings.TitanGoldTargets = new bool[12];
            Runtime.Settings.TitanMoneyDone = new bool[12];
            Runtime.Settings.TitanSwapTargets = new bool[12];
            Runtime.Settings.TitanGoldTargets[index] = true;
            for (var i = 0; i < 12; i++)
            {
                TitanField(fight, $"boss{i + 1}Spawn", new PlayerTime { totalseconds = 97 });
                if (i >= 5) TitanField(fight, $"titan{i + 1}Unlocked", true);
                for (var version = 0; version < 4; version++)
                    ((TitanAdventureController)fight.Controller).NativeAutokillResults[i, version] = true;
            }
            return fight;
        }

        private static void TitanField(Fight fight, string name, object value)
        {
            fight.Character.adventure.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(fight.Character.adventure, value);
        }

        private static void CheckTitanCountdown()
        {
            for (var index = 0; index < 12; index++)
            {
                var fight = TitanFight(index);
                var controller = (TitanAdventureController)fight.Controller;
                controller.SpawnIntervals[index] = 200 + index;
                foreach (var remaining in new[] { 20.0, 3, 0, -3, -20 })
                {
                    var timer = new PlayerTime { totalseconds = controller.SpawnIntervals[index] - remaining };
                    TitanField(fight, $"boss{index + 1}Spawn", timer);
                    ExpectValue($"titan {index}: actual PlayerTime elapsed", timer.totalseconds, ZoneHelpers.TitanSpawnElapsedSeconds(index));
                    ExpectValue($"titan {index}: signed remaining {remaining}", remaining, ZoneHelpers.TitanSpawnSecondsRemaining(index));
                }
                foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1 })
                {
                    TitanField(fight, $"boss{index + 1}Spawn", new PlayerTime { totalseconds = invalid });
                    ExpectValue($"titan {index}: invalid elapsed {invalid}", true, double.IsNaN(ZoneHelpers.TitanSpawnElapsedSeconds(index)));
                    ExpectValue($"titan {index}: invalid elapsed closes countdown", true, double.IsNaN(ZoneHelpers.TitanSpawnSecondsRemaining(index)));
                }
                TitanField(fight, $"boss{index + 1}Spawn", null);
                ExpectValue($"titan {index}: missing timer", true, double.IsNaN(ZoneHelpers.TitanSpawnElapsedSeconds(index)));
                Runtime.Test = true;
                ExpectValue($"titan {index}: test mode cannot invent elapsed", true, double.IsNaN(ZoneHelpers.TitanSpawnElapsedSeconds(index)));
                ExpectValue($"titan {index}: test mode cannot invent countdown", true, double.IsNaN(ZoneHelpers.TitanSpawnSecondsRemaining(index)));
                ExpectValue($"titan {index}: test mode preserves legacy window", true, ZoneHelpers.TitanSpawnWindowActive(index));
                ExpectValue($"titan {index}: test mode cannot prepare without timer", -1, ZoneHelpers.GetPreparingTitanGoldTarget());
                Runtime.Test = false;
                TitanField(fight, $"boss{index + 1}Spawn", new PlayerTime());
                ExpectValue($"titan {index}: zero elapsed is valid", 0.0, ZoneHelpers.TitanSpawnElapsedSeconds(index));
                foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0, -1 })
                {
                    controller.SpawnIntervals[index] = invalid;
                    ExpectValue($"titan {index}: invalid interval {invalid}", true, double.IsNaN(ZoneHelpers.TitanSpawnSecondsRemaining(index)));
                    ExpectValue($"titan {index}: invalid interval cannot target", -1, ZoneHelpers.GetPreparingTitanGoldTarget());
                }
            }

            var missing = TitanFight();
            missing.Character.adventure = new Adventure();
            ExpectValue("missing native timer field", true, double.IsNaN(ZoneHelpers.TitanSpawnElapsedSeconds(0)));
            missing = TitanFight();
            missing.Character.adventureController = new AdventureController(missing.Calls);
            ExpectValue("missing native interval method", true, double.IsNaN(ZoneHelpers.TitanSpawnSecondsRemaining(0)));
            missing = TitanFight();
            ((TitanAdventureController)missing.Controller).FailSpawnInterval = true;
            ExpectValue("throwing native interval method", true, double.IsNaN(ZoneHelpers.TitanSpawnSecondsRemaining(0)));
            missing.Character.adventureController = null;
            ExpectValue("missing controller", true, double.IsNaN(ZoneHelpers.TitanSpawnSecondsRemaining(0)));
            missing.Character.adventure = null;
            ExpectValue("missing adventure", true, double.IsNaN(ZoneHelpers.TitanSpawnElapsedSeconds(0)));
            Runtime.Character = null;
            ExpectValue("missing character countdown", true, double.IsNaN(ZoneHelpers.TitanSpawnSecondsRemaining(0)));
            ExpectValue("missing character preparation", -1, ZoneHelpers.GetPreparingTitanGoldTarget());
            foreach (var index in new[] { int.MinValue, -1, 12, int.MaxValue })
            {
                ExpectValue("invalid index elapsed " + index, true, double.IsNaN(ZoneHelpers.TitanSpawnElapsedSeconds(index)));
                ExpectValue("invalid index countdown " + index, true, double.IsNaN(ZoneHelpers.TitanSpawnSecondsRemaining(index)));
                ExpectValue("invalid index available " + index, false, ZoneHelpers.TitanAutokillAvailable(index));
                ExpectValue("invalid index numeric requirements " + index, true, ZoneHelpers.TitanAutokillRequirements(index) == null);
                ExpectValue("invalid index achieved " + index, false, ZoneHelpers.TitanAutokillAchieved(index));
                ExpectValue("invalid index version " + index, 0, ZoneHelpers.TitanVersion(index));
            }
        }

        private static void CheckTitanPreparation()
        {
            for (var index = 0; index < 12; index++)
            foreach (var remaining in new[] { 20.0, 3.001, 3, 2.999, .001, 0, -.001, -3, -19.999, -20 })
            {
                var fight = TitanFight(index);
                TitanField(fight, $"boss{index + 1}Spawn", new PlayerTime { totalseconds = 100 - remaining });
                ExpectValue($"titan {index}: three-second preparation at {remaining}", remaining >= 0 && remaining <= 3 ? index : -1,
                    ZoneHelpers.GetPreparingTitanGoldTarget());
                ExpectValue($"titan {index}: legacy signed twenty-second window at {remaining}", Math.Abs(remaining) < 20,
                    ZoneHelpers.TitanSpawnWindowActive(index));
                ExpectValue($"titan {index}: legacy gold target retained at {remaining}", Math.Abs(remaining) < 20 ? index : -1,
                    ZoneHelpers.GetSpawningTitanGoldTarget());
                ExpectValue($"titan {index}: selector does not mark completion", false, Runtime.Settings.TitanMoneyDone[index]);
                fight.Expect("selectors do not change gear or zone");
            }

            var pending = TitanFight();
            Runtime.Settings.TitanGoldTargets[1] = true;
            ExpectValue("first configured pending eligible titan wins", 0, ZoneHelpers.GetPreparingTitanGoldTarget());
            Runtime.Settings.TitanMoneyDone[0] = true;
            ExpectValue("completed titan is skipped", 1, ZoneHelpers.GetPreparingTitanGoldTarget());
            pending.Character.EffectiveBossId = 65;
            ExpectValue("unavailable later target is not selected", -1, ZoneHelpers.GetPreparingTitanGoldTarget());
            pending = TitanFight();
            Runtime.Settings.TitanGoldTargets[1] = true;
            TitanField(pending, "boss1Spawn", new PlayerTime { totalseconds = 96 });
            ExpectValue("first target outside preparation window does not mask next", 1, ZoneHelpers.GetPreparingTitanGoldTarget());
            pending.Character.buttons.adventure.interactable = false;
            ExpectValue("disabled adventure blocks preparation", -1, ZoneHelpers.GetPreparingTitanGoldTarget());

            foreach (var setting in new[] { "disabled", "unconfigured", "complete", "missing targets", "missing done", "short targets", "short done" })
            {
                var fight = TitanFight();
                if (setting == "disabled") Runtime.Settings.ManageGoldLoadouts = false;
                if (setting == "unconfigured") Runtime.Settings.TitanGoldTargets[0] = false;
                if (setting == "complete") Runtime.Settings.TitanMoneyDone[0] = true;
                if (setting == "missing targets") Runtime.Settings.TitanGoldTargets = null;
                if (setting == "missing done") Runtime.Settings.TitanMoneyDone = null;
                if (setting == "short targets") Runtime.Settings.TitanGoldTargets = Array.Empty<bool>();
                if (setting == "short done") Runtime.Settings.TitanMoneyDone = Array.Empty<bool>();
                ExpectValue("pending configuration rejects " + setting, -1, ZoneHelpers.GetPreparingTitanGoldTarget());
                fight.Expect("rejected configuration does not change gear or zone");
            }
        }

        private static void CheckTitanDispatchGates()
        {
            var unlockBossIds = new[] { 58, 66, 82, 100, 116, 132, 426, 467, 491, 777, 826, 850 };
            for (var index = 0; index < 12; index++)
            {
                var fight = TitanFight(index);
                fight.Character.EffectiveBossId = unlockBossIds[index];
                fight.Character.bossID = 1;
                ExpectValue($"titan {index}: exact native progression gate", true, ZoneHelpers.TitanAutokillAvailable(index));
                ExpectValue($"titan {index}: gate admits configured pre-spawn target", index, ZoneHelpers.GetPreparingTitanGoldTarget());
                ExpectValue($"titan {index}: fixture initially achieves autokill", true, ZoneHelpers.TitanAutokillAchieved(index));
                foreach (var gate in new[] { "setting", "boss", "progression", "prerequisite" })
                {
                    if (gate == "prerequisite" && index < 3) continue;
                    if (gate == "setting") fight.Character.settings.autoKillTitans = false;
                    if (gate == "boss") fight.Character.bossID = 0;
                    if (gate == "progression") fight.Character.EffectiveBossId--;
                    if (gate == "prerequisite" && index == 3) fight.Character.inventory.itemList.itemMaxxed[135] = false;
                    if (gate == "prerequisite" && index == 4) fight.Character.adventure.boss5Kills = 2;
                    if (gate == "prerequisite" && index >= 5) TitanField(fight, $"titan{index + 1}Unlocked", false);
                    var controller = (TitanAdventureController)fight.Controller;
                    controller.NativeAutokillCalls.Clear();
                    ExpectValue($"titan {index}: {gate} blocks availability", false, ZoneHelpers.TitanAutokillAvailable(index));
                    ExpectValue($"titan {index}: {gate} has no numeric requirements", true, ZoneHelpers.TitanAutokillRequirements(index) == null);
                    ExpectValue($"titan {index}: {gate} blocks targeting", -1, ZoneHelpers.GetPreparingTitanGoldTarget());
                    ExpectValue($"titan {index}: {gate} blocks achieved", false, ZoneHelpers.TitanAutokillAchieved(index));
                    ExpectValue($"titan {index}: {gate} rejects before native stat dispatch", 0, controller.NativeAutokillCalls.Count);
                    fight.Expect($"titan {index}: {gate} rejection has no gear/zone actions");
                    fight.Character.settings.autoKillTitans = true;
                    fight.Character.bossID = 1;
                    fight.Character.EffectiveBossId = unlockBossIds[index];
                    fight.Character.inventory.itemList.itemMaxxed[135] = true;
                    fight.Character.adventure.boss5Kills = 3;
                    if (index >= 5) TitanField(fight, $"titan{index + 1}Unlocked", true);
                }

                fight.Character.AdventureAttack = fight.Character.AdventureDefense = fight.Character.AdventureRegen = 0;
                ExpectValue($"titan {index}: availability is non-stat gating before trial gear", true, ZoneHelpers.TitanAutokillAvailable(index));
                ExpectValue($"titan {index}: preparation permits later gear trial", index, ZoneHelpers.GetPreparingTitanGoldTarget());
                TitanField(fight, $"boss{index + 1}Spawn", null);
                ExpectValue($"titan {index}: availability does not require timer", true, ZoneHelpers.TitanAutokillAvailable(index));
            }

            var missing = TitanFight(3);
            missing.Character.inventory.itemList.itemMaxxed = new bool[135];
            ExpectValue("missing maxxed item prerequisite fails closed", false, ZoneHelpers.TitanAutokillAvailable(3));
            ExpectValue("missing item prerequisite cannot target", -1, ZoneHelpers.GetPreparingTitanGoldTarget());
            missing = TitanFight(5);
            missing.Character.adventure = new Adventure();
            ExpectValue("missing native unlock field fails closed", false, ZoneHelpers.TitanAutokillAvailable(5));
            var disabled = TitanFight();
            disabled.Character.settings.autoKillTitans = false;
            Runtime.Settings.ManageGoldLoadouts = false;
            ExpectValue("native autokill disabled also blocks fight-gear trials", -1, ZoneHelpers.GetSpawningTitanFightTarget());
            Runtime.Character = null;
            ExpectValue("missing character eligibility fails closed", false, ZoneHelpers.TitanAutokillAvailable(0));
            ExpectValue("missing character has no numeric requirements", true, ZoneHelpers.TitanAutokillRequirements(0) == null);
        }

        private static void CheckNumericTitanRequirements()
        {
            var power = new[] { 3000f, 9000f, 25000f, 800000f, 13000000f };
            var toughness = new[] { 2500f, 7000f, 15000f, 400000f, 7000000f };
            for (var boss = 0; boss < 5; boss++)
            {
                var fight = TitanFight(boss);
                var requirements = ZoneHelpers.TitanAutokillRequirements(boss);
                ExpectValue($"titan {boss}: three numeric dimensions", 3, requirements.Length);
                for (var dimension = 0; dimension < 2; dimension++)
                {
                    var native = dimension == 0 ? power[boss] : toughness[boss];
                    var bits = BitConverter.ToInt32(BitConverter.GetBytes(native), 0);
                    var minimum = BitConverter.ToSingle(BitConverter.GetBytes(bits + (boss < 3 ? 1 : 0)), 0);
                    ExpectValue($"titan {boss}: exact float minimum {dimension}", minimum, requirements[dimension]);
                    foreach (var offset in new[] { -1, 0, 1 })
                    {
                        fight.Character.AdventureAttack = requirements[0];
                        fight.Character.AdventureDefense = requirements[1];
                        fight.Character.AdventureRegen = requirements[2];
                        var boundary = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(minimum), 0) + offset), 0);
                        if (dimension == 0) fight.Character.AdventureAttack = boundary;
                        else fight.Character.AdventureDefense = boundary;
                        ExpectValue($"titan {boss}: adjacent float {dimension}/{offset}", offset >= 0, ZoneHelpers.TitanAutokillAchieved(boss));
                    }
                }
                ExpectValue($"titan {boss}: regen minimum", boss < 3 ? 0f : boss == 3 ? 14000f : 150000f, requirements[2]);
                requirements[0] = 0;
                ExpectValue($"titan {boss}: caller cannot mutate shared table", true, ZoneHelpers.TitanAutokillRequirements(boss)[0] > 0);
            }
            for (var boss = 5; boss < 12; boss++)
            for (var version = 0; version < 4; version++)
            {
                var fight = TitanFight(boss);
                TitanField(fight, $"titan{boss + 1}Version", version);
                var requirements = ZoneHelpers.TitanAutokillRequirements(boss);
                ExpectValue($"titan {boss}/V{version + 1}: three numeric dimensions", 3, requirements.Length);
                foreach (var value in requirements)
                    ExpectValue($"titan {boss}/V{version + 1}: positive finite binary32 threshold", true, value > 0 && !float.IsInfinity(value));
                ExpectValue("numeric lookup never calls achieved", 0, ((TitanAdventureController)fight.Controller).NativeAutokillCalls.Count);
            }
            var rounded = TitanFight(9);
            TitanField(rounded, "titan10Version", 1);
            ExpectValue("T10 V2 uses native float literal, not decimal-double threshold", (double)3.2E+29f, (double)ZoneHelpers.TitanAutokillRequirements(9)[0]);
            ExpectValue("float literal differs from same decimal double", true, (double)ZoneHelpers.TitanAutokillRequirements(9)[0] != 3.2E+29);
        }

        private static void CheckEarlyTitanThresholds()
        {
            var attack = new[] { 3000f, 9000f, 25000f, 800000f, 13000000f };
            var defense = new[] { 2500f, 7000f, 15000f, 400000f, 7000000f };
            var regen = new[] { 0f, 0f, 0f, 14000f, 150000f };
            for (var index = 0; index < 5; index++)
            foreach (var stat in new[] { "attack", "defense", "regen" })
            foreach (var offset in new[] { -1, 0, 1 })
            {
                var fight = TitanFight(index);
                fight.Character.AdventureAttack = attack[index] + 1;
                fight.Character.AdventureDefense = defense[index] + 1;
                fight.Character.AdventureRegen = regen[index] + 1;
                if (stat == "attack") fight.Character.AdventureAttack = attack[index] + offset;
                if (stat == "defense") fight.Character.AdventureDefense = defense[index] + offset;
                if (stat == "regen") fight.Character.AdventureRegen = regen[index] + offset;
                TitanField(fight, $"boss{index + 1}Spawn", null);
                var expected = stat == "regen" && index < 3 || (index < 3 ? offset > 0 : offset >= 0);
                ExpectValue($"titan {index}: native {stat} boundary {offset}, no timestamp required", expected,
                    ZoneHelpers.TitanAutokillAchieved(index));
                ExpectValue($"titan {index}: early stats never call later native methods", 0,
                    ((TitanAdventureController)fight.Controller).NativeAutokillCalls.Count);
            }
        }

        private static void CheckTitanVersionDispatch()
        {
            for (var index = 5; index < 12; index++)
            foreach (var version in new[] { 0, 1, 2, 3, -1, 4, int.MinValue, int.MaxValue })
            {
                var fight = TitanFight(index);
                var controller = (TitanAdventureController)fight.Controller;
                TitanField(fight, $"titan{index + 1}Version", version);
                var valid = version >= 0 && version <= 3;
                ExpectValue($"titan {index}: stored version {version} maps exactly", valid ? version + 1 : 0, ZoneHelpers.TitanVersion(index));
                ExpectValue($"titan {index}: version {version} gates availability", valid, ZoneHelpers.TitanAutokillAvailable(index));
                ExpectValue($"titan {index}: version {version} gates numeric requirements", valid, ZoneHelpers.TitanAutokillRequirements(index) != null);
                ExpectValue($"titan {index}: version {version} gates preparation", valid ? index : -1, ZoneHelpers.GetPreparingTitanGoldTarget());
                ExpectValue($"titan {index}: preparation does not call native stats", 0, controller.NativeAutokillCalls.Count);
                TitanField(fight, $"boss{index + 1}Spawn", null);
                ExpectValue($"titan {index}: achieved version {version} independent of timestamp", valid, ZoneHelpers.TitanAutokillAchieved(index));
                ExpectValue($"titan {index}: exact version dispatch {version}", valid ? $"{index + 1}:V{version + 1}" : "",
                    string.Join(",", controller.NativeAutokillCalls));
                if (!valid) continue;
                controller.NativeAutokillResults[index, version] = false;
                ExpectValue($"titan {index} version {version}: native false respected even with other versions true", false,
                    ZoneHelpers.TitanAutokillAchieved(index));
                controller.FailNativeAutokill = true;
                ExpectValue($"titan {index} version {version}: throwing native method fails closed", false, ZoneHelpers.TitanAutokillAchieved(index));
                fight.Character.adventureController = new AdventureController(fight.Calls);
                ExpectValue($"titan {index} version {version}: missing native method fails closed", false, ZoneHelpers.TitanAutokillAchieved(index));
            }
            var missing = TitanFight(5);
            missing.Character.adventure = new UnversionedTitanAdventure();
            ExpectValue("missing version does not default to V1", 0, ZoneHelpers.TitanVersion(5));
            ExpectValue("unlocked but missing version is unavailable", false, ZoneHelpers.TitanAutokillAvailable(5));
            ExpectValue("missing version has no numeric requirements", true, ZoneHelpers.TitanAutokillRequirements(5) == null);
            ExpectValue("unlocked but missing version cannot achieve", false, ZoneHelpers.TitanAutokillAchieved(5));
            ExpectValue("unlocked but missing version cannot target", -1, ZoneHelpers.GetPreparingTitanGoldTarget());
        }
    }
}
