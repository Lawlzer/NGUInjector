using System;
using System.Linq;
using System.Reflection;

namespace NGUInjector.Managers
{
    static class ZoneHelpers
    {
        internal static readonly int[] TitanZones = { 6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42 };
        // Native manageFight() for T1-T5, then autokillTitan{N}V{1..4}Achieved().
        // These are binary32 literals, not enemy stats or rounded UI requirements.
        private static readonly float[,] TitanAutokillStats =
        {
            { 3000f, 2500f, 0f },
            { 9000f, 7000f, 0f },
            { 25000f, 15000f, 0f },
            { 800000f, 400000f, 14000f },
            { 13000000f, 7000000f, 150000f },
            // T6
            { 2.5E+09f, 1.6E+09f, 25000000f },
            { 2.5E+10f, 1.6E+10f, 250000000f },
            { 2.5E+11f, 1.6E+11f, 2.5E+09f },
            { 2.5E+12f, 1.6E+12f, 2.5E+10f },
            // T7
            { 5E+14f, 2.5E+14f, 5E+12f },
            { 1E+16f, 5E+15f, 1E+14f },
            { 2E+17f, 1E+17f, 2E+15f },
            { 5E+18f, 2.5E+18f, 5E+16f },
            // T8
            { 5E+18f, 2.5E+18f, 5E+16f },
            { 1E+20f, 5E+19f, 1E+18f },
            { 2E+21f, 1E+21f, 2E+19f },
            { 5E+22f, 2.5E+22f, 5E+20f },
            // T9
            { 1E+23f, 5E+22f, 1E+21f },
            { 2E+24f, 1E+24f, 2E+22f },
            { 4E+25f, 2E+25f, 4E+23f },
            { 7.5E+26f, 3.7E+26f, 7.5E+24f },
            // T10
            { 4E+28f, 2E+28f, 4E+26f },
            { 3.2E+29f, 1.6E+29f, 1.6E+27f },
            { 2E+30f, 1E+30f, 1E+28f },
            { 1E+31f, 5E+30f, 5E+28f },
            // T11
            { 1.8E+31f, 6E+30f, 1.2E+29f },
            { 9E+31f, 3E+31f, 6E+29f },
            { 3.6E+32f, 1.2E+32f, 2.5E+30f },
            { 1.1E+33f, 3.6E+32f, 7.5E+30f },
            // T12
            { 3E+33f, 1E+33f, 2E+31f },
            { 1.2E+34f, 4E+33f, 8E+31f },
            { 3.6E+34f, 1.2E+34f, 2.4E+32f },
            { 7.2E+34f, 2.4E+34f, 4.8E+32f }
        };
        // Mirrors AdventureController.constructDropdown(), but checked against current rebirth progress.
        private static readonly int[] ZoneUnlockEffectiveBossIds =
        {
            0,
            7,
            17,
            37,
            48,
            58,
            58,
            66,
            66,
            74,
            82,
            82,
            90,
            100,
            100,
            108,
            116,
            116,
            124,
            132,
            137,
            359,
            401,
            426,
            459,
            467,
            467,
            475,
            483,
            491,
            491,
            501,
            727,
            752,
            777,
            810,
            818,
            826,
            826,
            834,
            842,
            850,
            850,
            871,
            897,
            902
        };

        internal static bool ZoneIsTitan(int zone)
        {
            // Tippi and the Traitor are titans too, but lack automated titan targets here.
            return zone == 44 || zone == 45 || TitanZones.Contains(zone);
        }

        internal static bool IsZoneUnlocked(int zone)
        {
            if (zone == -1 || zone >= 1000)
                return true;

            if (zone < 0 || zone >= ZoneUnlockEffectiveBossIds.Length)
                return false;

            return CurrentRebirthEffectiveBossId() >= ZoneUnlockEffectiveBossIds[zone];
        }

        internal static int ZoneUnlockEffectiveBossId(int zone)
        {
            if (zone == -1 || zone >= 1000)
                return 0;

            if (zone < 0 || zone >= ZoneUnlockEffectiveBossIds.Length)
                return int.MaxValue;

            return ZoneUnlockEffectiveBossIds[zone];
        }

