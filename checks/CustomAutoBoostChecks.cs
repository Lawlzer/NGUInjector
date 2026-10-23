using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

internal static class CustomAutoBoostChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Type _main, _managerType, _extensions, _item, _equipment;
    private static object _character, _controller, _inventory, _settings, _manager;
    private static string[] _names;
    private static int _assertions;

    // Only compiled selectors, snapshots, conversions, and data-only game queries run here.
    // Never call ManageCustomAutoBoost or any controller boost/merge/UI action.
    internal static int Run(Assembly assembly)
    {
        _assertions = 0;
        _main = assembly.GetType("NGUInjector.Main", true);
        _managerType = assembly.GetType("NGUInjector.Managers.InventoryManager", true);
        _extensions = assembly.GetType("NGUInjector.Extensions", true);
        _item = assembly.GetType("NGUInjector.Managers.ih", true);
        _equipment = _item.GetProperty("equipment", All).PropertyType;
        var saved = new Dictionary<FieldInfo, object>();
        foreach (var name in new[] {"Character", "Controller", "Settings", "reference"})
        {
            var field = _main.GetField(name, All);
            saved.Add(field, field.GetValue(null));
        }
        try
        {
            foreach (var count in new[] {1, 3, 10}) CheckMergeBoundaries(count);
            CheckZeroMergeSlots();
            CheckNegativeSlot();
            foreach (var locked in new[] {false, true})
            foreach (var exclusion in new[] {"blacklist", "non-equipment", "fully boosted"})
                CheckExclusion(exclusion, locked);
            CheckBoostNeeds();
            CheckNonnegativeBoostNeeds();
            CheckOrdering();
            CheckPriorities();
            CheckPhysicalCompletion();
            CheckEquipmentTelemetry();
            Console.WriteLine("Custom auto-boost checks passed: " + _assertions + " assertions.");
            return _assertions;
        }
        finally
        {
            foreach (var field in saved) field.Key.SetValue(null, field.Value);
        }
    }

    private static void Setup(int mergeSlots)
    {
        _character = New(_main.GetField("Character", All).FieldType);
        _controller = New(_main.GetField("Controller", All).FieldType);
        _settings = New(_main.GetField("Settings", All).FieldType);
        _main.GetField("Character", All).SetValue(null, _character);
        _main.GetField("Controller", All).SetValue(null, _controller);
        _main.GetField("Settings", All).SetValue(null, _settings);
        Set(_settings, "_boostBlacklist", new int[0]);
        Set(_settings, "_specialBoostBlacklist", new int[0]);
        Set(_settings, "_priorityBoosts", new int[0]);
        Set(_controller, "character", _character);
        Set(_character, "inventoryController", _controller);
        _inventory = Child(_character, "inventory");
        var info = Child(_character, "itemInfo");
        Set(_controller, "itemInfo", info);
        _names = new string[512];
        _names[0] = "Empty";
        Set(info, "itemName", _names);

        Child(_character, "purchases");
        Set(Child(_character, "arbitrary"), "invMergeSlots", mergeSlots);
        List(Child(_character, "beastQuest"), "quirkLevel", 56);
        List(Child(Child(_character, "adventure"), "itopod"), "perkLevel", 113);
        List(Child(_character, "wishes"), "wishes", 29);
        Equal(mergeSlots, _controller.GetType().GetMethod("totalInvMergeSlots", All).Invoke(_controller, null),
            "fixture uses the real merge-slot query");

        foreach (var field in new[] {"head", "chest", "legs", "boots", "weapon", "weapon2"})
            Set(_inventory, field, Equipment(0));
        List(_inventory, "inventory", 0);
        List(_inventory, "accs", 0);
        _manager = Activator.CreateInstance(_managerType, true);
    }

    private static void CheckMergeBoundaries(int count)
    {
        Setup(count);
        for (var slot = 0; slot <= count + 1; slot++)
            Put(slot, Equipment(100 + slot, locked: slot == count + 1));
        var expected = Enumerable.Range(0, count).Concat(new[] {count + 1}).ToArray();
        Select("unlocked merge range [0," + count + ") excludes first outside, retains locked outside", expected);
    }

    private static void CheckZeroMergeSlots()
    {
        Setup(0);
        Put(0, Equipment(100));
        Put(1, Equipment(101, locked: true));
        Select("zero merge slots retains only locked inventory", 1);
    }

    private static void CheckNegativeSlot()
    {
        Setup(3);
        var equipment = Equipment(100);
        var wrapper = _extensions.GetMethod("GetInventoryHelper", All).Invoke(null, new[] {equipment, (object) (-7)});
        var input = Array.CreateInstance(_item, 1);
        input.SetValue(wrapper, 0);
        var selected = (Array) _managerType.GetMethod("GetCustomBoostSlots", All).Invoke(_manager, new object[] {input});
        Equal(0, selected.Length, "unlocked negative slot is not an auto-merge inventory slot");
    }

    private static void CheckExclusion(string exclusion, bool locked)
    {
        Setup(1);
        if (exclusion == "blacklist") Set(_settings, "_boostBlacklist", new[] {100, 101});
        var part = exclusion == "non-equipment" ? "atkBoost" : "Head";
        var need = exclusion == "fully boosted" ? "None" : "Power";
        Put(locked ? 1 : 0, Equipment(100, part, need, locked));
        Set(_inventory, "head", Equipment(101, part, need));
        Put(2, Equipment(102, locked: true));
        Select(exclusion + " excluded from equipped and " + (locked ? "locked outside merge range" : "unlocked merge") + " inventory", 2);
    }

    private static void CheckBoostNeeds()
    {
        Setup(3);
        Put(0, Equipment(100, need: "Power"));
        Put(1, Equipment(101, need: "Toughness"));
        Put(2, Equipment(102, need: "Special"));
        Select("each individual boost deficit remains eligible", 0, 1, 2);
    }

    private static void CheckNonnegativeBoostNeeds()
    {
        Setup(1);
        var equipment = AtLevel(Equipment(100, need: "None"), 37);
        foreach (var index in new[] {2, 3})
        {
            Set(equipment, "spec" + index + "Cap", 100f);
            Set(equipment, "spec" + index + "Type", Get(equipment, "spec1Type"));
        }
        Set(_inventory, "head", equipment);
        Put(0, equipment);
        var stats = new[] {"curAttack", "curDefense", "spec1Cur", "spec2Cur", "spec3Cur"};
        foreach (var blacklisted in new[] {false, true})
        {
            Set(_settings, "_specialBoostBlacklist", blacklisted ? new[] {100} : new int[0]);
            foreach (var stat in stats)
            {
                foreach (var other in stats) Set(equipment, other, 1000f);
                Set(equipment, stat, 136.5f);
                var scenario = stat + " deficit with other stats overcap, special blacklist=" + blacklisted;
                var special = stat.StartsWith("spec");
                CheckNeededBoosts(equipment, stat == "curAttack" ? 0.5m : 0m,
                    stat == "curDefense" ? 0.5m : 0m, special && !blacklisted ? 0.5m : 0m, scenario);
                Select(scenario, special && blacklisted ? new int[0] : new[] {-1, 0});
            }
            foreach (var stat in stats) Set(equipment, stat, 1000f);
            CheckNeededBoosts(equipment, 0m, 0m, 0m, "all overcap, special blacklist=" + blacklisted);
            Select("all overcap excluded, special blacklist=" + blacklisted);
            foreach (var stat in stats) Set(equipment, stat, 136.5f);
            CheckNeededBoosts(equipment, 0.5m, 0.5m, blacklisted ? 0m : 1.5m,
                "individual deficits sum without negatives, special blacklist=" + blacklisted);
            Select("remaining physical deficits eligible, special blacklist=" + blacklisted, -1, 0);
        }
    }

    private static void CheckNeededBoosts(object equipment, decimal power, decimal toughness, decimal special, string scenario)
    {
        var needed = _extensions.GetMethod("GetNeededBoosts", All).Invoke(null, new[] {equipment});
        Equal(power, needed.GetType().GetProperty("Power", All).GetValue(needed), scenario + " Power");
        Equal(toughness, needed.GetType().GetProperty("Toughness", All).GetValue(needed), scenario + " Toughness");
        Equal(special, needed.GetType().GetProperty("Special", All).GetValue(needed), scenario + " Special");
        Equal(power + toughness + special, needed.GetType().GetMethod("Total", All).Invoke(needed, null), scenario + " total");
    }

    private static void CheckOrdering()
    {
        Setup(3);
        var fields = new[] {"head", "chest", "legs", "boots", "weapon", "weapon2"};
        var parts = new[] {"Head", "Chest", "Legs", "Boots", "Weapon", "Weapon"};
        for (var i = 0; i < fields.Length; i++) Set(_inventory, fields[i], Equipment(200 + i, parts[i]));
        var wishes = (IList) Get(Get(_character, "wishes"), "wishes");
        Set(wishes[28], "level", 1);
        var accessories = (IList) Get(_inventory, "accs");
        accessories.Add(Equipment(210, "Accessory"));
        accessories.Add(Equipment(211, "Accessory", heart: true));
        Put(0, Equipment(100));
        Put(1, Equipment(101, heart: true));
        Put(2, Equipment(102, locked: true));
        Put(6, Equipment(106, locked: true));
        Put(7, Equipment(107, locked: true, heart: true));
        Select("weapons then armor/accessories then ascending inventory; equipped and inventory hearts last",
            -5, -6, -4, -3, -2, -1, 10000, 0, 2, 6, 10001, 1, 7);
    }

    private static void Select(string scenario, params int[] expected)
    {
        var converted = (IEnumerable) _extensions.GetMethod("GetConvertedInventory", All).Invoke(null, new[] {_inventory});
        // Reverse the real converted wrappers so input order cannot satisfy inventory ordering accidentally.
        var wrappers = converted.Cast<object>().Reverse().ToArray();
        var input = Array.CreateInstance(_item, wrappers.Length);
        for (var i = 0; i < wrappers.Length; i++) input.SetValue(wrappers[i], i);
        var selected = ((Array) _managerType.GetMethod("GetCustomBoostSlots", All).Invoke(_manager, new object[] {input})).Cast<object>().ToArray();
        Equal(string.Join(",", expected), string.Join(",", selected.Select(x => (int) _item.GetProperty("slot", All).GetValue(x))), scenario);
        for (var i = 0; i < expected.Length; i++)
        {
            var slot = expected[i];
            var equipment = slot >= 10000 ? ((IList) Get(_inventory, "accs"))[slot - 10000] : slot >= 0
                ? ((IList) Get(_inventory, "inventory"))[slot]
                : Get(_inventory, new[] {"head", "chest", "legs", "boots", "weapon", "weapon2"}[-slot - 1]);
            Equal(true, ReferenceEquals(equipment, _item.GetProperty("equipment", All).GetValue(selected[i])), scenario + " instance at " + slot);
        }
        for (var i = 0; i < wrappers.Length; i++)
            Equal(true, ReferenceEquals(wrappers[i], input.GetValue(i)), scenario + " leaves input order unchanged");
    }

    private static void CheckPriorities()
    {
        Setup(3);
        Set(_inventory, "head", Equipment(200));
        Set(_inventory, "weapon", Equipment(201, "Weapon"));
        ((IList) Get(_inventory, "accs")).Add(Equipment(100, "Accessory", heart: true));
        Put(0, Equipment(100, heart: true));
        Put(1, Equipment(101));
        Put(2, Equipment(102));
        Put(4, Equipment(100, locked: true, heart: true));
        Put(5, Equipment(103, locked: true));
        Put(6, Equipment(104));
        Put(7, Equipment(105, locked: true));
        Put(8, Equipment(106, need: "None", locked: true));
        Put(9, Equipment(107, "atkBoost", locked: true));
        Set(_settings, "_boostBlacklist", new[] {105});
        Set(_settings, "_priorityBoosts", null);
        Select("null priorities retain default order", -5, -1, 1, 2, 5, 10000, 0, 4);
        Set(_settings, "_priorityBoosts", new int[0]);
        Select("empty priorities retain default order", -5, -1, 1, 2, 5, 10000, 0, 4);
        var priorities = new[] {100, 103, 101, 100, 104, 105, 106, 107, 999};
        Set(_settings, "_priorityBoosts", priorities);
        Select("explicit heart overrides hearts-last; duplicate copies stable; blacklist and eligibility win",
            10000, 0, 4, 5, 1, -5, -1, 2);
        Equal("100,103,101,100,104,105,106,107,999", string.Join(",", priorities), "selector does not rewrite saved priorities");
        Set(_settings, "_priorityBoosts", new[] {101, 103, 100});
        Select("edited priority order takes effect without disturbing remaining defaults", 1, 5, 10000, 0, 4, -5, -1, 2);
        Set(_settings, "_boostBlacklist", new[] {100, 105});
        Select("blacklist excludes all prioritized equipped and inventory copies", 1, 5, -5, -1, 2);
    }

    private static void CheckPhysicalCompletion()
    {
        Setup(0);
        Equal(false, FullyBoosted(null), "null equipment is not physically complete");
        foreach (var level in new[] {0, 37, 100, 120})
        {
            var equipment = AtLevel(Equipment(100, need: "None"), level);
            foreach (var index in new[] {2, 3})
            {
                Set(equipment, "spec" + index + "Cap", 100f);
                Set(equipment, "spec" + index + "Cur", 100f + level);
                Set(equipment, "spec" + index + "Type", Get(equipment, "spec1Type"));
            }
            Set(_settings, "_specialBoostBlacklist", new[] {100});
            Equal(true, FullyBoosted(equipment), "all native stats at caps at level " + level);
            var stats = new[] {"curAttack", "curDefense", "spec1Cur", "spec2Cur", "spec3Cur"};
            foreach (var stat in stats)
            {
                Set(equipment, stat, 99.5f + level);
                Equal(false, FullyBoosted(equipment), stat + " deficit cannot be blacklisted at level " + level);
                foreach (var other in stats.Where(x => x != stat)) Set(equipment, other, 1000f);
                Equal(false, FullyBoosted(equipment), "overcapped stats cannot cancel " + stat + " deficit");
                foreach (var other in stats) Set(equipment, other, 100f + level);
            }
        }
        var absent = Equipment(100, need: "None");
        Set(absent, "capAttack", 0f);
        Set(absent, "capDefense", 0f);
        Set(absent, "spec1Type", Enum.ToObject(_equipment.GetField("spec1Type", All).FieldType, 0));
        Set(absent, "curAttack", -1f);
        Set(absent, "curDefense", -1f);
        Set(absent, "spec1Cur", -1f);
        Equal(true, FullyBoosted(absent), "absent native stats do not require boosts");
        var rounded = AtLevel(Equipment(100, need: "None"), 33);
        Set(rounded, "capAttack", 100.75f);
        Set(rounded, "curAttack", 133f);
        Equal(true, FullyBoosted(rounded), "completion uses CalcCap floor at current level");
        Set(rounded, "curAttack", 132.5f);
        Equal(false, FullyBoosted(rounded), "fractional deficit below floored cap remains incomplete");
    }

    private static bool FullyBoosted(object equipment)
    {
        return (bool) _extensions.GetMethod("IsFullyBoosted", All).Invoke(null, new[] {equipment});
    }

    private static object AtLevel(object equipment, int level)
    {
        Set(equipment, "level", level);
        foreach (var stat in new[] {"curAttack", "curDefense", "spec1Cur"}) Set(equipment, stat, 100f + level);
        return equipment;
    }

    private static void CheckEquipmentTelemetry()
    {
        Setup(0);
        Set(_inventory, "head", AtLevel(Equipment(100, need: "None"), 100));
        Put(0, AtLevel(Equipment(100, need: "None"), 99));
        var special = AtLevel(Equipment(100, need: "None"), 80);
        Set(special, "spec1Cur", 0f);
        Put(1, special);
        Put(2, AtLevel(Equipment(101, need: "None"), 100));
        Put(3, AtLevel(Equipment(101, need: "None"), 101));
        Put(4, Equipment(102, need: "None"));
        var unboosted = AtLevel(Equipment(103, need: "None"), 100);
        Set(unboosted, "curDefense", 199f);
        Put(5, unboosted);
        ((IList) Get(_inventory, "accs")).Add(AtLevel(Equipment(104, "Accessory", "None"), 100));
        Put(6, AtLevel(Equipment(104, "Accessory", "None"), 60));
        var overlevel = AtLevel(Equipment(105, need: "None"), 120);
        Set(overlevel, "curAttack", 219f);
        Put(7, overlevel);
        Put(8, Equipment(106, "atkBoost"));
        var special2 = AtLevel(Equipment(107, need: "None"), 100);
        Set(special2, "spec2Type", Get(special2, "spec1Type"));
        Set(special2, "spec2Cap", 100f);
        Set(special2, "spec2Cur", 199f);
        Put(9, special2);
        Put(10, AtLevel(Equipment(105, need: "None"), 100));
        Set(_inventory, "weapon2", Equipment(108, "Weapon"));
        Set(_settings, "_boostBlacklist", new[] {100, 103});
        Set(_settings, "_specialBoostBlacklist", new[] {100, 107});
        Set(_settings, "_priorityBoosts", new[] {101, 100});
        _main.GetField("Controller", All).SetValue(null, null);
        var snapshot = _main.GetMethod("GetEquipmentBoostsRuntime", All);
        var state = snapshot.Invoke(null, null);
        Equal("true", Value(Node(state, "available")), "owned equipment telemetry available");
        Equal(string.Empty, Value(Node(state, "reason")), "available telemetry has no failure reason");
        var items = Node(state, "items");
        Equal(7, Count(items), "unique IDs exclude only completely finished equipment and non-equipment");
        CheckRow(items, 0, 100, 3, 80, false);
        CheckRow(items, 1, 102, 1, 0, true);
        CheckRow(items, 2, 103, 1, 100, false);
        CheckRow(items, 3, 104, 2, 60, true);
        CheckRow(items, 4, 105, 2, 120, false);
        CheckRow(items, 5, 107, 1, 100, false);
        CheckRow(items, 6, 108, 1, 0, false);
        Equal(state.ToString(), snapshot.Invoke(null, null).ToString(), "repeated snapshots are stable");
        Equal(0f, Get(special, "spec1Cur"), "snapshot does not boost special stats");
        Equal(199f, Get(unboosted, "curDefense"), "snapshot does not boost defense");
        Equal("101,100", string.Join(",", (int[]) Get(_settings, "_priorityBoosts")), "snapshot leaves priorities unchanged");
        Equal(11, ((IList) Get(_inventory, "inventory")).Count, "snapshot does not merge or remove inventory");

        // Exercise the real runtime root without a Unity Main instance or settings publication.
        _main.GetField("reference", All).SetValue(null, null);
        _main.GetField("Settings", All).SetValue(null, null);
        var builder = _main.GetMethod("BuildExternalUiRuntimeState", All);
        var runtime = builder.Invoke(null, null);
        Equal(state.ToString(), Node(runtime, "equipmentBoosts").ToString(), "runtime root exposes exact equipment contract without settings");
        Equal("3", Value(Node(runtime, "protocolVersion")), "runtime remains intact");
        var revision = _main.GetField("_externalUiRuntimeRevision", All).GetValue(null);
        Set(_character, "inventory", null);
        CheckUnavailable(Node(builder.Invoke(null, null), "equipmentBoosts"), "missing inventory");
        Set(_character, "inventory", _inventory);
        Set(Get(_character, "itemInfo"), "itemName", _names.Take(108).ToArray());
        runtime = builder.Invoke(null, null);
        CheckUnavailable(Node(runtime, "equipmentBoosts"), "name lookup failure after capture started");
        Equal("3", Value(Node(runtime, "protocolVersion")), "equipment failure does not abort runtime");
        Set(Get(_character, "itemInfo"), "itemName", _names);
        Set(special2, "spec2Cur", float.NaN);
        CheckUnavailable(Node(builder.Invoke(null, null), "equipmentBoosts"), "invalid native stat");
        _main.GetField("Character", All).SetValue(null, null);
        CheckUnavailable(Node(builder.Invoke(null, null), "equipmentBoosts"), "missing character");
        Equal(revision, _main.GetField("_externalUiRuntimeRevision", All).GetValue(null), "snapshot builders do not publish or consume runtime revisions");
        Setup(0);
        state = snapshot.Invoke(null, null);
        Equal("true", Value(Node(state, "available")), "empty but readable ownership is available");
        Equal(0, Count(Node(state, "items")), "empty ownership emits an empty array");
    }

    private static void CheckRow(object items, int index, int id, int copies, int level, bool fullyBoosted)
    {
        var row = items.GetType().GetProperty("Item", new[] {typeof(int)}).GetValue(items, new object[] {index});
        Equal(5, Count(row), "equipment row contract field count");
        Equal(id.ToString(), Value(Node(row, "id")), "equipment row ID");
        Equal(_names[id], Value(Node(row, "name")), "equipment row name");
        Equal(copies.ToString(), Value(Node(row, "copies")), "all owned copies counted");
        Equal(level.ToString(), Value(Node(row, "level")), "minimum level of incomplete copies");
        Equal(fullyBoosted ? "true" : "false", Value(Node(row, "fullyBoosted")), "all owned copies physically boosted");
        foreach (var key in new[] {"id", "copies", "level"})
            Equal(true, Node(row, key).GetType().GetProperty("IsNumber").GetValue(Node(row, key)), key + " is a JSON number");
        Equal(true, Node(row, "name").GetType().GetProperty("IsString").GetValue(Node(row, "name")), "name is a JSON string");
        Equal(true, Node(row, "fullyBoosted").GetType().GetProperty("IsBoolean").GetValue(Node(row, "fullyBoosted")), "fullyBoosted is a JSON boolean");
    }

    private static void CheckUnavailable(object state, string scenario)
    {
        Equal("false", Value(Node(state, "available")), scenario + " unavailable");
        Equal(true, Value(Node(state, "reason")).Length > 0, scenario + " provides reason");
        Equal(0, Count(Node(state, "items")), scenario + " does not leak partial items");
    }

    private static object Node(object parent, string key) { return parent.GetType().GetProperty("Item", new[] {typeof(string)}).GetValue(parent, new object[] {key}); }
    private static string Value(object node)
    {
        var value = (string) node.GetType().GetProperty("Value").GetValue(node);
        return (bool) node.GetType().GetProperty("IsBoolean").GetValue(node) ? value.ToLowerInvariant() : value;
    }
    private static int Count(object node) { return (int) node.GetType().GetProperty("Count").GetValue(node); }

    private static object Equipment(int id, string part = "Head", string need = "Power", bool locked = false, bool heart = false)
    {
        var equipment = New(_equipment);
        Set(equipment, "id", id);
        Set(equipment, "type", Enum.Parse(_equipment.GetField("type", All).FieldType, part));
        Set(equipment, "removable", !locked);
        foreach (var field in new[] {"capAttack", "capDefense", "spec1Cap", "curAttack", "curDefense", "spec1Cur"})
            Set(equipment, field, 100f);
        Set(equipment, "spec1Type", Enum.Parse(_equipment.GetField("spec1Type", All).FieldType, "NGU"));
        if (need != "None") Set(equipment, need == "Power" ? "curAttack" : need == "Toughness" ? "curDefense" : "spec1Cur", 0f);
        _names[id] = heart ? "Check HeArT " + id : "Check equipment " + id;
        return equipment;
    }

    private static void Put(int slot, object equipment)
    {
        var inventory = (IList) Get(_inventory, "inventory");
        while (inventory.Count <= slot) inventory.Add(Equipment(0));
        inventory[slot] = equipment;
    }

    private static void List(object parent, string name, int count)
    {
        var field = parent.GetType().GetField(name, All);
        var list = (IList) Activator.CreateInstance(field.FieldType);
        var element = field.FieldType.GetGenericArguments()[0];
        for (var i = 0; i < count; i++) list.Add(element.IsValueType ? Activator.CreateInstance(element) : New(element));
        field.SetValue(parent, list);
    }

    private static object New(Type type) { return FormatterServices.GetUninitializedObject(type); }
    private static object Get(object parent, string name) { return parent.GetType().GetField(name, All).GetValue(parent); }
    private static void Set(object parent, string name, object value) { parent.GetType().GetField(name, All).SetValue(parent, value); }
    private static object Child(object parent, string name)
    {
        var value = New(parent.GetType().GetField(name, All).FieldType);
        Set(parent, name, value);
        return value;
    }
    private static void Equal(object expected, object actual, string scenario)
    {
        _assertions++;
        if (!Equals(expected, actual)) throw new Exception("Custom auto-boost " + scenario + ": expected " + expected + ", got " + actual);
    }
}
