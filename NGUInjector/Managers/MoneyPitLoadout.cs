using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUInjector.Managers
{
    internal static class MoneyPitLoadout
    {
        private static readonly int[] Hearts = { 119, 129, 162, 171, 196, 212, 293, 297, 344, 390 };
        private static readonly int[] PendantsAndLooty = { 53, 76, 94, 142, 170, 229, 295, 388, 430, 504,
            67, 128, 169, 230, 296, 389, 431, 505 };

        // Native LootDrop.zoneNDrop sources: zone, minimum titan version, first/last item ID.
        // IDs alone are not progression ordered (notably Chocolate/Evilverse and Duck/Nether).
        private static readonly int[,] Sources = {
            {0,0,62,65}, {0,0,75,75}, {0,0,120,121},
            {1,0,40,46}, {1,0,77,77}, {2,0,47,52}, {2,0,135,135}, {2,0,432,432},
            {3,0,54,61}, {3,0,433,433}, {4,0,434,434}, {5,0,68,74}, {5,0,435,435},
            {6,0,78,84}, {7,0,85,91}, {7,0,436,436}, {9,0,95,101}, {9,0,437,437},
            {10,0,103,110}, {10,0,438,438}, {11,0,111,118}, {12,0,122,127}, {12,0,439,439},
            {13,0,130,134}, {13,0,440,440}, {14,0,136,140}, {14,0,149,149},
            {15,0,143,148}, {15,0,441,441}, {16,0,150,161}, {17,0,164,168}, {17,0,442,442},
            {18,0,173,178}, {18,0,443,443}, {19,1,184,189}, {19,2,190,190},
            {19,3,192,193}, {19,4,194,195}, {20,0,221,227}, {20,0,444,444},
            {21,0,213,220}, {21,0,445,445}, {22,0,231,236}, {22,0,446,446},
            {23,1,237,243}, {23,2,244,245}, {23,3,246,247}, {23,4,248,249},
            {24,0,251,257}, {24,0,447,447}, {25,0,258,264}, {25,0,448,448},
            {26,1,265,271}, {26,2,272,273}, {26,3,274,275}, {26,4,276,277},
            {27,0,301,307}, {27,0,449,449}, {28,0,308,314}, {28,0,450,450},
            {29,0,315,321}, {29,0,451,451}, {30,0,335,335}, {30,1,322,328},
            {30,1,342,342}, {30,2,329,330}, {30,3,331,332}, {30,4,333,334},
            {31,0,345,351}, {31,0,452,452}, {32,0,352,358}, {33,0,359,366},
            {34,1,373,380}, {34,2,381,382}, {34,3,383,384}, {34,4,385,386},
            {35,0,392,399}, {36,0,400,407}, {37,0,408,415}, {38,1,416,423},
            {38,2,424,425}, {38,3,426,427}, {38,4,428,429}, {39,0,453,460},
            {40,0,496,503}, {41,0,461,468}, {42,1,469,476}, {42,2,477,477},
            {42,3,478,478}, {42,4,479,479}, {43,0,507,514}
        };

        private static int Priority(ih item)
        {
            if (Hearts.Contains(item.id)) return 10000;
            if (PendantsAndLooty.Contains(item.id)) return 9000;
            for (var i = 0; i < Sources.GetLength(0); i++)
                if (item.id >= Sources[i, 2] && item.id <= Sources[i, 3]) return 1 + Sources[i, 0] * 10 + Sources[i, 1];
            return 0;
        }

        internal static ih[] Select(ih[] original, IEnumerable<ih> owned, int[] rewardedSlots)
        {
            var candidates = owned.Where(x => x.id != 0 && x.level < 100 && x.equipment.isEquipment())
                .OrderByDescending(Priority).ThenByDescending(x => x.level)
                .ThenBy(x => original.Any(y => y.slot == x.slot && ReferenceEquals(y.equipment, x.equipment)) ? 0 : 1)
                .ThenBy(x => x.id).ThenBy(x => x.slot).ToArray();
            var selected = new List<ih>();
            foreach (var slot in rewardedSlots.Distinct())
            {
                if (!original.Any(x => x.slot == slot)) continue;
                var type = slot <= -5 ? part.Weapon : slot == -1 ? part.Head : slot == -2 ? part.Chest
                    : slot == -3 ? part.Legs : slot == -4 ? part.Boots : part.Accessory;
                var item = candidates.FirstOrDefault(x => x.equipment.type == type && selected.All(y => y.id != x.id));
                if (item != null) selected.Add(new ih { equipment = item.equipment, id = item.id, level = item.level,
                    locked = item.locked, name = item.name, slot = slot });
            }
            return LoadoutManager.ComposeExactLoadout(original, selected);
        }
    }
}
