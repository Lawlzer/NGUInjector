using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class GearSingleStatChecks
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] Goals = {"Drop", "Gold", "EnergyCap", "MagicCap"};
    private static readonly string[] MainSlots = {"Head", "Chest", "Legs", "Boots"};
    private static Type _optimizer, _candidate, _stat, _goal, _mode, _item, _equipment, _part;
    private static int _assertions;

    internal static int Run(Assembly assembly)
    {
        _assertions = 0;
        _optimizer = assembly.GetType("NGUInjector.Managers.GearOptimizer", true);
        _candidate = _optimizer.GetNestedType("GearCandidate", BindingFlags.NonPublic);
        _stat = _optimizer.GetNestedType("StatKey", BindingFlags.NonPublic);
        _goal = assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true);
        _mode = assembly.GetType("NGUInjector.Managers.FightScoreMode", true);
        _item = assembly.GetType("NGUInjector.Managers.ih", true);
        _equipment = _item.GetProperty("equipment", Instance).PropertyType;
        _part = _equipment.GetField("type", Instance).FieldType;
        CheckSlotSorting();
        CheckSlotLimits();
        CheckZeroRelevantStats();
        CheckExhaustive();
        CheckManyTies();
        CheckFailures();
        Console.WriteLine("Gear single-stat checks passed: " + _assertions + " assertions.");
        return _assertions;
    }

    private static void CheckSlotSorting()
    {
        foreach (var goal in Goals)
        {
            var relevant = (Array) Call("GetRelevantStats", Enum.Parse(_goal, goal));
            Equal(1, relevant.Length, goal + " has exactly one relevant stat");
            Equal(goal, relevant.GetValue(0).ToString(), goal + " relevant stat is explicit");
            var candidates = Candidates();
            var expectedMain = new object[4];
            for (var slot = 0; slot < MainSlots.Length; slot++)
            {
                // Level is deliberately inverted relative to the already-applied bonus.
                candidates.Add(Candidate(100 + slot, MainSlots[slot], 100, goal, 1d, "Power", 100000d));
                expectedMain[slot] = Candidate(100 + slot, MainSlots[slot], 1, goal, (slot + 1) * 10d);
                candidates.Add(expectedMain[slot]);
                candidates.Add(Candidate(110 + slot, MainSlots[slot], 99, goal, 2d));
            }
            var strongest = Candidate(200, "Weapon", 1, goal, 80d);
            var second = Candidate(201, "Weapon", 17, goal, 60d);
            candidates.Add(second);
            candidates.Add(Candidate(200, "Weapon", 100, goal, 75d, "Power", 100000d));
            candidates.Add(Candidate(202, "Weapon", 100, goal, 40d));
            candidates.Add(strongest);
            var bestAccessory = Candidate(300, "Accessory", 1, goal, 90d);
            var nextAccessory = Candidate(301, "Accessory", 17, goal, 70d);
            candidates.Add(nextAccessory);
            candidates.Add(Candidate(300, "Accessory", 100, goal, 85d));
            candidates.Add(Candidate(302, "Accessory", 100, goal, 50d));
            candidates.Add(bestAccessory);
            candidates.Add(Candidate(300, "Accessory", 90, goal, 88d, "Power", 100000d));
            candidates.Add(Candidate(303, "Accessory", 100, "Power", 1000000d));

            foreach (var factor in new[] {0f, .5f, 1f})
            foreach (var mode in Enum.GetNames(_mode))
            {
                var scenario = goal + "/slots/factor=" + factor + "/" + mode;
                var result = Optimize(goal, candidates, 2, factor, mode);
                // 100 baseline + 100 armor + 80 mainhand + 160 accessories.
                CheckResult(result, ExpectedScore(goal, 440d + 60d * factor), scenario);
                CheckSelection(result, candidates, goal, 2, factor, scenario);
                var items = (Array) Get(result, "Items");
                for (var slot = 0; slot < MainSlots.Length; slot++)
                    Same(Get(expectedMain[slot], "Item"), items.GetValue(slot), scenario + " best applied " + MainSlots[slot]);
                Same(Get(strongest, "Item"), items.GetValue(4), scenario + " strongest distinct mainhand");
                if (factor > 0)
                    Same(Get(second, "Item"), items.GetValue(5), scenario + " offhand uses second ID, not second copy");
                var start = factor > 0 ? 6 : 5;
                Same(Get(bestAccessory, "Item"), items.GetValue(start), scenario + " lower-level best accessory copy");
                Same(Get(nextAccessory, "Item"), items.GetValue(start + 1), scenario + " second distinct accessory ID");
            }
        }
    }

    private static void CheckSlotLimits()
    {
        foreach (var goal in Goals)
        {
            var candidates = RequiredGear();
            // Two physical weapon copies still permit only one weapon ID.
            candidates.RemoveAt(4);
            candidates.Add(Candidate(200, "Weapon", 100, goal, 10d));
            var weapon = Candidate(200, "Weapon", 1, goal, 30d);
            candidates.Add(weapon);
            candidates.Add(Candidate(300, "Accessory", 100, goal, 40d));
            var accessory = Candidate(300, "Accessory", 1, goal, 60d);
            candidates.Add(accessory);
            candidates.Add(Candidate(301, "Accessory", 17, goal, 20d));
            foreach (var factor in new[] {0f, .5f, 1f})
            foreach (var slots in new[] {0, 1, 10})
            {
                var scenario = goal + "/limits/slots=" + slots + "/factor=" + factor;
                var total = slots == 0 ? 130d : slots == 1 ? 190d : 210d;
                var result = Optimize(goal, candidates, slots, factor);
                CheckResult(result, ExpectedScore(goal, total), scenario);
                CheckSelection(result, candidates, goal, slots, factor, scenario);
                Same(Get(weapon, "Item"), ((Array) Get(result, "Items")).GetValue(4), scenario + " best sole weapon ID");
                if (slots > 0)
                    Same(Get(accessory, "Item"), ((Array) Get(result, "Items")).GetValue(5), scenario + " best sole accessory copy");
            }

            candidates = RequiredGear();
            var withoutAccessories = Optimize(goal, candidates, 10, 1f);
            CheckResult(withoutAccessories, ExpectedScore(goal, 100d), goal + " no owned accessories");
            CheckSelection(withoutAccessories, candidates, goal, 10, 1f, goal + " no owned accessories");
        }
    }

    private static void CheckZeroRelevantStats()
    {
        var candidates = RequiredGear();
        foreach (var candidate in candidates)
            ((IDictionary) Get(candidate, "Stats")).Add(Enum.Parse(_stat, "Power"), 100000d);
        candidates.Add(Candidate(201, "Weapon", 100, "Toughness", 100000d));
        candidates.Add(Candidate(300, "Accessory", 100, "EnergyPower", 100000d));
        candidates.Add(Candidate(300, "Accessory", 1));
        candidates.Add(Candidate(301, "Accessory", 17));
        candidates.Add(Candidate(302, "Accessory", 17, "MagicPower", 100000d));
        foreach (var goal in Goals)
        foreach (var factor in new[] {0f, .5f, 1f})
        foreach (var slots in new[] {0, 2, 10})
        {
            var scenario = goal + "/zero/slots=" + slots + "/factor=" + factor;
            var result = Optimize(goal, candidates, slots, factor);
            CheckResult(result, ExpectedScore(goal, 100d), scenario);
            CheckSelection(result, candidates, goal, slots, factor, scenario);
        }
    }

    private static void CheckExhaustive()
    {
        // The same inventory has nondominated same-ID tradeoffs across all four
        // goals. Each single-stat run must choose its own best physical copy.
        var candidates = Candidates();
        for (var slot = 0; slot < MainSlots.Length; slot++)
        {
            candidates.Add(Candidate(100 + slot, MainSlots[slot], 100,
                "Drop", 5d + slot, "Gold", 31d - slot, "EnergyCap", 4d, "MagicCap", 24d));
            candidates.Add(Candidate(100 + slot, MainSlots[slot], 1,
                "Drop", 28d - slot, "Gold", 3d, "EnergyCap", 35d + slot, "MagicCap", 2d));
            candidates.Add(Candidate(110 + slot, MainSlots[slot], 17));
        }
        candidates.Add(Candidate(200, "Weapon", 100, "Drop", 8d, "Gold", 55d, "EnergyCap", 9d, "MagicCap", 40d));
        candidates.Add(Candidate(200, "Weapon", 1, "Drop", 60d, "Gold", 7d, "EnergyCap", 65d, "MagicCap", 3d));
        candidates.Add(Candidate(201, "Weapon", 17, "Drop", 35d, "Gold", 44d, "EnergyCap", 20d, "MagicCap", 50d));
        candidates.Add(Candidate(202, "Weapon", 17, "Drop", 40d, "Gold", 22d, "EnergyCap", 45d, "MagicCap", 30d));
        candidates.Add(Candidate(203, "Weapon", 100, "Power", 100000d));
        for (var id = 0; id < 3; id++)
        {
            candidates.Add(Candidate(300 + id, "Accessory", 100,
                "Drop", 4d + id, "Gold", 36d - id, "EnergyCap", 6d, "MagicCap", 25d + id));
            candidates.Add(Candidate(300 + id, "Accessory", 1,
                "Drop", 30d - id, "Gold", 5d, "EnergyCap", 40d + id, "MagicCap", 3d));
        }
        candidates.Add(Candidate(303, "Accessory", 17, "Drop", 18d, "Gold", 20d, "EnergyCap", 19d, "MagicCap", 15d));
        candidates.Add(Candidate(304, "Accessory", 100, "Power", 100000d));
        foreach (var goal in Goals)
        foreach (var factor in new[] {0f, .5f, 1f})
        foreach (var slots in new[] {0, 1, 2, 4, 10})
        {
            var scenario = goal + "/exhaustive/slots=" + slots + "/factor=" + factor;
            var expected = Exhaustive(goal, candidates, slots, factor);
            var result = Optimize(goal, candidates, slots, factor);
            CheckResult(result, expected, scenario);
            CheckSelection(result, candidates, goal, slots, factor, scenario);
        }
    }

    private static void CheckManyTies()
    {
        foreach (var goal in Goals)
        {
            var candidates = RequiredGear();
            var firstWeapon = candidates[4];
            candidates.Add(Candidate(200, "Weapon", 100, "Power", 100000d));
            var secondWeapon = Candidate(201, "Weapon", 17);
            candidates.Add(secondWeapon);
            var firstCopies = new object[100];
            for (var id = 0; id < firstCopies.Length; id++)
            {
                firstCopies[id] = Candidate(300 + id, "Accessory", 1, goal, 25d, "Power", (double) id);
                candidates.Add(firstCopies[id]);
                candidates.Add(Candidate(300 + id, "Accessory", 100, goal, 25d, "Power", 100000d + id));
            }
            foreach (var factor in new[] {0f, .5f, 1f})
            {
                var scenario = goal + "/100 tied IDs/10 slots/factor=" + factor;
                var result = Optimize(goal, candidates, 10, factor);
                CheckResult(result, ExpectedScore(goal, 350d), scenario);
                CheckSelection(result, candidates, goal, 10, factor, scenario);
                var items = (Array) Get(result, "Items");
                Same(Get(firstWeapon, "Item"), items.GetValue(4), scenario + " first tied mainhand");
                if (factor > 0)
                    Same(Get(secondWeapon, "Item"), items.GetValue(5), scenario + " first other tied weapon ID");
                var start = factor > 0 ? 6 : 5;
                for (var i = 0; i < 10; i++)
                    Same(Get(firstCopies[i], "Item"), items.GetValue(start + i), scenario + " stable tied ID/copy " + i);
            }
        }
    }

    private static void CheckFailures()
    {
        foreach (var goal in Goals)
        {
            CheckFailure(Optimize(goal, Candidates(), 10, 1f), "No owned gear candidates found.", goal + " empty inventory");
            for (var missing = 0; missing < 5; missing++)
            {
                var candidates = RequiredGear();
                candidates.RemoveAt(missing);
                candidates.Add(Candidate(300, "Accessory", 100, goal, 100000d));
                foreach (var factor in new[] {0f, .5f, 1f})
                    CheckFailure(Optimize(goal, candidates, 10, factor),
                        "Missing at least one required gear slot for optimization.",
                        goal + " missing " + (missing == 4 ? "Weapon" : MainSlots[missing]) + "/factor=" + factor);
            }
        }
    }

    private static double Exhaustive(string goal, IList candidates, int slots, float factor)
    {
        // Enumerate raw copies, armor products, ordered weapon pairs, and accessory
        // subsets without any production pruning, weapon-set builder, or scoring.
        var groups = new List<int>[6];
        for (var i = 0; i < groups.Length; i++) groups[i] = new List<int>();
        var values = new double[candidates.Count];
        var ids = new int[candidates.Count];
        var accessoryIds = new HashSet<int>();
        var weaponIds = new HashSet<int>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var slot = Slot(candidates[i]);
            var group = Array.IndexOf(MainSlots, slot);
            if (group < 0) group = slot == "Weapon" ? 4 : 5;
            groups[group].Add(i);
            values[i] = Stat(candidates[i], goal);
            ids[i] = Id(candidates[i]);
            if (group == 4) weaponIds.Add(ids[i]);
            if (group == 5) accessoryIds.Add(ids[i]);
        }
        var picks = Math.Min(slots, accessoryIds.Count);
        var used = new HashSet<int>();
        var best = double.NegativeInfinity;
        Action<int, int, double> accessories = null;
        accessories = (start, remaining, total) =>
        {
            if (remaining == 0)
            {
                best = Math.Max(best, total);
                return;
            }
            for (var i = start; i < groups[5].Count; i++)
            {
                var index = groups[5][i];
                if (!used.Add(ids[index])) continue;
                accessories(i + 1, remaining - 1, total + values[index]);
                used.Remove(ids[index]);
            }
        };
        Action<int, double> armor = null;
        armor = (slot, total) =>
        {
            if (slot < 4)
            {
                foreach (var index in groups[slot]) armor(slot + 1, total + values[index]);
                return;
            }
            foreach (var main in groups[4])
            {
                if (factor <= 0 || weaponIds.Count == 1)
                {
                    accessories(0, picks, total + values[main]);
                    continue;
                }
                foreach (var off in groups[4])
                    if (ids[main] != ids[off])
                        accessories(0, picks, total + values[main] + values[off] * factor);
            }
        };
        armor(0, 100d);
        return ExpectedScore(goal, best);
    }

    private static void CheckResult(object result, double expected, string scenario)
    {
        Equal(true, (bool) Get(result, "Success"), scenario + " succeeds");
        Near(expected, (double) Get(result, "Score"), scenario + " independent score");
        Equal(1L, (long) Get(result, "EvaluatedCombinations"), scenario + " one direct evaluation");
        Equal(0L, (long) Get(result, "VisitedNodes"), scenario + " no search nodes");
        Equal(0L, (long) Get(result, "PrunedBranches"), scenario + " no search pruning");
    }

    private static void CheckSelection(object result, IList candidates, string goal, int slots, float factor, string scenario)
    {
        var items = (Array) Get(result, "Items");
        var resultIds = (int[]) Get(result, "ItemIds");
        Equal(items.Length, resultIds.Length, scenario + " IDs correspond to items");
        var owned = new Dictionary<object, object>();
        var accessoryIds = new HashSet<int>();
        var weaponIds = new HashSet<int>();
        foreach (var candidate in candidates)
        {
            owned.Add(Get(candidate, "Item"), candidate);
            if (Slot(candidate) == "Accessory") accessoryIds.Add(Id(candidate));
            if (Slot(candidate) == "Weapon") weaponIds.Add(Id(candidate));
        }
        var counts = new Dictionary<string, int>();
        var selectedIds = new HashSet<int>();
        var total = 100d;
        var weapons = 0;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items.GetValue(i);
            Equal(true, owned.ContainsKey(item), scenario + " retains owned physical instance " + i);
            var candidate = owned[item];
            var id = Id(candidate);
            Equal(id, resultIds[i], scenario + " item ID " + i);
            Equal(id, (int) Get(Get(item, "equipment"), "id"), scenario + " equipment ID " + i);
            Equal(true, selectedIds.Add(id), scenario + " distinct selected ID " + i);
            var slot = Slot(candidate);
            if (!counts.ContainsKey(slot)) counts[slot] = 0;
            counts[slot]++;
            var weight = slot == "Weapon" && weapons++ > 0 ? factor : 1d;
            total += Stat(candidate, goal) * weight;
        }
        foreach (var slot in MainSlots) Equal(1, counts.ContainsKey(slot) ? counts[slot] : 0, scenario + " fills " + slot);
        var expectedWeapons = factor > 0 && weaponIds.Count > 1 ? 2 : 1;
        Equal(expectedWeapons, weapons, scenario + " feasible weapon count");
        var expectedAccessories = Math.Min(slots, accessoryIds.Count);
        Equal(expectedAccessories, counts.ContainsKey("Accessory") ? counts["Accessory"] : 0, scenario + " distinct accessory capacity");
        Equal(4 + expectedWeapons + expectedAccessories, items.Length, scenario + " complete loadout size");
        Near(ExpectedScore(goal, total), (double) Get(result, "Score"), scenario + " returned instances reproduce score independently");
    }

    private static void CheckFailure(object result, string message, string scenario)
    {
        Equal(false, (bool) Get(result, "Success"), scenario + " fails");
        Equal(message, (string) Get(result, "Message"), scenario + " failure reason");
        Equal(0, ((int[]) Get(result, "ItemIds")).Length, scenario + " no item IDs");
        Equal(0, ((Array) Get(result, "Items")).Length, scenario + " no items");
        Near(0d, (double) Get(result, "Score"), scenario + " no score");
        Equal(0L, (long) Get(result, "EvaluatedCombinations"), scenario + " no complete evaluations");
        Equal(0L, (long) Get(result, "VisitedNodes"), scenario + " no visited nodes");
    }

    private static IList RequiredGear()
    {
        var candidates = Candidates();
        for (var i = 0; i < MainSlots.Length; i++) candidates.Add(Candidate(100 + i, MainSlots[i], 17));
        candidates.Add(Candidate(200, "Weapon", 17));
        return candidates;
    }

    private static object Candidate(int id, string slot, int level, params object[] stats)
    {
        var equipment = FormatterServices.GetUninitializedObject(_equipment);
        Set(equipment, "id", id);
        Set(equipment, "type", Enum.Parse(_part, slot));
        Set(equipment, "level", level);
        var item = FormatterServices.GetUninitializedObject(_item);
        Set(item, "id", id);
        Set(item, "level", level);
        Set(item, "equipment", equipment);
        var candidate = Activator.CreateInstance(_candidate, true);
        Set(candidate, "Item", item);
        var values = (IDictionary) Get(candidate, "Stats");
        for (var i = 0; i < stats.Length; i += 2)
            values.Add(Enum.Parse(_stat, (string) stats[i]), (double) stats[i + 1]);
        return candidate;
    }

    private static object Optimize(string goal, IList candidates, int slots, float factor, string mode = "SqrtPowerToughness")
    {
        return Call("OptimizeCandidates", Enum.Parse(_goal, goal), Enum.Parse(_mode, mode), candidates, slots, factor);
    }

    private static IList Candidates() { return (IList) Activator.CreateInstance(typeof(List<>).MakeGenericType(_candidate)); }
    private static int Id(object candidate) { return (int) Get(Get(candidate, "Item"), "id"); }
    private static string Slot(object candidate) { return Get(Get(Get(candidate, "Item"), "equipment"), "type").ToString(); }
    private static double ExpectedScore(string goal, double total) { return goal == "Drop" || goal == "Gold" ? total / 100d : total; }
    private static double Stat(object candidate, string stat)
    {
        var values = (IDictionary) Get(candidate, "Stats");
        var key = Enum.Parse(_stat, stat);
        return values.Contains(key) ? (double) values[key] : 0d;
    }
    private static object Get(object owner, string name)
    {
        var property = owner.GetType().GetProperty(name, Instance);
        return property != null ? property.GetValue(owner) : owner.GetType().GetField(name, Instance).GetValue(owner);
    }
    private static void Set(object owner, string name, object value)
    {
        var property = owner.GetType().GetProperty(name, Instance);
        if (property != null) property.SetValue(owner, value);
        else owner.GetType().GetField(name, Instance).SetValue(owner, value);
    }
    private static object Call(string method, params object[] args) { return _optimizer.GetMethod(method, Static).Invoke(null, args); }
    private static void Same(object expected, object actual, string scenario) { Equal(true, ReferenceEquals(expected, actual), scenario); }
    private static void Equal<T>(T expected, T actual, string scenario)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception("Gear single-stat " + scenario + ": expected " + expected + ", got " + actual);
    }
    private static void Near(double expected, double actual, string scenario)
    {
        _assertions++;
        if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(expected - actual) > Math.Max(1d, Math.Abs(expected)) * 1e-10)
            throw new Exception("Gear single-stat " + scenario + ": expected approximately " + expected + ", got " + actual);
    }
}
