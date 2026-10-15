using System;
using System.Linq;
using System.Reflection;

namespace NGUInjector.Managers
{
    static class ZoneHelpers
    {
        internal static readonly int[] TitanZones = { 6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42 };
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
                if (TitanGoldTargetActive(i))
                    continue;

                if (TitanSpawnWindowActive(i) && !TitanAutokillAchieved(i))
                    return i;
            }

            return -1;
        }

        internal static bool TitanAutokillAchieved(int bossId)
        {
            if (bossId < 5 || bossId >= TitanZones.Length)
                return false;

            try
            {
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
                return 1;

            try
            {
                var field = Main.Character.adventure.GetType().GetField(
                    $"titan{bossId + 1}Version",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var value = field?.GetValue(Main.Character.adventure);
                if (value is int version)
                    return Math.Max(1, Math.Min(4, version));
            }
            catch
            {
                // Fall back to V1; missing version data should not break titan routing.
            }

            return 1;
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
            var controller = Main.Character.adventureController;
            var adventure = Main.Character.adventure;

            var spawnMethod = controller.GetType().GetMethod($"boss{bossId + 1}SpawnTime",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var spawnTimeObj = spawnMethod?.Invoke(controller, null);
            if (spawnTimeObj == null)
                return false;
            var spawnTime = (float) spawnTimeObj;

            var spawnField = adventure.GetType().GetField($"boss{bossId + 1}Spawn",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var spawnObj = spawnField?.GetValue(adventure);

            if (spawnObj == null)
                return false;

            var spawn = (PlayerTime) spawnObj;
            return Math.Abs(spawnTime - spawn.totalseconds) < 20;
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
