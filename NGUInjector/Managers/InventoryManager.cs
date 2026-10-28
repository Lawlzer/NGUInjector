using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static NGUInjector.Main;

namespace NGUInjector.Managers
{

    public class FixedSizedQueue
    {
        private Queue<decimal> queue = new Queue<decimal>();

        public int Size { get; private set; }

        public FixedSizedQueue(int size)
        {
            Size = size;
        }

        public void Enqueue(decimal obj)
        {
            queue.Enqueue(obj);

            while (queue.Count > Size)
            {
                queue.Dequeue();
            }
        }

        public void Reset()
        {
            queue.Clear();
        }

        public decimal Avg()
        {
            try
            {
                return queue.Average(x => x);
            }
            catch (Exception e)
            {
                Log(e.Message);
                return 0;
            }
        }
    }

    public class Cube
    {
        internal float Power { get; set; }
        internal float Toughness { get; set; }
        protected bool Equals(Cube other)
        {
            return Power.Equals(other.Power) && Toughness.Equals(other.Toughness);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Cube) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Power.GetHashCode() * 397) ^ Toughness.GetHashCode();
            }
        }
    }

    internal class InventoryManager
    {
        private readonly Character _character;
        private readonly InventoryController _controller;

        private readonly int[] _pendants = { 53, 76, 94, 142, 170, 229, 295, 388, 430, 504 };
        private readonly int[] _lootys = { 67, 128, 169, 230, 296, 389, 431, 505 };
        private readonly int[] _convertibles;
        private readonly int[] _wandoos = {66, 169};
        private readonly int[] _guffs = {198, 200, 199, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 228, 211, 250, 291, 289, 290, 298, 299, 300};
        private readonly int[] _mergeBlacklist = { 367, 368, 369, 370, 371, 372 };
        private BoostsNeeded _previousBoostsNeeded = null;
        private Cube _lastCube = null;
        private readonly FixedSizedQueue _invBoostAvg = new FixedSizedQueue(60);
        private readonly FixedSizedQueue _cubeBoostAvg = new FixedSizedQueue(60);
        private bool _inventoryScanRunning;

        private static bool RunInventoryAction(ItemActionKind kind, ih target)
        {
            var outcome = Main.reference?.RunInventoryLeaf(kind, target) ?? InventoryLeafOutcome.NoWork;
            if (outcome == InventoryLeafOutcome.RescanRequired) throw new InventoryRescanRequiredException();
            return outcome == InventoryLeafOutcome.Applied;
        }


        //Wandoos 98, Giant Seed, Wandoos XL, Lonely Flubber, Wanderer's Cane, Guffs, Lemmi
        private readonly int[] _filterExcludes = { 66, 92, 163, 120, 154, 195, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287  };
        public InventoryManager()
        {
            _character = Main.Character;
            _controller = Controller;
            var temp = _pendants.Concat(_lootys).ToList();
            //Wanderer's Cane
            temp.Add(154);
            //Lonely Flubber
            temp.Add(120);
            //A Giant Seed
            temp.Add(92);
            _convertibles = temp.ToArray();
        }

        internal void Reset()
        {
            _invBoostAvg.Reset();
            _cubeBoostAvg.Reset();
        }

        internal ih[] GetBoostSlots(ih[] ci)
        {
            var result = new List<ih>();
            //First, find items in our priority list
            foreach (var id in Settings.PriorityBoosts)
            {
                if (Settings.BoostBlacklist.Contains(id))
                    continue;
                
                var f = FindItemSlot(ci, id);
                if (f != null)
                    result.Add(f);
            }

            //Next, get equipped items that aren't in our priority list and aren't blacklisted
            var equipped = Main.Character.inventory.GetConvertedEquips()
                .Where(x => !Settings.PriorityBoosts.Contains(x.id) && !Settings.BoostBlacklist.Contains(x.id));
            result = result.Concat(equipped).ToList();

            //Finally, find locked items in inventory that aren't blacklisted
            var invItems = ci.Where(x => x.locked && x.equipment.isEquipment() && !Settings.BoostBlacklist.Contains(x.id) && !Settings.PriorityBoosts.Contains(x.id));
            result = result.Concat(invItems).ToList();

            //Make sure we filter out non-equips again, just in case one snuck into priorityboosts
            return result.Where(x => x.equipment.isEquipment()).Where(x => x.equipment.GetNeededBoosts().Total() > 0).ToArray();
        }

        internal string ManageCustomAutoBoost()
        {
            if (_controller.midDrag)
                return "Auto boost skipped: inventory drag active.";
            if (_inventoryScanRunning)
                return "Auto boost skipped: inventory scan active.";

            _inventoryScanRunning = true;
            try
            {
                var converted = _character.inventory.GetConvertedInventory().ToArray();
                var lockedBoost = ManageLockedBoostPriority(converted);
                if (!string.IsNullOrWhiteSpace(lockedBoost))
                    converted = _character.inventory.GetConvertedInventory().ToArray();

                MergeBoosts(converted);
                converted = _character.inventory.GetConvertedInventory().ToArray();

                var boostSlots = GetCustomBoostSlots(converted);
                foreach (var item in boostSlots)
                {
                    if (!_character.inventory.HasBoosts())
                        break;

                    BoostNativeItem(item);
                }

                if (_character.inventory.HasBoosts())
                {
                    BoostInfinityCube();
                }

                if (!string.IsNullOrWhiteSpace(lockedBoost))
                    return lockedBoost;

                return ManageCustomBoostConversion();
            }
            catch (InventoryRescanRequiredException)
            {
                return "Auto boost skipped: inventory rescan required.";
            }
            finally { _inventoryScanRunning = false; }
        }

        private ih[] GetCustomBoostSlots(ih[] ci)
        {
            var equipped = _character.inventory.GetConvertedEquips()
                .Where(IsCustomBoostTarget)
                .ToArray();
            var mergeSlotCount = _controller.totalInvMergeSlots();
            var mergeSlots = ci
                .Where(x => (x.locked || (x.slot >= 0 && x.slot < mergeSlotCount)) && IsCustomBoostTarget(x))
                .ToArray();

            var targets = new List<ih>();
            targets.AddRange(OrderCustomEquipped(equipped.Where(x => !IsHeart(x))));
            targets.AddRange(mergeSlots.Where(x => !IsHeart(x)).OrderBy(x => x.slot));
            targets.AddRange(OrderCustomEquipped(equipped.Where(IsHeart)));
            targets.AddRange(mergeSlots.Where(IsHeart).OrderBy(x => x.slot));
            var priorities = Settings.PriorityBoosts ?? new int[0];
            // OrderBy is stable: copies of an ID and all unlisted targets keep the default order.
            return targets.Where(NeedsAnyBoost).OrderBy(x =>
            {
                var rank = Array.IndexOf(priorities, x.id);
                return rank < 0 ? int.MaxValue : rank;
            }).ToArray();
        }

        private static bool IsCustomBoostTarget(ih item)
        {
            return item != null
                   && item.equipment != null
                   && item.equipment.isEquipment()
                   && !Settings.BoostBlacklist.Contains(item.id);
        }

        private static IEnumerable<ih> OrderCustomEquipped(IEnumerable<ih> items)
        {
            return items.OrderBy(CustomEquipBoostOrder).ThenBy(x => x.slot);
        }

        private static int CustomEquipBoostOrder(ih item)
        {
            if (item.slot == -5)
                return 0;

            if (item.slot == -6)
                return 1;

            if (item.slot < 0)
                return 2;

            return 3;
        }

        private static bool IsHeart(ih item)
        {
            try
            {
                var name = Main.Character.itemInfo.itemName[item.id];
                return name != null && name.IndexOf("heart", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return item.name != null && item.name.IndexOf("heart", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static bool NeedsAnyBoost(ih item)
        {
            var needed = item.equipment.GetNeededBoosts();
            return needed.Power > 0 || needed.Toughness > 0 || needed.Special > 0;
        }

        private string ManageCustomBoostConversion()
        {
            if (_character.challenges.levelChallenge10k.curCompletions <
                _character.allChallenges.level100Challenge.maxCompletions)
                return "Auto boost: boost transform locked.";

            var boostSlots = GetCustomBoostSlots(_character.inventory.GetConvertedInventory().ToArray());
            foreach (var item in boostSlots)
            {
                var needed = item.equipment.GetNeededBoosts();
                if (needed.Power > 0)
                {
                    Main.reference?.RequestBoostTransform(1);
                    return $"Auto boost: transforming Power for {SanitizeName(item.name)}.";
                }

                if (needed.Toughness > 0)
                {
                    Main.reference?.RequestBoostTransform(2);
                    return $"Auto boost: transforming Toughness for {SanitizeName(item.name)}.";
                }

                if (needed.Special > 0)
                {
                    Main.reference?.RequestBoostTransform(3);
                    return $"Auto boost: transforming Special for {SanitizeName(item.name)}.";
                }
            }

            return SelectCustomCubeTransform();
        }

        private string ManageLockedBoostPriority(ih[] ci)
        {
            var target = ci
                .Where(IsLockedUnmaxedBoost)
                .OrderByDescending(x => x.level)
                .ThenBy(x => x.slot)
                .FirstOrDefault();
            if (target == null)
                return null;

            if (ci.Count(x => x.id == target.id) > 1)
            {
                MergeNativeItem(target);
                _controller.updateInventory();
            }

            var transform = SelectTransformForBoost(target);
            return $"Auto boost: locked boost priority {SanitizeName(target.name)} level {target.level}/100. {transform}";
        }

        private static bool IsLockedUnmaxedBoost(ih item)
        {
            return item != null
                   && item.id > 0
                   && item.id <= 39
                   && item.locked
                   && item.level < 100;
        }

        private string SelectTransformForBoost(ih item)
        {
            var name = item.name ?? string.Empty;
            if (name.IndexOf("power", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Main.reference?.RequestBoostTransform(1);
                return "Transforming Power boosts for it.";
            }

            if (name.IndexOf("tough", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Main.reference?.RequestBoostTransform(2);
                return "Transforming Toughness boosts for it.";
            }

            if (name.IndexOf("special", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Main.reference?.RequestBoostTransform(3);
                return "Transforming Special boosts for it.";
            }

            return "Boost type unknown; leaving current transform unchanged.";
        }

        private string SelectCustomCubeTransform()
        {
            var power = _character.inventory.cubePower;
            var toughness = _character.inventory.cubeToughness;
            var ratio = Math.Max(1, Settings.CustomAutoBoostCubePowerRatio);
            if (power < toughness * ratio)
            {
                Main.reference?.RequestBoostTransform(1);
                return $"Auto boost: transforming Power for cube {ratio}:1.";
            }

            Main.reference?.RequestBoostTransform(2);
            return $"Auto boost: transforming Toughness for cube {ratio}:1.";
        }

        internal void BoostInventory(ih[] boostSlots)
        {
            foreach (var item in boostSlots)
            {
                if (!_character.inventory.HasBoosts())
                    break;
                BoostNativeItem(item);
            }
        }

        private static ih FindItemSlot(IEnumerable<ih> ci, int id)
        {
            var inv = Main.Character.inventory;
            if (inv.head.id == id)
            {
                return inv.head.GetInventoryHelper(-1);
            }

            if (inv.chest.id == id)
            {
                return inv.chest.GetInventoryHelper(-2);
            }

            if (inv.legs.id == id)
            {
                return inv.legs.GetInventoryHelper(-3);
            }

            if (inv.boots.id == id)
            {
                return inv.boots.GetInventoryHelper(-4);
            }

            if (inv.weapon.id == id)
            {
                return inv.weapon.GetInventoryHelper(-5);
            }

            if (Controller.weapon2Unlocked())
            {
                if (inv.weapon2.id == id)
                {
                    inv.weapon2.GetInventoryHelper(-6);
                }
            }

            for (var i = 0; i < inv.accs.Count; i++)
            {
                if (inv.accs[i].id == id)
                {
                    return inv.accs[i].GetInventoryHelper(i + 10000);
                }
            }

            var items = ci.Where(x => x.id == id).ToArray();
            if (items.Length != 0) return items.MaxItem();

            return null;
        }

        internal void BoostInfinityCube()
        {
            if (!_character.inventory.HasBoosts())
                return;
            for (var i = 0; i < 40 * _character.inventory.inventory.Count && _character.inventory.HasBoosts(); i++)
                if (!RunInventoryAction(ItemActionKind.Cube, null)) break;
        }

        internal void MergeEquipped(ih[] ci)
        {
            foreach (var item in _character.inventory.GetConvertedEquips().ToArray())
                MergeNativeItem(item);
        }

        internal void MergeBoosts(ih[] ci)
        {
            var grouped = ci.Where(x =>
                x.id <= 39 && !_character.inventory.inventory[x.slot].removable &&
                !_character.inventory.itemList.itemMaxxed[x.id]);

            foreach (var target in grouped)
            {
                if (ci.Count(x => x.id == target.id) <= 1) continue;
                Log($"Merging {target.name} in slot {target.slot}");
                MergeNativeItem(target);
            }
        }

        private void MergeNativeItem(ih target)
        {
            for (var i = 0; i < _character.inventory.inventory.Count; i++)
                if (!RunInventoryAction(ItemActionKind.Merge, target)) break;
        }

        private void BoostNativeItem(ih target)
        {
            for (var i = 0; i < 40 * _character.inventory.inventory.Count && NeedsAnyBoost(target); i++)
                if (!RunInventoryAction(ItemActionKind.Boost, target)) break;
        }

        private string SanitizeName(string name)
        {
            if (name.Contains("\n"))
            {
                name = name.Split('\n').Last();
            }

            return name;
        }

        internal void ManageQuestItems(ih[] ci)
        {
            //Merge quest items first
            var toMerge = ci.Where(x =>
                x.id >= 278 && x.id <= 287 && !_character.inventory.inventory[x.slot].removable &&
                !_character.inventory.itemList.itemMaxxed[x.id]);

            foreach (var target in toMerge)
            {
                if (ci.Count(x => x.id == target.id) <= 1) continue;
                Log($"Merging {SanitizeName(target.name)} in slot {target.slot}");
                MergeNativeItem(target);
            }

            //Consume quest items that dont need to be merged
            var questItems = ci.Where(x =>
                x.id >= 278 && x.id <= 287 && _character.inventory.inventory[x.slot].removable).ToArray();

            if (questItems.Length > 0)
                Log($"Turning in {questItems.Length} quest items");
            foreach (var target in questItems)
            {
                RunInventoryAction(ItemActionKind.Consume, target);
            }
        }

        internal void MergeInventory(ih[] ci)
        {
            var grouped =
                ci.Where(x => x.id >= 40 && x.level < 100 && !_mergeBlacklist.Contains(x.id) && !_guffs.Contains(x.id) && (x.id < 278 || x.id > 287)).GroupBy(x => x.id).Where(x => x.Count() > 1);

            foreach (var item in grouped)
            {
                if (item.All(x => x.locked))
                    continue;

                var target = item.MaxItem();

                Log($"Merging {SanitizeName(target.name)} in slot {target.slot}");
                MergeNativeItem(target);
            }
        }

        internal void MergeGuffs(ih[] ci)
        {
            for (var id = 0; id < _character.inventory.macguffins.Count; ++id)
            {
                var guffId = _character.inventory.macguffins[id].id;
                if (ci.Any(x => x.id == guffId))
                    MergeNativeItem(_character.inventory.macguffins[id].GetInventoryHelper(_controller.globalMacguffinID(id)));
            }

            var invGuffs = ci.Where(x => _guffs.Contains(x.id)).GroupBy(x => x.id).Where(x => x.Count() > 1);
            foreach (var guff in invGuffs)
            {
                var target = guff.MaxItem();
                MergeNativeItem(target);
            }
        }

        internal void ManageConvertibles(ih[] ci)
        {
            var grouped = ci.Where(x => _convertibles.Contains(x.id));
            foreach (var item in grouped)
            {
                if (item.level != 100) continue;
                var temp = _character.inventory.inventory[item.slot];
                if (!temp.removable) continue;
                RunInventoryAction(ItemActionKind.Consume, item);
            }
        }

        internal void ShowBoostProgress(ih[] boostSlots)
        {
            var needed = new BoostsNeeded();
            var cube = new Cube
            {
                Power = _character.inventory.cubePower,
                Toughness = _character.inventory.cubeToughness
            };

            foreach (var item in boostSlots)
            {
                needed.Add(item.equipment.GetNeededBoosts());
            }

            var current = needed.Power + needed.Toughness + needed.Special;

            if (current > 0)
            {
                if (_previousBoostsNeeded == null)
                {
                    Log($"Boosts Needed to Green: {needed.Power} Power, {needed.Toughness} Toughness, {needed.Special} Special");
                    _previousBoostsNeeded = needed;
                }
                else
                {
                    var old = _previousBoostsNeeded.Power + _previousBoostsNeeded.Toughness +
                              _previousBoostsNeeded.Special;

                    var diff = current - old;

                    if (diff == 0) return;

                    //If diff is > 0, then we either added another item to boost or we levelled something. Don't add the diff to average
                    if (diff <= 0)
                    {
                        _invBoostAvg.Enqueue(diff * -1);
                    }

                    Log($"Boosts Needed to Green: {needed.Power} Power, {needed.Toughness} Toughness, {needed.Special} Special");
                    var average = _invBoostAvg.Avg();
                    if (average > 0)
                    {
                        var eta = current / average;
                        Log($"Last Minute: {diff}. Average Per Minute: {average:0}. ETA: {eta:0} minutes.");
                    }
                    else
                    {
                        Log($"Last Minute: {diff}.");
                    }

                    _previousBoostsNeeded = needed;
                }
            }

            if (_lastCube == null)
            {
                _lastCube = cube;
            }
            else
            {
                if (!_lastCube.Equals(cube))
                {
                    var output = $"Cube Progress:";
                    var toughnessDiff = cube.Toughness - _lastCube.Toughness;
                    var powerDiff = cube.Power - _lastCube.Power;

                    output = toughnessDiff > 0 ? $"{output} {toughnessDiff} Toughness." : output;
                    output = powerDiff > 0 ? $"{output} {powerDiff} Power." : output;

                    _cubeBoostAvg.Enqueue((decimal)(toughnessDiff + powerDiff));
                    output = $"{output} Average Per Minute: {_cubeBoostAvg.Avg():0}";
                    Log(output);
                    Log($"Cube Power: {cube.Power} ({_character.inventoryController.cubePowerSoftcap()} softcap). Cube Toughness: {cube.Toughness} ({_character.inventoryController.cubeToughnessSoftcap()} softcap)");
                }

                _lastCube = cube;
            }
        }

        internal void ManageBoostConversion(ih[] boostSlots)
        {
            if (_character.challenges.levelChallenge10k.curCompletions <
                _character.allChallenges.level100Challenge.maxCompletions)
                return;

            if (!Settings.AutoConvertBoosts)
                return;

            var converted = _character.inventory.GetConvertedInventory();
            //If we have a boost locked, we want to stay on that until its maxxed
            var lockedBoosts = converted.Where(x => x.id < 40 && x.locked).ToArray();
            if (lockedBoosts.Any())
            {
                foreach (var locked in lockedBoosts)
                {
                    //Unlock level 100 boosts
                    if (locked.level == 100)
                    {
                        Main.reference?.RequestUnlockBoost(locked);
                        continue;
                    }

                    if (locked.id <= 13)
                    {
                        Main.reference?.RequestBoostTransform(1);
                    }else if (locked.id <= 26)
                    {
                        Main.reference?.RequestBoostTransform(2);
                    }else if (locked.id <= 39)
                    {
                        Main.reference?.RequestBoostTransform(3);
                    }
                }

                return;
            }

            var needed = new BoostsNeeded();

            foreach (var item in boostSlots)
            {
                needed.Add(item.equipment.GetNeededBoosts());
            }

            if (needed.Power > 0)
            {
                Main.reference?.RequestBoostTransform(1);
                return;
            }

            if (needed.Toughness > 0)
            {
                Main.reference?.RequestBoostTransform(2);
                return;
            }

            if (needed.Special > 0)
            {
                Main.reference?.RequestBoostTransform(3);
                return;
            }

            var cube = new Cube
            {
                Power = _character.inventory.cubePower,
                Toughness = _character.inventory.cubeToughness
            };

            if (Settings.CubePriority > 0)
            {
                if (Settings.CubePriority == 1)
                {
                    if (cube.Power > cube.Toughness)
                    {
                        Main.reference?.RequestBoostTransform(2);
                    }
                    else if (cube.Toughness > cube.Power)
                    {
                        Main.reference?.RequestBoostTransform(1);
                    }
                    else
                    {
                        Main.reference?.RequestBoostTransform(1);
                    }
                }else if (Settings.CubePriority == 2)
                {
                    Main.reference?.RequestBoostTransform(1);
                }
                else
                {
                    Main.reference?.RequestBoostTransform(2);
                }
                
                return;
            }

            Main.reference?.RequestBoostTransform(0);
        }

        #region Filtering
        internal void EnsureFiltered(ih[] ci)
        {
            if (!Main.Character.arbitrary.lootFilter)
                return;

            var targets = ci.Where(x => x.level == 100);
            foreach (var target in targets)
            {
                FilterItem(target.id);
            }

            FilterEquip(_character.inventory.head);
            FilterEquip(_character.inventory.boots);
            FilterEquip(_character.inventory.chest);
            FilterEquip(_character.inventory.legs);
            FilterEquip(_character.inventory.weapon);
            if (_character.inventoryController.weapon2Unlocked())
                FilterEquip(_character.inventory.weapon2);

            foreach (var acc in _character.inventory.accs)
            {
                FilterEquip(acc);
            }
        }

        void FilterItem(int id)
        {
            if (_pendants.Contains(id) || _lootys.Contains(id) || _wandoos.Contains(id) ||
                _filterExcludes.Contains(id) || _guffs.Contains(id) || id < 40 || _mergeBlacklist.Contains(id))
                return;

            //Dont filter quest items
            if (id >= 278 && id <= 287)
                return;

            Main.reference?.RequestItemFilter(id);
        }

        void FilterEquip(Equipment e)
        {
            if (e.level == 100)
            {
                FilterItem(e.id);
            }
        }
        #endregion

    }

    public class ih
    {
        internal int slot { get; set; }
        internal string name { get; set; }
        internal int level { get; set; }
        internal bool locked { get; set; }
        internal int id { get; set; }
        internal Equipment equipment { get; set; }
    }

    public class BoostsNeeded
    {
        internal decimal Power { get; set; }
        internal decimal Toughness { get; set; }
        internal decimal Special { get; set; }

        public BoostsNeeded()
        {
            Power = 0;
            Toughness = 0;
            Special = 0;
        }

        public void Add(BoostsNeeded other)
        {
            Power += other.Power;
            Toughness += other.Toughness;
            Special += other.Special;
        }

        public decimal Total()
        {
            return Power + Toughness + Special;
        }
    }
}
