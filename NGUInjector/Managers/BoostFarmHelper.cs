using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NGUInjector.Managers
{
    internal sealed class BoostFarmTarget
    {
        internal int Zone { get; set; }
        internal bool IsItopod { get; set; }
        internal double Score { get; set; }
        internal int ExpectedHits { get; set; }
        internal int FightType { get; set; }
        internal string Summary { get; set; }
    }

    internal static class BoostFarmHelper
    {
        private const int ItopodZone = 1000;
        private const double DefaultIdleAttackModifier = 1.2d;
        private const double ItopodBoostDropChance = 0.14d;

        private static readonly int[] BoostValues = {1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000};
        private static readonly int[] ItopodBoostFloors = {0, 50, 100, 150, 200, 250, 300, 350, 400, 450, 700, 850, 1150};

        // Farmer Sanc's Boost Almanac normal-zone boost table, NGU 1.220.
        private static readonly BoostFarmZone[] Zones =
        {
            new BoostFarmZone(0, 0.75d, 109d, Drop(1, 0.15d, 1d)),
            new BoostFarmZone(1, 0.75d, 163d, Drop(1, 0.15d, 1d)),
            new BoostFarmZone(2, 0.7778d, 946d, Drop(1, 0.12d, 1d), Drop(2, 0.08d, 1d)),
            new BoostFarmZone(3, 0.8125d, 3190d, Drop(1, 0.13d, 1d), Drop(2, 0.12d, 1d)),
            new BoostFarmZone(4, 0.80d, 9550d, Drop(2, 0.08d, 1d), Drop(5, 0.08d, 1d)),
            new BoostFarmZone(5, 0.80d, 12700d, Drop(2, 0.06d, 1d), Drop(5, 0.015d, 1d)),
            new BoostFarmZone(7, 0.7778d, 89400d, Drop(5, 0.03d, 0.15d), Drop(10, 0.03d, 0.15d)),
            new BoostFarmZone(9, 0.75d, 140000d, Drop(10, 0.07d, 0.15d), Drop(20, 0.07d, 0.15d)),
            new BoostFarmZone(10, 0.75d, 353000d, Drop(10, 0.06d, 0.20d), Drop(20, 0.06d, 0.20d)),
            new BoostFarmZone(12, 0.75d, 1050000d, Drop(20, 0.03d, 0.25d), Drop(50, 0.03d, 0.25d)),
            new BoostFarmZone(13, 0.80d, 4410000d, Drop(50, 0.011d, 0.15d), Drop(100, 0.011d, 0.15d)),
            new BoostFarmZone(15, 0.75d, 57700000d, Drop(50, 0.0035d, 0.25d), Drop(100, 0.0035d, 0.25d)),
            new BoostFarmZone(17, 0.75d, 1110000000d, Drop(100, 0.001d, 0.20d), Drop(200, 0.001d, 0.20d)),
            new BoostFarmZone(18, 0.7778d, 9000000000d, Drop(200, 0.00012d, 0.20d), Drop(500, 0.00012d, 0.20d)),
            new BoostFarmZone(20, 0.7692d, 3400000000000d, Drop(200, 0.00055d, 0.10d), Drop(500, 0.00055d, 0.10d)),
            new BoostFarmZone(21, 0.7778d, 549000000000000d, Drop(200, 0.00012d, 0.10d), Drop(500, 0.00012d, 0.10d)),
            new BoostFarmZone(22, 0.75d, 2830000000000000d, Drop(500, 0.00010d, 0.08d), Drop(1000, 0.00010d, 0.06d)),
            new BoostFarmZone(24, 0.75d, 1310000000000000000d, Drop(1000, 0.00005d, 0.07d), Drop(2000, 0.00005d, 0.07d)),
            new BoostFarmZone(25, 0.75d, 13100000000000000000d, Drop(1000, 0.00003d, 0.08d), Drop(2000, 0.00003d, 0.08d)),
            new BoostFarmZone(27, 0.75d, 8630000000000000000000d, Drop(1000, 0.000022d, 0.09d), Drop(2000, 0.000022d, 0.09d)),
            new BoostFarmZone(28, 0.75d, 44500000000000000000000d, Drop(2000, 0.000018d, 0.10d), Drop(5000, 0.000018d, 0.10d)),
            new BoostFarmZone(29, 0.75d, 235000000000000000000000d, Drop(2000, 0.000015d, 0.10d), Drop(5000, 0.000015d, 0.10d)),
            new BoostFarmZone(31, 0.80d, 235000000000000000000000000d, Drop(2000, 0.0000006d, 0.15d), Drop(5000, 0.0000006d, 0.15d)),
            new BoostFarmZone(32, 0.75d, 65400000000000000000000000000d, Drop(5000, 0.0000004d, 0.10d), Drop(10000, 0.0000004d, 0.10d)),
            new BoostFarmZone(33, 0.75d, 277000000000000000000000000000d, Drop(5000, 0.00000025d, 0.15d), Drop(10000, 0.00000025d, 0.15d)),
            new BoostFarmZone(35, 0.75d, 6320000000000000000000000000000d, Drop(5000, 0.00000010d, 0.15d), Drop(10000, 0.00000010d, 0.15d)),
            new BoostFarmZone(36, 0.75d, 21700000000000000000000000000000d, Drop(10000, 0.00000006d, 0.15d), Drop(10000, 0.00000006d, 0.15d)),
            new BoostFarmZone(37, 0.75d, 68400000000000000000000000000000d, Drop(10000, 0.00000004d, 0.15d), Drop(10000, 0.00000004d, 0.15d)),
            new BoostFarmZone(39, 0.75d, 2210000000000000000000000000000000d, Drop(10000, 0.000000025d, 0.16d), Drop(10000, 0.000000025d, 0.16d)),
            new BoostFarmZone(40, 0.75d, 5330000000000000000000000000000000d, Drop(10000, 0.000000020d, 0.17d), Drop(10000, 0.000000020d, 0.17d)),
            new BoostFarmZone(41, 0.75d, 13700000000000000000000000000000000d, Drop(10000, 0.000000016d, 0.17d), Drop(10000, 0.000000016d, 0.17d)),
            new BoostFarmZone(43, 0.8095d, 877000000000000000000000000000000000d, Drop(10000, 0.000000010d, 0.17d), Drop(10000, 0.000000010d, 0.17d))
        };

        // Immutable Almanac index; recurring acquired-route observation must not scan alternatives.
        private static readonly Dictionary<int, BoostFarmZone> ZonesById = Zones.ToDictionary(x => x.Zone);
        internal static object PresetZoneRow(int zone)
        {
            BoostFarmZone row;
            return ZonesById.TryGetValue(zone, out row) ? row : null;
        }

        internal static BoostFarmTarget GetBestTarget()
        {
            var recycledBoosts = BuildRecycledBoostValues();
            var filterMultipliers = BuildBoostFilterMultipliers();
            var best = ScoreItopod(recycledBoosts, filterMultipliers);
            foreach (var zone in Zones)
            {
                var target = ScoreZone(zone, recycledBoosts, filterMultipliers);
                if (target == null)
                    continue;

                if (best == null || target.Score > best.Score)
                    best = target;
            }

            return best != null && best.Score > 0d ? best : null;
        }

        internal static string FormatTarget(BoostFarmTarget target)
        {
            if (target == null)
                return "none";

            return string.Format("{0}; score {1:0.#####}; {2}", target.IsItopod ? "ITOPOD" : SafeZoneName(target.Zone), target.Score, target.Summary);
        }

        private static BoostFarmTarget ScoreItopod(double[] recycledBoosts, double[] filterMultipliers)
        {
            if (!ItopodUnlocked())
                return null;

            var floor = ZoneHelpers.CalculateBestItopodLevel();
            var boostIndex = ItopodBoostIndex(floor);
            var score = recycledBoosts[boostIndex] * ItopodBoostDropChance * filterMultipliers[boostIndex];
            return new BoostFarmTarget
            {
                Zone = ItopodZone,
                IsItopod = true,
                Score = score,
                ExpectedHits = 1,
                FightType = 2,
                Summary = string.Format("floor {0}, drops {1} boosts", floor, BoostValues[boostIndex])
            };
        }

        private static bool ItopodUnlocked()
        {
            try
            {
                return Main.Character.inventory.itemList.itopodKeyComplete
                       || Main.Character.adventure.highestItopodLevel > 0
                       || Main.Character.adventure.itopod.enemiesKilled > 0;
            }
            catch
            {
                return true;
            }
        }

        private static BoostFarmTarget ScoreZone(BoostFarmZone zone, double[] recycledBoosts, double[] filterMultipliers)
        {
            if (!ZoneHelpers.IsZoneUnlocked(zone.Zone) || ZoneHelpers.ZoneIsTitan(zone.Zone))
                return null;

            var effectivePower = EffectiveAdventurePower();
            if (effectivePower <= 0d)
                return null;

            var expectedHits = Math.Max(1, (int) Math.Ceiling(zone.OneHitPower / effectivePower));
            int fightType;
            if (!CanFarmZone(zone.Zone, expectedHits, out fightType))
                return null;

            var rootedDropChance = zone.Zone >= 20;
            var dropMultiplier = PlayerDropMultiplier(rootedDropChance);
            var averageBoosts = 0d;
            foreach (var drop in zone.Drops)
            {
                var dropChance = Math.Min(drop.BaseDropChance * dropMultiplier, drop.MaxDropChance);
                averageBoosts += recycledBoosts[drop.BoostIndex] * dropChance * filterMultipliers[drop.BoostIndex];
            }

            averageBoosts *= zone.NormalEnemyRatio;
            var score = averageBoosts * KillSpeedFactor(expectedHits);
            return new BoostFarmTarget
            {
                Zone = zone.Zone,
                IsItopod = false,
                Score = score,
                ExpectedHits = expectedHits,
                FightType = fightType,
                Summary = string.Format("{0} idle hit{1}, drops {2} boosts", expectedHits, expectedHits == 1 ? string.Empty : "s", zone.BoostSummary)
            };
        }

        private static bool CanFarmZone(int zone, int expectedHits, out int fightType)
        {
            fightType = expectedHits <= 1 ? 2 : 1;
            if (ZoneStatHelper.UserOverrides == null)
                return true;

            ZoneStats stats;
            if (!ZoneStatHelper.UserOverrides.TryGetValue(zone, out stats))
                return true;

            fightType = stats.FightType(Main.Character.totalAdvAttack(), Main.Character.totalAdvDefense());
            return fightType > 0;
        }

        private static double EffectiveAdventurePower()
        {
            try
            {
                var idleModifier = Math.Max(0.0001d, Main.Character.idleAttackPower());
                return Math.Max(0d, Main.Character.totalAdvAttack()) * idleModifier / DefaultIdleAttackModifier;
            }
            catch
            {
                return 0d;
            }
        }

        private static double KillSpeedFactor(int expectedHits)
        {
            var respawn = 4d;
            var attackSpeed = 1d;
            try
            {
                respawn = Math.Max(0d, Main.Character.adventureController.respawnTime());
                attackSpeed = Math.Max(0.1d, Main.Character.adventure.attackSpeed);
            }
            catch
            {
                // Defaults match the Almanac inputs.
            }

            return (respawn + attackSpeed) / (respawn + attackSpeed * Math.Max(1, expectedHits));
        }

        private static double PlayerDropMultiplier(bool rooted)
        {
            try
            {
                double multiplier = rooted ? Main.Character.lootFactorRooted() : Main.Character.lootFactor();
                if (Main.Character.arbitrary.lootcharm1Time.totalseconds > 0d)
                {
                    var charm = Main.Character.inventory.itemList.blueHeartComplete ? 2.2d : 2d;
                    multiplier *= rooted ? Math.Pow(charm, 1d / 3d) : charm;
                }

                return Math.Max(0d, multiplier);
            }
            catch
            {
                return 1d;
            }
        }

        private static double[] BuildRecycledBoostValues()
        {
            var values = new double[BoostValues.Length];
            var boostBonus = BoostBonus();
            var recycleChance = Math.Min(1d, Math.Max(0d, RecycleChance()));

            for (var boostIndex = 0; boostIndex < BoostValues.Length; boostIndex++)
            {
                var total = 0d;
                var probability = 1d;
                for (var index = boostIndex; index >= 0; index--)
                {
                    total += probability * BoostValues[index] * boostBonus;
                    probability *= recycleChance;
                }

                values[boostIndex] = total;
            }

            return values;
        }

        private static double BoostBonus()
        {
            try
            {
                return Math.Max(0d, Main.Character.allItemList.boostBonus());
            }
            catch
            {
                return 1d;
            }
        }

        private static double RecycleChance()
        {
            try
            {
                return Main.Character.totalRecycleBonus();
            }
            catch
            {
                return 0d;
            }
        }

        private static double[] BuildBoostFilterMultipliers()
        {
            var multipliers = new double[BoostValues.Length];
            for (var boostIndex = 0; boostIndex < BoostValues.Length; boostIndex++)
            {
                multipliers[boostIndex] = BoostFilterMultiplier(boostIndex);
            }

            return multipliers;
        }

        private static double BoostFilterMultiplier(int boostIndex)
        {
            var unfiltered = 0;
            if (BoostUnfiltered(1 + boostIndex)) unfiltered++;
            if (BoostUnfiltered(14 + boostIndex)) unfiltered++;
            if (BoostUnfiltered(27 + boostIndex)) unfiltered++;
            return unfiltered / 3d;
        }

        private static bool BoostUnfiltered(int itemId)
        {
            try
            {
                var character = Main.Character;
                var itemList = GetMember(character.inventory, "itemList");
                var itemFiltered = GetMember(itemList, "itemFiltered");
                if (GetBoolMember(character.arbitrary, "lootFilter", false) && GetBoolArrayValue(itemFiltered, itemId, false))
                    return false;

                if (!GetBoolMember(character.purchases, "hasFilter", false) || !GetBoolMember(character.settings, "filterOn", false))
                    return true;

                if (itemId < 14)
                    return !GetBoolMember(character.settings, "filterBoostAtk", false);

                if (itemId < 27)
                    return !GetBoolMember(character.settings, "filterBoostDef", false);

                return !GetBoolMember(character.settings, "filterBoostSpec", false);
            }
            catch
            {
                return true;
            }
        }

        private static object GetMember(object target, string name)
        {
            if (target == null)
                return null;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = target.GetType();
            var field = type.GetField(name, flags);
            if (field != null)
                return field.GetValue(target);

            var property = type.GetProperty(name, flags);
            return property == null ? null : property.GetValue(target, null);
        }

        private static bool GetBoolMember(object target, string name, bool fallback)
        {
            var value = GetMember(target, name);
            return value is bool ? (bool) value : fallback;
        }

        private static bool GetBoolArrayValue(object value, int index, bool fallback)
        {
            var array = value as Array;
            if (array == null || index < 0 || index >= array.Length)
                return fallback;

            var item = array.GetValue(index);
            return item is bool ? (bool) item : fallback;
        }

        private static int ItopodBoostIndex(int floor)
        {
            var index = 0;
            for (var i = 0; i < ItopodBoostFloors.Length; i++)
            {
                if (floor >= ItopodBoostFloors[i])
                    index = i;
            }

            return Math.Min(index, BoostValues.Length - 1);
        }

        private static BoostDrop Drop(int boostValue, double baseDropChance, double maxDropChance)
        {
            var index = Array.IndexOf(BoostValues, boostValue);
            if (index < 0)
                throw new ArgumentOutOfRangeException("boostValue");

            return new BoostDrop(index, baseDropChance, maxDropChance);
        }

        private static string SafeZoneName(int zone)
        {
            try
            {
                return Main.Character.adventureController.zoneName(zone);
            }
            catch
            {
                return string.Format("zone {0}", zone);
            }
        }

        private sealed class BoostFarmZone
        {
            internal readonly int Zone;
            internal readonly double NormalEnemyRatio;
            internal readonly double OneHitPower;
            internal readonly BoostDrop[] Drops;
            internal readonly string BoostSummary;

            internal BoostFarmZone(int zone, double normalEnemyRatio, double oneHitPower, params BoostDrop[] drops)
            {
                Zone = zone;
                NormalEnemyRatio = normalEnemyRatio;
                OneHitPower = oneHitPower;
                Drops = drops;
                BoostSummary = string.Join("/", drops.Select(x => BoostValues[x.BoostIndex].ToString()).Distinct().ToArray());
            }
        }

        private sealed class BoostDrop
        {
            internal readonly int BoostIndex;
            internal readonly double BaseDropChance;
            internal readonly double MaxDropChance;

            internal BoostDrop(int boostIndex, double baseDropChance, double maxDropChance)
            {
                BoostIndex = boostIndex;
                BaseDropChance = baseDropChance;
                MaxDropChance = maxDropChance;
            }
        }
    }
}
