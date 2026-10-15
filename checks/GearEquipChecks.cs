using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class GearEquipChecks
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string NeedsEmpty = "Safe gear swap requires an empty inventory slot; loadout was not equipped.";
    private static Type _optimizer, _item, _equipment, _inventory, _controller;
    private static int _assertions;

    // Only the pure production planner and the game's data-only Inventory swaps run here.
    // Never execute successful InventoryController swaps: their UI calls require native Unity.
    internal static int Run(Assembly assembly)
    {
        _assertions = 0;
        _optimizer = assembly.GetType("NGUInjector.Managers.GearOptimizer", true);
        _item = assembly.GetType("NGUInjector.Managers.ih", true);
        _equipment = _item.GetProperty("equipment", Instance).PropertyType;
        _inventory = _equipment.Assembly.GetType("Inventory", true);
        _controller = _equipment.Assembly.GetType("InventoryController", true);
        CheckCopies();
        CheckCycleOrdering();
        CheckPermutations();
        CheckPreflight();
        Console.WriteLine("Gear equip checks passed: " + _assertions + " assertions.");
        return _assertions;
    }

    private static void CheckCopies()
    {
        var types = new[] {"Head", "Chest", "Legs", "Boots", "Weapon", "Weapon", "Accessory"};
        var destinations = new[] {-1, -2, -3, -4, -5, -6, 10000};
        for (var i = 0; i < types.Length; i++)
        {
            var inv = Inventory(2, 2);
            var oldCopy = Equipment(100 + i, types[i]);
            var selected = Equipment(100 + i, types[i]);
            Put(inv, destinations[i], oldCopy);
            Put(inv, 0, selected);
            object loadout = null;
            if (i == 0)
            {
                var loadouts = (IList) Get(inv, "loadouts");
                loadout = FormatterServices.GetUninitializedObject(loadouts.GetType().GetGenericArguments()[0]);
                Set(loadout, "head", -1);
                Set(loadout, "accessories", new List<int>());
                loadouts.Add(loadout);
            }
            var controller = FormatterServices.GetUninitializedObject(_controller);
            Equal(true, (bool) _controller.GetMethod("mergeable", Instance).Invoke(controller, new[] {oldCopy, selected}),
                types[i] + " original controller path would merge copies");
            var gear = Gear(Item(selected, destinations[i]));
            var plan = Plan(inv, gear, true, 2, 2, true, null);
            Equal(2, ((IList) plan[7]).Count, "same-ID replacement stages before equip");
            Execute(inv, plan);
            Same(selected, At(inv, destinations[i]), "selected same-ID instance equipped");
            Same(oldCopy, At(inv, 1), "displaced same-ID copy survives in inventory");
            Equal(17, (int) Get(oldCopy, "level"), "displaced copy was not merged");
            Equal(17, (int) Get(selected, "level"), "selected copy was not merged");
            if (loadout != null) Equal(1, (int) Get(loadout, "head"), "game loadout index follows the displaced instance");

            inv = Inventory(1, 2);
            Put(inv, destinations[i], oldCopy);
            Put(inv, 0, selected);
            Plan(inv, gear, true, 2, 1, true, NeedsEmpty);
        }

        // A same-ID accessory can block a different destination via alreadyEquipped.
        var inventory = Inventory(2, 2);
        var first = Equipment(200, "Accessory");
        var replacement = Equipment(200, "Accessory");
        var other = Equipment(201, "Accessory");
        Put(inventory, 10000, first);
        Put(inventory, 10001, other);
        Put(inventory, 0, replacement);
        var result = Plan(inventory, Gear(Item(replacement, 10001), Item(other, 10000)), true, 2, 2, true, null);
        Execute(inventory, result);
        Same(first, At(inventory, 1), "off-destination same-ID conflict survives");
        Same(replacement, At(inventory, 10001), "replacement reaches original accessory destination");
        Same(other, At(inventory, 10000), "other selected accessory follows its simulated position");
    }

    private static void CheckCycleOrdering()
    {
        foreach (var restore in new[] {false, true})
        {
            var inv = Inventory(1, 4);
            var items = new object[4];
            for (var i = 0; i < items.Length; i++)
            {
                items[i] = Equipment(250 + i, "Accessory");
                Put(inv, 10000 + i, items[i]);
            }
            // Input interleaves two saved-slot cycles; sequential destinations form one long cycle.
            var gear = Gear(Item(items[1], 10000), Item(items[3], 10002),
                Item(items[0], 10001), Item(items[2], 10003));
            var plan = Plan(inv, gear, true, 4, 1, restore, null);
            var swaps = (IList) plan[7];
            Equal(restore ? 6 : 5, swaps.Count, "cycles reuse one empty slot without extra staging");
            Same(items[0], Get(swaps[2], "InventoryItem"), "displaced selected item moves before next equipped input");
            Execute(inv, plan);
            var expected = restore ? new[] {1, 0, 3, 2} : new[] {1, 3, 0, 2};
            for (var i = 0; i < expected.Length; i++)
                Same(items[expected[i]], At(inv, 10000 + i), "cycle ordering preserves destination semantics");
            Equal(0, (int) Get(At(inv, 0), "id"), "completed cycles return the staging slot empty");
        }
    }

    private static void CheckPermutations()
    {
        foreach (var restore in new[] {false, true})
        {
            var inv = Inventory(1, 0);
            var main = Equipment(300, "Weapon");
            var offhand = Equipment(301, "Weapon");
            Put(inv, -5, main);
            Put(inv, -6, offhand);
            var gear = Gear(Item(offhand, -5), Item(main, -6));
            var plan = Plan(inv, gear, true, 0, 1, restore, null);
            Equal(3, ((IList) plan[7]).Count, "weapon permutation uses inventory staging");
            Execute(inv, plan);
            Same(offhand, At(inv, -5), "offhand becomes mainhand");
            Same(main, At(inv, -6), "mainhand becomes offhand");
            Equal(0, (int) Get(At(inv, 0), "id"), "weapon permutation returns staging slot empty");

            inv = Inventory(1, 0);
            Put(inv, -5, main);
            Put(inv, -6, offhand);
            Put(inv, 0, Equipment(999, "Head"));
            Plan(inv, gear, true, 0, 1, restore, NeedsEmpty);
        }

        // Every ordering of four equipped accessories, in both destination modes.
        foreach (var restore in new[] {false, true})
        for (var a = 0; a < 4; a++)
        for (var b = 0; b < 4; b++)
        for (var c = 0; c < 4; c++)
        for (var d = 0; d < 4; d++)
        {
            var order = new[] {a, b, c, d};
            if (new HashSet<int>(order).Count != 4) continue;
            var inv = Inventory(1, 4);
            var items = new object[4];
            for (var i = 0; i < 4; i++)
            {
                items[i] = Equipment(400 + i, "Accessory");
                Put(inv, 10000 + i, items[i]);
            }
            var wrappers = new object[4];
            for (var i = 0; i < 4; i++) wrappers[i] = Item(items[order[i]], 10000 + i);
            // Snapshot array order must not override saved slots.
            if (restore) Array.Reverse(wrappers);
            var gear = Gear(wrappers);
            var plan = Plan(inv, gear, true, 4, 1, restore, null);
            Execute(inv, plan);
            for (var i = 0; i < 4; i++) Same(items[order[i]], At(inv, 10000 + i), "accessory permutation identity");
            Equal(0, (int) Get(At(inv, 0), "id"), "accessory permutation reuses one empty slot");

            inv = Inventory(1, 4);
            for (var i = 0; i < 4; i++) Put(inv, 10000 + i, items[i]);
            Put(inv, 0, Equipment(999, "Head"));
            Plan(inv, gear, true, 4, 1, restore, a == 0 && b == 1 && c == 2 && d == 3 ? null : NeedsEmpty);
        }
    }

    private static void CheckPreflight()
    {
        var inv = Inventory(3, 2);
        var head = Equipment(500, "Head");
        var copy = Equipment(500, "Head");
        Put(inv, -1, head);
        Put(inv, 0, copy);
        Plan(inv, Gear(Item(head, -1), Item(head, -1)), true, 2, 3, false,
            "Loadout contains missing or duplicate equipment instances.");
        Plan(inv, Gear(Item(head, -1), Item(copy, -1)), true, 2, 3, false,
            "Loadout contains empty equipment or duplicate item IDs.");
        var stale = Item(copy, -1);
        _item.GetProperty("id", Instance).SetValue(stale, 999);
        Plan(inv, Gear(Item(head, -1), stale), true, 2, 3, false,
            "Loadout contains empty equipment or duplicate item IDs.");
        Plan(inv, Gear(Item(Equipment(0, "Head"), -1)), true, 2, 3, false,
            "Loadout contains empty equipment or duplicate item IDs.");
        Plan(inv, Gear(Item(head, 0)), true, 2, 3, true,
            "Original equipment slot does not match the selected item's type.");
        Plan(inv, Gear(Item(head, -2)), true, 2, 3, true,
            "Original equipment slot does not match the selected item's type.");

        var weapon = Equipment(501, "Weapon");
        Put(inv, 1, weapon);
        Plan(inv, Gear(Item(weapon, -6)), false, 2, 3, true,
            "Loadout requires duplicate or unavailable equipment slots.");
        var accessory = Equipment(502, "Accessory");
        Put(inv, 2, accessory);
        Plan(inv, Gear(Item(accessory, 10001)), true, 1, 3, true,
            "Loadout requires duplicate or unavailable equipment slots.");
        Plan(inv, Gear(Item(accessory, 10002)), true, 3, 3, true,
            "Loadout requires duplicate or unavailable equipment slots.");

        // An empty list entry outside real capacity is not a usable staging slot.
        inv = Inventory(2, 0);
        Put(inv, -1, head);
        Put(inv, 0, copy);
        Plan(inv, Gear(Item(copy, -1)), true, 0, 1, false, NeedsEmpty);

        // A later staging failure must reject the entire plan, not apply its safe prefix.
        var chest = Equipment(503, "Chest");
        Put(inv, 1, chest);
        Put(inv, -2, Equipment(504, "Chest"));
        Plan(inv, Gear(Item(chest, -2), Item(copy, -1)), true, 0, 2, false, NeedsEmpty);

        // Full inventory is fine when different IDs can be exchanged without staging.
        inv = Inventory(1, 0);
        Put(inv, -1, head);
        var different = Equipment(505, "Head");
        Put(inv, 0, different);
        var plan = Plan(inv, Gear(Item(different, -1)), true, 0, 1, false, null);
        Equal(1, ((IList) plan[7]).Count, "full inventory nonmerge replacement is a single safe swap");
        Execute(inv, plan);
        Same(head, At(inv, 0), "ordinary replacement preserves displaced equipment");

        inv = Inventory(1, 0);
        Plan(inv, Gear(Item(head, -1)), true, 0, 1, false,
            "Cannot find selected item check (500); loadout was not equipped.");
    }

    private static object[] Plan(object inv, Array gear, bool offhand, int accessories, int spaces, bool restore, string failure)
    {
        var before = Slots(inv);
        object[] args = {gear, inv, offhand, accessories, spaces, restore, null, null};
        Equal(failure, (string) _optimizer.GetMethod("PlanEquipGear", Static).Invoke(null, args), "plan result");
        foreach (var slot in before) Same(slot.Value, At(inv, slot.Key), "planning never mutates instance positions");
        Equal(123, (int) Get(inv, "item1"), "planning preserves item1");
        Equal(456, (int) Get(inv, "item2"), "planning preserves item2");
        Equal(null, Get(inv, "temp"), "planning does not call data swaps");
        return args;
    }

    private static void Execute(object inv, object[] plan)
    {
        var originals = new HashSet<object>(Slots(inv).Values);
        foreach (var swap in (IList) plan[7])
        {
            var equippedSlot = (int) Get(swap, "EquippedSlot");
            var inventorySlot = (int) Get(swap, "InventorySlot");
            var equipped = At(inv, equippedSlot);
            var inventory = At(inv, inventorySlot);
            Same(Get(swap, "EquippedItem"), equipped, "operation equipped identity matches simulation");
            Same(Get(swap, "InventoryItem"), inventory, "operation inventory identity matches simulation");
            Equal(true, inventorySlot >= 0 && inventorySlot < ((IList) Get(inv, "inventory")).Count,
                "controller source is always a real inventory slot");
            var id = (int) Get(inventory, "id");
            var type = Get(inventory, "type").ToString();
            var expectedType = equippedSlot <= -5 ? "Weapon" : equippedSlot >= 10000 ? "Accessory" :
                new[] {"Head", "Chest", "Legs", "Boots"}[-equippedSlot - 1];
            Equal(true, id == 0 || type == expectedType, "game equipment type guard passes");
            Equal(false, id != 0 && id == (int) Get(equipped, "id"), "no operation can merge same-ID copies");
            if (id != 0 && (equippedSlot == -5 || equippedSlot == -6 || equippedSlot >= 10000))
                foreach (var slot in Slots(inv))
                    if (slot.Key < 0 || slot.Key >= 10000)
                        Equal(false, id == (int) Get(slot.Value, "id"), "game alreadyEquipped guard passes");
            Set(inv, "item1", equippedSlot);
            Set(inv, "item2", inventorySlot);
            var methods = new[] {"swapHead", "swapChest", "swapLegs", "swapBoots", "swapWeapon", "swapWeapon2"};
            if (equippedSlot < 0)
                _inventory.GetMethod(methods[-equippedSlot - 1], Instance).Invoke(inv, null);
            else
                _inventory.GetMethod("swapAccWithItem", Instance).Invoke(inv, new object[] {equippedSlot - 10000, inventorySlot});
            Same(inventory, At(inv, equippedSlot), "data swap equipped postcondition");
            Same(equipped, At(inv, inventorySlot), "data swap inventory postcondition");
        }
        foreach (DictionaryEntry destination in (IDictionary) plan[6])
            Same(destination.Key, At(inv, (int) destination.Value), "final selected instance destination");
        var after = new HashSet<object>(Slots(inv).Values);
        Equal(true, originals.SetEquals(after), "all original instances survive the full plan");
    }

    private static object Inventory(int spaces, int accessories)
    {
        var inv = FormatterServices.GetUninitializedObject(_inventory);
        foreach (var field in new[] {"head", "chest", "legs", "boots", "weapon", "weapon2"}) Set(inv, field, Equipment(0, "Head"));
        foreach (var field in new[] {"inventory", "accs", "loadouts"})
        {
            var type = _inventory.GetField(field, Instance).FieldType;
            var list = (IList) Activator.CreateInstance(type);
            var count = field == "inventory" ? spaces : field == "accs" ? accessories : 0;
            for (var i = 0; i < count; i++) list.Add(Equipment(0, "Accessory"));
            Set(inv, field, list);
        }
        Set(inv, "item1", 123);
        Set(inv, "item2", 456);
        return inv;
    }

    private static object Equipment(int id, string part)
    {
        var equipment = FormatterServices.GetUninitializedObject(_equipment);
        Set(equipment, "id", id);
        Set(equipment, "level", 17);
        Set(equipment, "removable", true);
        Set(equipment, "type", Enum.Parse(_equipment.GetField("type", Instance).FieldType, part));
        return equipment;
    }

    private static object Item(object equipment, int slot)
    {
        var item = FormatterServices.GetUninitializedObject(_item);
        _item.GetProperty("equipment", Instance).SetValue(item, equipment);
        _item.GetProperty("slot", Instance).SetValue(item, slot);
        _item.GetProperty("id", Instance).SetValue(item, Get(equipment, "id"));
        _item.GetProperty("name", Instance).SetValue(item, "check");
        return item;
    }

    private static Array Gear(params object[] items)
    {
        var gear = Array.CreateInstance(_item, items.Length);
        for (var i = 0; i < items.Length; i++) gear.SetValue(items[i], i);
        return gear;
    }

    private static Dictionary<int, object> Slots(object inv)
    {
        var slots = new Dictionary<int, object>();
        for (var i = -6; i <= -1; i++) slots.Add(i, At(inv, i));
        for (var i = 0; i < ((IList) Get(inv, "accs")).Count; i++) slots.Add(10000 + i, At(inv, 10000 + i));
        for (var i = 0; i < ((IList) Get(inv, "inventory")).Count; i++) slots.Add(i, At(inv, i));
        return slots;
    }

    private static object At(object inv, int slot)
    {
        if (slot >= 10000) return ((IList) Get(inv, "accs"))[slot - 10000];
        if (slot >= 0) return ((IList) Get(inv, "inventory"))[slot];
        return Get(inv, new[] {"head", "chest", "legs", "boots", "weapon", "weapon2"}[-slot - 1]);
    }

    private static void Put(object inv, int slot, object equipment)
    {
        if (slot >= 10000) ((IList) Get(inv, "accs"))[slot - 10000] = equipment;
        else if (slot >= 0) ((IList) Get(inv, "inventory"))[slot] = equipment;
        else Set(inv, new[] {"head", "chest", "legs", "boots", "weapon", "weapon2"}[-slot - 1], equipment);
    }

    private static object Get(object owner, string field) { return owner.GetType().GetField(field, Instance).GetValue(owner); }
    private static void Set(object owner, string field, object value) { owner.GetType().GetField(field, Instance).SetValue(owner, value); }
    private static void Same(object expected, object actual, string scenario) { Equal(true, ReferenceEquals(expected, actual), scenario); }
    private static void Equal<T>(T expected, T actual, string scenario)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception("Gear equip " + scenario + ": expected " + expected + ", got " + actual);
    }
}
