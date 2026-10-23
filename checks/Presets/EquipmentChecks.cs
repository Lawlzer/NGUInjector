using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

internal static class PresetEquipmentChecks
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static Type inventoryType, equipmentType, itemType;
    private static MethodInfo planner;
    private static int assertions;

    public static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Pass the production NGUInjector.dll and installed game's Managed directory.");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
            var path = Path.Combine(args[1], new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        var production = Assembly.LoadFrom(args[0]);
        var optimizer = production.GetType("NGUInjector.Managers.GearOptimizer", true);
        itemType = production.GetType("NGUInjector.Managers.ih", true);
        equipmentType = itemType.GetProperty("equipment", Instance).PropertyType;
        inventoryType = equipmentType.Assembly.GetType("Inventory", true);
        planner = optimizer.GetMethod("PlanEquipGear", BindingFlags.NonPublic | BindingFlags.Static);
        if (planner == null) throw new Exception("Production preset preflight is missing; build the current sources first.");

        foreach (var emptyMain in new[] { true, false })
        {
            var inv = Inventory(1, 0); var weapon = Equipment(41, "Weapon");
            var source = emptyMain ? -6 : -5; var destination = emptyMain ? -5 : -6;
            Put(inv, source, weapon);
            var swaps = Plan(inv, new[] { Item(weapon, destination) }, 0, 1, null);
            Check(swaps.Count == 2, "moved hand stages through inventory before equip");
            Execute(inv, swaps);
            Check(ReferenceEquals(At(inv, destination), weapon), "selected weapon reaches pinned destination");
            Check((int)Get(At(inv, source), "id") == 0, "moved weapon really leaves the intended hand empty");
        }

        var accessories = Inventory(1, 2); var accessory = Equipment(51, "Accessory");
        Put(accessories, 10001, accessory);
        Execute(accessories, Plan(accessories, new[] { Item(accessory, 10000) }, 2, 1, null));
        Check(ReferenceEquals(At(accessories, 10000), accessory) && (int)Get(At(accessories, 10001), "id") == 0,
            "moved empty accessory is executable by the actual data swaps");

        var current = Inventory(1, 0); var offhand = Equipment(41, "Weapon"); var replacement = Equipment(42, "Weapon");
        Put(current, -6, offhand); Put(current, 0, replacement);
        Execute(current, Plan(current, new[] { Item(replacement, -5) }, 0, 1, null));
        Check(ReferenceEquals(At(current, -5), replacement) && (int)Get(At(current, -6), "id") == 0,
            "shared exact planner clears omitted offhand using space freed by incoming gear");
        current = Inventory(1, 0); Put(current, -6, offhand); Put(current, 0, replacement);
        var unchanged = Plan(current, new[] { Item(offhand, -6) }, 0, 1, null);
        Check(unchanged.Count == 0 && (int)Get(At(current, -5), "id") == 0 && ReferenceEquals(At(current, -6), offhand),
            "current [0,41] preflights with full inventory despite another policy needing an unexecutable empty slot");
        Plan(current, new[] { Item(offhand, -5) }, 0, 1, "requires an empty inventory slot");
        Check(ReferenceEquals(At(current, -6), offhand) && ReferenceEquals(At(current, 0), replacement),
            "failed staging preflight never executes a safe prefix");
        Console.WriteLine("Preset equipment checks passed: " + assertions + " assertions (production planner and offline game Inventory data swaps).");
    }

    private static IList Plan(object inv, object[] items, int accessories, int spaces, string failure)
    {
        var gear = Array.CreateInstance(itemType, items.Length);
        for (var i = 0; i < items.Length; i++) gear.SetValue(items[i], i);
        var before = Slots(inv);
        var destinations = Enumerable.Range(-6, 6).Concat(Enumerable.Range(10000, accessories)).ToArray();
        var exact = Array.CreateInstance(itemType, destinations.Length);
        for (var i = 0; i < destinations.Length; i++)
        {
            var selected = items.FirstOrDefault(x => (int)itemType.GetProperty("slot", Instance).GetValue(x) == destinations[i]);
            exact.SetValue(selected ?? Item(Equipment(0, "Weapon"), destinations[i]), i);
        }
        object[] arguments = { gear, inv, true, accessories, spaces, true, null, null, exact };
        var result = (string)planner.Invoke(null, arguments);
        Check(failure == null ? result == null : result != null && result.Contains(failure), "exact-empty preflight result: " + result);
        foreach (var slot in before) Check(ReferenceEquals(slot.Value, At(inv, slot.Key)), "preflight preserves every actual inventory instance");
        Check((int)Get(inv, "item1") == 123 && (int)Get(inv, "item2") == 456, "preflight does not execute controller/data swaps");
        return (IList)arguments[7];
    }

    private static void Execute(object inv, IList swaps)
    {
        var before = new HashSet<object>(Slots(inv).Values);
        foreach (var swap in swaps)
        {
            var equipped = (int)Get(swap, "EquippedSlot"); var source = (int)Get(swap, "InventorySlot");
            Check(source >= 0 && source < ((IList)Get(inv, "inventory")).Count, "actual game swap always uses a real inventory source");
            Check(ReferenceEquals(Get(swap, "EquippedItem"), At(inv, equipped)) && ReferenceEquals(Get(swap, "InventoryItem"), At(inv, source)),
                "planned instance identities match actual data swaps");
            Set(inv, "item1", equipped); Set(inv, "item2", source);
            if (equipped >= 10000) inventoryType.GetMethod("swapAccWithItem", Instance).Invoke(inv, new object[] { equipped - 10000, source });
            else inventoryType.GetMethod(equipped == -5 ? "swapWeapon" : "swapWeapon2", Instance).Invoke(inv, null);
            Check(ReferenceEquals(Get(swap, "InventoryItem"), At(inv, equipped)), "native data swap reaches predicted destination");
        }
        Check(before.SetEquals(Slots(inv).Values), "all original equipment and empty instances survive");
    }

    private static object Inventory(int spaces, int accessories)
    {
        var inv = FormatterServices.GetUninitializedObject(inventoryType);
        foreach (var slot in new[] { "head", "chest", "legs", "boots", "weapon", "weapon2" }) Set(inv, slot, Equipment(0, "Weapon"));
        foreach (var field in new[] { "inventory", "accs", "loadouts" })
        {
            var list = (IList)Activator.CreateInstance(inventoryType.GetField(field, Instance).FieldType);
            var count = field == "inventory" ? spaces : field == "accs" ? accessories : 0;
            for (var i = 0; i < count; i++) list.Add(Equipment(0, "Accessory"));
            Set(inv, field, list);
        }
        Set(inv, "item1", 123); Set(inv, "item2", 456);
        return inv;
    }

    private static object Equipment(int id, string type)
    {
        var equipment = FormatterServices.GetUninitializedObject(equipmentType);
        Set(equipment, "id", id); Set(equipment, "level", 17); Set(equipment, "removable", true);
        Set(equipment, "type", Enum.Parse(equipmentType.GetField("type", Instance).FieldType, type));
        return equipment;
    }

    private static object Item(object equipment, int destination)
    {
        var item = Activator.CreateInstance(itemType, true);
        itemType.GetProperty("equipment", Instance).SetValue(item, equipment);
        itemType.GetProperty("id", Instance).SetValue(item, Get(equipment, "id"));
        itemType.GetProperty("slot", Instance).SetValue(item, destination);
        itemType.GetProperty("name", Instance).SetValue(item, "Empty-slot regression");
        return item;
    }

    private static Dictionary<int, object> Slots(object inv)
    {
        var slots = Enumerable.Range(-6, 6).ToDictionary(x => x, x => At(inv, x));
        for (var i = 0; i < ((IList)Get(inv, "inventory")).Count; i++) slots.Add(i, At(inv, i));
        for (var i = 0; i < ((IList)Get(inv, "accs")).Count; i++) slots.Add(10000 + i, At(inv, 10000 + i));
        return slots;
    }

    private static object At(object inv, int slot)
    {
        if (slot >= 10000) return ((IList)Get(inv, "accs"))[slot - 10000];
        if (slot >= 0) return ((IList)Get(inv, "inventory"))[slot];
        return Get(inv, new[] { "head", "chest", "legs", "boots", "weapon", "weapon2" }[-slot - 1]);
    }

    private static void Put(object inv, int slot, object item)
    {
        if (slot >= 10000) ((IList)Get(inv, "accs"))[slot - 10000] = item;
        else if (slot >= 0) ((IList)Get(inv, "inventory"))[slot] = item;
        else Set(inv, new[] { "head", "chest", "legs", "boots", "weapon", "weapon2" }[-slot - 1], item);
    }

    private static object Get(object owner, string field) { return owner.GetType().GetField(field, Instance).GetValue(owner); }
    private static void Set(object owner, string field, object value) { owner.GetType().GetField(field, Instance).SetValue(owner, value); }
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
}