        internal static int CurrentRebirthEffectiveBossId()
        {
            return Main.Character == null ? 0 : Main.Character.effectiveBossID();
        }

        internal static TitanSpawn TitansSpawningSoon()
        {
            var result = new TitanSpawn();

            if (!Main.Character.buttons.adventure.IsInteractable())
                return result;
            for (var i = 0; i < TitanZones.Length; i++)
            {
                result.Merge(GetTitanSpawn(i));
            }
            return result;
        }

        internal static bool TitanSpawningSoon(int boss)
        {
            return Main.Character.buttons.adventure.IsInteractable() && CheckTitanSpawnTime(boss);
        }

        internal static int GetSpawningTitanGoldTarget()
        {
            if (!Main.Settings.ManageGoldLoadouts || Main.Settings.TitanGoldTargets == null || Main.Settings.TitanMoneyDone == null)
                return -1;

            for (var i = 0; i < TitanZones.Length; i++)
            {
                if (TitanGoldTargetActive(i))
                    return i;
            }

            return -1;
        }

        internal static int GetSpawningTitanFightTarget()
        {
            for (var i = 0; i < TitanZones.Length; i++)
            {
                if (TitanGoldTargetActive(i) || !TitanAutokillAvailable(i))
                    continue;

                if (TitanSpawnWindowActive(i) && !TitanAutokillAchieved(i))
                    return i;
            }

            return -1;
        }

        internal static int GetPreparingTitanGoldTarget()
        {
            if (Main.Character == null || !Main.Character.buttons.adventure.IsInteractable())
                return -1;

            for (var i = 0; i < TitanZones.Length; i++)
            {
                if (TitanGoldTargetPreparing(i))
                    return i;
            }

            return -1;
        }

        internal static bool TitanGoldTargetPreparing(int bossId)
        {
            if (!Main.Character.buttons.adventure.IsInteractable()
                || !TitanGoldTargetPending(bossId) || !TitanAutokillAvailable(bossId))
                return false;

            var remaining = TitanSpawnSecondsRemaining(bossId);
            return remaining >= 0 && remaining <= 3;
        }

