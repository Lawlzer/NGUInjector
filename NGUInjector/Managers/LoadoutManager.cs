using System;
using System.Collections.Generic;
using System.Linq;
using static NGUInjector.Main;

namespace NGUInjector.Managers
{
    internal static class LoadoutManager
    {
        // Equipment references are only meaningful in the save/rebirth that supplied them.
        internal sealed class SnapshotEpoch
        {
            private readonly Character character = Main.Character;
            private readonly object inventory = Main.Character.inventory;
            private readonly object diggers = Main.Character.diggers;
            private readonly object yggdrasil = Main.Character.yggdrasil;
            private readonly object stats = Main.Character.stats;
            private readonly long rebirth = Main.Character.stats.rebirthNumber;
            private readonly double elapsed = Main.Character.rebirthTime.totalseconds;
            private readonly double total = Main.Character.totalPlaytime.totalseconds;

            internal bool Matches()
            {
                var c = Main.Character;
                return ReferenceEquals(c, character) && c?.rebirthTime != null && c.totalPlaytime != null
                    && ReferenceEquals(c.inventory, inventory) && ReferenceEquals(c.diggers, diggers)
                    && ReferenceEquals(c.yggdrasil, yggdrasil) && ReferenceEquals(c.stats, stats)
                    && c.stats.rebirthNumber == rebirth && c.rebirthTime.totalseconds + .1d >= elapsed
                    && c.totalPlaytime.totalseconds + .1d >= total
                    && Math.Abs(c.totalPlaytime.totalseconds - c.rebirthTime.totalseconds - (total - elapsed)) <= 1d;
            }
        }

        internal static ih[] CaptureExactLoadout()
        {
            var inv = Main.Character.inventory;
            var result = new List<ih> {
                inv.head.GetInventoryHelper(-1), inv.chest.GetInventoryHelper(-2), inv.legs.GetInventoryHelper(-3),
                inv.boots.GetInventoryHelper(-4), inv.weapon.GetInventoryHelper(-5)
            };
            if (Controller.weapon2Unlocked()) result.Add(inv.weapon2.GetInventoryHelper(-6));
            result.AddRange(inv.accs.Take(Math.Max(0, Controller.accessorySpaces()))
                .Select((item, index) => item.GetInventoryHelper(10000 + index)));
            return result.ToArray();
        }

        internal static ih[] ComposeExactLoadout(ih[] original, IEnumerable<ih> selectedDestinations)
        {
            if (original == null || selectedDestinations == null) throw new ArgumentNullException();
            var selected = selectedDestinations.ToArray();
            if (original.Any(x => x?.equipment == null) || original.Select(x => x.slot).Distinct().Count() != original.Length
                || selected.Any(x => x?.equipment == null || !original.Any(y => y.slot == x.slot))
                || selected.Select(x => x.slot).Distinct().Count() != selected.Length
                || selected.Where(x => x.id != 0).Select(x => x.equipment).Distinct().Count() != selected.Count(x => x.id != 0)
                || selected.Where(x => x.id != 0).Select(x => x.id).Distinct().Count() != selected.Count(x => x.id != 0))
                throw new InvalidOperationException("Exact loadout contains missing, duplicate or unavailable destinations/items.");
            var ids = new HashSet<int>(selected.Where(x => x.id != 0).Select(x => x.id));
            var instances = new HashSet<Equipment>(selected.Where(x => x.id != 0).Select(x => x.equipment));
            var retainedIds = new HashSet<int>();
            return original.Select(item => {
                var replacement = selected.FirstOrDefault(x => x.slot == item.slot);
                var keep = replacement ?? (item.id == 0 || !ids.Contains(item.id) && !instances.Contains(item.equipment)
                    && retainedIds.Add(item.id) ? item : new ih { equipment = new Equipment(), id = 0 });
                return new ih { slot = item.slot, equipment = keep.equipment, id = keep.id,
                    level = keep.level, locked = keep.locked, name = keep.name };
            }).ToArray();
        }

        internal static GearMutationOutcome ApplyExactLoadoutObserved(ih[] wanted,
            GearRecallFootprint recallFootprint = GearRecallFootprint.None, Func<bool> authorized = null)
        {
            return GearOptimizer.EquipGearObserved(wanted?.Where(x => x != null && x.id != 0).ToArray(),
                recallFootprint, true, wanted, authorized);
        }

        internal static bool ExactLoadoutMatches(ih[] wanted)
        {
            if (ValidateExactLoadout(wanted) != null) return false;
            var actual = CaptureExactLoadout();
            return wanted.All(x => actual.Any(y => x.slot == y.slot && (x.id == 0 ? y.id == 0
                : y.id == x.id && ReferenceEquals(x.equipment, y.equipment))));
        }

        internal static string ValidateExactLoadout(ih[] wanted)
        {
            var slots = CaptureExactLoadout().Select(x => x.slot).ToArray();
            if (wanted == null || wanted.Length != slots.Length || wanted.Any(x => x?.equipment == null || x.id != x.equipment.id)
                || wanted.Select(x => x.slot).Distinct().Count() != slots.Length || wanted.Any(x => !slots.Contains(x.slot)))
                return "Exact loadout must describe every currently unlocked equipment slot, including empty slots.";
            return null;
        }

    }
}
