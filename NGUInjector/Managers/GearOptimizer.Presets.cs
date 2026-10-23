using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUInjector.Managers
{
    internal static partial class GearOptimizer
    {
        internal static Dictionary<int, ih> CapturePresetGear(PresetSnapshot snapshot)
        {
            return CapturePresetGearCandidates(snapshot, BuildCandidates());
        }

        // Execution observation never converts inventory alternatives. A valid receipt
        // already proves its selected instances are equipped; replay still checks them.
        internal static Dictionary<int, ih> CaptureEquippedPresetGear(PresetSnapshot snapshot)
        {
            return CapturePresetGearCandidates(snapshot, Main.Character.inventory.GetConvertedEquips()
                .Where(x => x.id >= 40 && x.equipment != null && x.equipment.isEquipment())
                .Select(BuildCandidate));
        }

        private static Dictionary<int, ih> CapturePresetGearCandidates(PresetSnapshot snapshot, IEnumerable<GearCandidate> source)
        {
            var controller = Main.Controller;
            var inv = Main.Character.inventory;
            var slots = new List<int> { -1, -2, -3, -4, -5 };
            if (controller.weapon2Unlocked()) slots.Add(-6);
            slots.AddRange(Enumerable.Range(0, Math.Min(controller.accessorySpaces(), inv.accs.Count)).Select(i => i + 10000));
            snapshot.Slots = slots.ToArray();
            snapshot.Factors = slots.Select(slot => slot == -6 ? (double)controller.weapon2Factor() : 1).ToArray();
            // Reuse numeric capture, not legacy objective pruning or its unbounded exact search.
            var candidates = source.Where(x => slots.Contains(x.Item.slot)
                || x.Item.slot >= 0 && x.Item.slot < controller.curSpaces()).ToArray();
            var resourceSpecs = ResourceProjectionCapture.SpecTypes;
            var wrappers = candidates.ToDictionary(x => x.Item.slot, x => x.Item);
            snapshot.Items = candidates.Select(c => {
                var item = new PresetItem { Id = c.Id, SourceSlot = c.Item.slot, Name = c.Item.name,
                    Level = c.Item.level, Group = c.Slot == part.Head ? -1 : c.Slot == part.Chest ? -2
                        : c.Slot == part.Legs ? -3 : c.Slot == part.Boots ? -4 : c.Slot == part.Weapon ? -5 : 10000 };
                foreach (var stat in c.Stats)
                {
                    PresetStat key;
                    if (Enum.TryParse(stat.Key.ToString(), out key)) item.Stats[(int)key] = stat.Value;
                }
                // The game sums raw specs in slot order BEFORE converting each tier to a bonus.
                for (var i = 0; i < resourceSpecs.Length; i++) item.ResourceSpecs[i] = controller.equipSpecBonus(resourceSpecs[i], c.Item.equipment);
                item.LootSpecs[0] = controller.equipSpecBonus(specType.Looting, c.Item.equipment);
                item.LootSpecs[1] = controller.equipSpecBonus(specType.Looting2, c.Item.equipment);
                // Respawn is its own game spec, NOT move cooldown. Verified AdventureController.respawnTime.
                item.Stats[(int)PresetStat.Respawn] = controller.getBonusFactor(controller.equipSpecBonus(specType.Respawn, c.Item.equipment), specType.Respawn) * 100d;
                return item;
            }).ToArray();
            snapshot.Current = slots.Select(slot => snapshot.Items.FirstOrDefault(x => x.SourceSlot == slot)
                ?? new PresetItem { Id = 0, SourceSlot = slot, Group = LoadoutPresetPlanner.Group(slot), Name = "Empty" }).ToArray();
            return wrappers;
        }

        internal static string CheckPresetEquip(ih[] gear)
        {
            var basic = Main.Character.training.attackEnergy.Concat(Main.Character.training.defenseEnergy).Aggregate(0m, (sum, amount) => sum + Math.Max(0, amount));
            var capacity = ResourceProjectionCapture.Create(Main.Character, true, true).Project(ResourceProjectionCapture.GetBonuses(gear));
            if ((decimal)capacity < basic) return "Exact preset energy capacity is below retained basic training; no gear or resources were changed.";
            return CheckExactSwapCapacity(gear, basic, 0m);
        }
    }
}