        internal static bool TitanAutokillAvailable(int bossId)
        {
            if (bossId < 0 || bossId >= TitanZones.Length)
                return false;

            try
            {
                var character = Main.Character;
                if (character == null || !character.settings.autoKillTitans || character.bossID < 1
                    || !IsZoneUnlocked(TitanZones[bossId]))
                    return false;

                // AdventureController.manageFight() gates, without its due-time or stat checks.
                if (bossId == 3)
                    return character.inventory.itemList.itemMaxxed[135];
                if (bossId == 4)
                    return character.adventure.boss5Kills >= 3;
                if (bossId < 5)
                    return true;

                var field = character.adventure.GetType().GetField($"titan{bossId + 1}Unlocked",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field?.GetValue(character.adventure) is bool unlocked && unlocked && TitanVersion(bossId) > 0;
            }
            catch
            {
                return false;
            }
        }

        internal static float[] TitanAutokillRequirements(int bossId)
        {
            if (!TitanAutokillAvailable(bossId))
                return null;

            var version = bossId < 5 ? 0 : TitanVersion(bossId);
            if (bossId >= 5 && version == 0)
                return null;
            var row = bossId < 5 ? bossId : 5 + (bossId - 5) * 4 + version - 1;
            var requirements = new[] { TitanAutokillStats[row, 0], TitanAutokillStats[row, 1], TitanAutokillStats[row, 2] };
            // Positive finite thresholds: advancing the bits converts native > into numeric >=.
            if (bossId < 3)
                for (var i = 0; i < 2; i++)
                    requirements[i] = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(requirements[i]), 0) + 1), 0);
            return requirements;
        }

        internal static bool TitanAutokillAchieved(int bossId)
        {
            if (!TitanAutokillAvailable(bossId))
                return false;

            try
            {
                var character = Main.Character;
                // Native early-titan thresholds live in manageFight(), not separate achieved methods.
                switch (bossId)
                {
                    case 0: return character.totalAdvAttack() > 3000f && character.totalAdvDefense() > 2500f;
                    case 1: return character.totalAdvAttack() > 9000f && character.totalAdvDefense() > 7000f;
                    case 2: return character.totalAdvAttack() > 25000f && character.totalAdvDefense() > 15000f;
                    case 3: return character.totalAdvAttack() >= 800000f && character.totalAdvDefense() >= 400000f
                                   && character.totalAdvHPRegen() >= 14000f;
                    case 4: return character.totalAdvAttack() >= 13000000f && character.totalAdvDefense() >= 7000000f
                                   && character.totalAdvHPRegen() >= 150000f;
                }

                var method = Main.Character.adventureController.GetType().GetMethod(
                    $"autokillTitan{bossId + 1}V{TitanVersion(bossId)}Achieved",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var result = method?.Invoke(Main.Character.adventureController, null);
                return result is bool achieved && achieved;
            }
            catch
            {
                return false;
            }
        }

        internal static int TitanVersion(int bossId)
        {
            if (bossId < 5 || bossId >= TitanZones.Length)
                return 0;

            try
            {
                var field = Main.Character.adventure.GetType().GetField(
                    $"titan{bossId + 1}Version",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var value = field?.GetValue(Main.Character.adventure);
                if (value is int version && version >= 0 && version <= 3)
                    return version + 1;
            }
            catch
            {
                // Missing or invalid native version data must not select an easier autokill.
            }

            return 0;
        }

        internal static Enemy TitanEnemy(int bossId)
        {
            if (bossId < 0 || bossId >= TitanZones.Length)
                return null;

            var zone = TitanZones[bossId];
            var enemies = Main.Character.adventureController.enemyList;
            if (zone < 0 || zone >= enemies.Count)
                return null;

            return enemies[zone].FirstOrDefault(x => x.enemyType == enemyType.boss || x.enemyType.ToString().Contains("bigBoss"))
                   ?? enemies[zone].FirstOrDefault();
        }

        internal static bool TitanGoldTargetActive(int bossId)
        {
            return TitanGoldTargetPending(bossId) && TitanSpawnWindowActive(bossId);
        }

        internal static bool TitanSpawnWindowActive(int bossId)
        {
            return Main.Character.buttons.adventure.IsInteractable()
                   && bossId >= 0
                   && bossId < TitanZones.Length
                   && TitanZones[bossId] <= GetMaxReachableZone(true)
                   && CheckTitanSpawnTime(bossId);
        }

        internal static void MarkTitanMoneyDone(int bossId)
        {
            if (Main.Settings.TitanMoneyDone == null || bossId < 0 || bossId >= Main.Settings.TitanMoneyDone.Length)
                return;

            var temp = Main.Settings.TitanMoneyDone.ToArray();
            temp[bossId] = true;
            Main.Settings.TitanMoneyDone = temp;
        }

        private static TitanSpawn GetTitanSpawn(int bossId)
        {
            var result = new TitanSpawn();

            if (TitanZones[bossId] > GetMaxReachableZone(true))
                return result;

            if (!CheckTitanSpawnTime(bossId)) return result;

            result.SwapLoadout = Main.Settings.SwapTitanLoadouts && Main.Settings.TitanSwapTargets[bossId];
            // Run money once for each boss
            result.RunMoneyLoadout = TitanGoldTargetPending(bossId);
            result.GoldTargetIndex = result.RunMoneyLoadout ? bossId : -1;
            result.SpawningSoon = result.SwapLoadout || result.RunMoneyLoadout;

            return result;
        }

        private static bool TitanGoldTargetPending(int bossId)
        {
            return Main.Settings.TitanGoldTargets != null
                   && Main.Settings.TitanMoneyDone != null
                   && bossId >= 0
                   && bossId < TitanZones.Length
                   && bossId < Main.Settings.TitanGoldTargets.Length
                   && bossId < Main.Settings.TitanMoneyDone.Length
                   && TitanZones[bossId] <= GetMaxReachableZone(true)
                   && Main.Settings.ManageGoldLoadouts
                   && Main.Settings.TitanGoldTargets[bossId]
                   && !Main.Settings.TitanMoneyDone[bossId];
        }

        private static bool CheckTitanSpawnTime(int bossId)
        {
            if (Main.Test) return true;
            return Math.Abs(TitanSpawnSecondsRemaining(bossId)) < 20;
        }

        internal static double TitanSpawnElapsedSeconds(int bossId)
        {
            if (bossId < 0 || bossId >= TitanZones.Length)
                return double.NaN;

            try
            {
                var adventure = Main.Character.adventure;
                var field = adventure.GetType().GetField($"boss{bossId + 1}Spawn",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var timer = field?.GetValue(adventure) as PlayerTime;
                var elapsed = timer == null ? double.NaN : timer.totalseconds;
                return double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0 ? double.NaN : elapsed;
            }
            catch
            {
                return double.NaN;
            }
        }

        internal static double TitanSpawnSecondsRemaining(int bossId)
        {
            var elapsed = TitanSpawnElapsedSeconds(bossId);
            if (double.IsNaN(elapsed))
                return double.NaN;

            try
            {
                var controller = Main.Character.adventureController;
                var method = controller.GetType().GetMethod($"boss{bossId + 1}SpawnTime",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var value = method?.Invoke(controller, null);
                if (!(value is float interval) || float.IsNaN(interval) || float.IsInfinity(interval) || interval <= 0)
                    return double.NaN;

                return interval - elapsed;
            }
            catch
            {
                return double.NaN;
            }
        }

        internal static int GetMaxReachableZone(bool includingTitans)
        {
            for (var i = ZoneUnlockEffectiveBossIds.Length - 1; i >= 0; i--)
            {
                if (!includingTitans && ZoneIsTitan(i))
                    continue;

                if (IsZoneUnlocked(i))
                    return i;
            }

            return 0;
        }

        internal static void OptimizeITOPOD()
        {
            if (!Main.Settings.OptimizeITOPODFloor) return;
            if (Main.Character.arbitrary.boughtLazyITOPOD && Main.Character.arbitrary.lazyITOPODOn) return;
            if (Main.Character.adventure.zone < 1000) return;
            var controller = Main.Character.adventureController;
            var level = controller.itopodLevel;
            var optimal = CalculateBestItopodLevel();
            if (level == optimal) return; // we are on optimal floor
            var highestOpen = Main.Character.adventure.highestItopodLevel;
            var climbing = (level < optimal && level >= highestOpen - 1);
            controller.itopodStartInput.text = optimal.ToString();
            if (climbing)
                optimal++;
            controller.itopodEndInput.text = optimal.ToString();
            controller.verifyItopodInputs();
            if (!climbing)
                controller.zoneSelector.changeZone(1000);
        }

        internal static int CalculateBestItopodLevel()
        {
            var c = Main.Character;
            var num1 = c.totalAdvAttack() / 765f * (Main.Settings.ITOPODCombatMode == 1 || c.training.attackTraining[1] == 0 ? c.idleAttackPower() : c.regAttackPower());
            if (c.totalAdvAttack() < 700.0)
                return 0;
            var num2 = Convert.ToInt32(Math.Floor(Math.Log(num1, 1.05)));
            if (num2 < 1)
                return 1;
            var maxLevel = c.adventureController.maxItopodLevel();
            if (num2 > maxLevel)
                num2 = maxLevel;
            return num2;
        }
    }

    public class TitanSpawn
    {
        internal bool SpawningSoon { get; set; }
        internal bool SwapLoadout { get; set; }
        internal bool RunMoneyLoadout { get; set; }
        internal int GoldTargetIndex { get; set; } = -1;

        internal void Merge(TitanSpawn other)
        {
            SpawningSoon = SpawningSoon || other.SpawningSoon;
            SwapLoadout = SwapLoadout || other.SwapLoadout;
            if (GoldTargetIndex < 0 && other.GoldTargetIndex >= 0)
                GoldTargetIndex = other.GoldTargetIndex;
            RunMoneyLoadout = RunMoneyLoadout || other.RunMoneyLoadout;
        }
    }
}
