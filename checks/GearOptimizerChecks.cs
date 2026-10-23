using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

internal static class GearOptimizerChecks
{
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type _optimizer, _candidate, _stat, _spec, _item, _selected, _goal, _fightMode;
    private static int _assertions;

    internal static int Run(Assembly assembly)
    {
        _assertions = 0;
        _optimizer = assembly.GetType("NGUInjector.Managers.GearOptimizer", true);
        _candidate = _optimizer.GetNestedType("GearCandidate", BindingFlags.NonPublic);
        _stat = _optimizer.GetNestedType("StatKey", BindingFlags.NonPublic);
        _selected = _optimizer.GetNestedType("SelectedGear", BindingFlags.NonPublic);
        _goal = assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true);
        _fightMode = assembly.GetType("NGUInjector.Managers.FightScoreMode", true);
        _item = assembly.GetType("NGUInjector.Managers.ih", true);
        var main = assembly.GetType("NGUInjector.Main", true);
        var controllerField = main.GetField("Controller", StaticMembers);
        var characterField = main.GetField("Character", StaticMembers);
        _spec = controllerField.FieldType.Assembly.GetType("specType", true);
        var previousController = controllerField.GetValue(null);
        var previousCharacter = characterField.GetValue(null);
        try
        {
            // Runtime fields are populated below without invoking Unity constructors.
            controllerField.SetValue(null, FormatterServices.GetUninitializedObject(controllerField.FieldType));
            Call("ClearCache");
            CheckNormalization();
            CheckNguRanking();
            CheckCombatPrepScores();
            CheckCache();
            CheckBuildCandidate(assembly, controllerField, characterField);
            try { CheckEquipPreflight(characterField); }
            finally { characterField.SetValue(null, previousCharacter); }
            Console.WriteLine("Gear optimizer checks passed: " + _assertions + " assertions.");
            return _assertions;
        }
        finally
        {
            Call("ClearCache");
            _optimizer.GetField("CachedAccessorySlots", StaticMembers).SetValue(null, 0);
            _optimizer.GetField("CachedWeapon2Unlocked", StaticMembers).SetValue(null, false);
            _optimizer.GetField("CachedWeapon2Factor", StaticMembers).SetValue(null, 0f);
            controllerField.SetValue(null, previousController);
            characterField.SetValue(null, previousCharacter);
        }
    }

    private static void CheckBuildCandidate(Assembly assembly, FieldInfo controllerField, FieldInfo characterField)
    {
        var controller = controllerField.GetValue(null);
        var controllerCharacter = controller.GetType().GetField("character", InstanceMembers);
        var previousControllerCharacter = controllerCharacter.GetValue(controller);
        var previousCharacter = characterField.GetValue(null);
        var character = FormatterServices.GetUninitializedObject(characterField.FieldType);
        var settings = FixtureField(character, "settings");
        var difficultyField = settings.GetType().GetField("rebirthDifficulty", InstanceMembers);
        difficultyField.SetValue(settings, Enum.Parse(difficultyField.FieldType, "normal"));
        SetField(character, "bossID", 100);
        var inventory = FixtureField(character, "inventory");
        SetField(inventory, "disabled", false);
        var noEquipment = FixtureField(FixtureField(character, "challenges"), "noEquipmentChallenge");
        SetField(noEquipment, "inChallenge", false);
        try
        {
            characterField.SetValue(null, character);
            controllerCharacter.SetValue(controller, character);
            Equal(100, (int) character.GetType().GetMethod("effectiveBossID", InstanceMembers).Invoke(character, null),
                "runtime normal effective boss");

            var heart = RuntimeItem(196, 69f, 69f);
            SetSpec(heart, 1, "GoldDropAmount", 100f);
            SetSpec(heart, 2, "NGU2", 456000f);
            var ring = RuntimeItem(138, 0f, 1000f);
            SetSpec(ring, 1, "NGU", 6050f);
            SetSpec(ring, 2, "Wandoos98", 6050f);
            SetSpec(ring, 3, "AdvTraining", 6050f);
            var heartCandidate = Call("BuildCandidate", heart);
            var ringCandidate = Call("BuildCandidate", ring);
            Equal(true, ReferenceEquals(heart, Get(heartCandidate, "Item")), "BuildCandidate retains the item wrapper");
            Equal(true, ReferenceEquals(Get(heart, "equipment"), Equipment(heartCandidate)), "BuildCandidate retains equipment identity");
            Near(69d, Stat(heartCandidate, "Power"), "runtime heart attack");
            Near(69d, Stat(heartCandidate, "Toughness"), "runtime heart defense");
            Near(100d, Stat(heartCandidate, "Gold"), "runtime heart slot-one gold");
            Near(45.6, Stat(heartCandidate, "NguSpeed"), "runtime blue heart NGU2 percentage");
            Equal(4, Stats(heartCandidate).Count, "runtime heart has only its four stats");
            Near(60.5, Stat(ringCandidate, "NguSpeed"), "runtime ring NGU percentage");
            Near(60.5, Stat(ringCandidate, "WandoosSpeed"), "runtime ring slot-two percentage");
            Near(60.5, Stat(ringCandidate, "AdvancedTrainingSpeed"), "runtime ring slot-three percentage");
            Near(1000d, Stat(ringCandidate, "Toughness"), "runtime ring defense");
            Equal(4, Stats(ringCandidate).Count, "runtime zero attack is omitted");
            foreach (var goal in new[] {"EnergyNgu", "MagicNgu", "AllNgu"})
            {
                Near(1.456, Score(goal, heartCandidate), goal + " runtime heart score");
                Near(1.605, Score(goal, ringCandidate), goal + " runtime ring score");
                Equal(true, Score(goal, ringCandidate) > Score(goal, heartCandidate), goal + " runtime ring wins");
                Equal(true, (bool) Call("Dominates", ringCandidate, heartCandidate, Relevant(goal)), goal + " runtime normalized dominance");
            }

            var underBoss = RuntimeItem(100, 101.75f, 205.75f, 200);
            SetSpec(underBoss, 1, "NGU", 101.75f);
            SetSpec(underBoss, 2, "EnergyPower2", 203.75f);
            SetSpec(underBoss, 3, "AllCap", 305.75f);
            var halved = Call("BuildCandidate", underBoss);
            // Fixed expectations verify boss scaling and per-slot flooring before normalization.
            Near(50d, Stat(halved, "Power"), "underboss attack halves and floors");
            Near(102d, Stat(halved, "Toughness"), "underboss defense halves and floors independently");
            Near(.5, Stat(halved, "NguSpeed"), "underboss slot-one NGU halves and floors");
            Near(10.1, Stat(halved, "EnergyPower"), "underboss slot-two power halves and floors");
            Near(.0152, Stat(halved, "EnergyCap"), "underboss slot-three cap halves and floors");
            Near(.0152, Stat(halved, "MagicCap"), "underboss combined cap maps to both resources");
            Equal(6, Stats(halved).Count, "underboss writes only expected stats");

            foreach (var difficulty in new[] {"evil", "sadistic"})
            {
                difficultyField.SetValue(settings, Enum.Parse(difficultyField.FieldType, difficulty));
                Equal(difficulty == "evil" ? 401 : 702,
                    (int) character.GetType().GetMethod("effectiveBossID", InstanceMembers).Invoke(character, null),
                    difficulty + " runtime effective boss offset");
                var full = Call("BuildCandidate", underBoss);
                Near(101d, Stat(full, "Power"), difficulty + " attack scaling caps at one and floors");
                Near(205d, Stat(full, "Toughness"), difficulty + " defense scaling caps at one and floors");
                Near(1.01, Stat(full, "NguSpeed"), difficulty + " NGU scaling caps at one and floors");
                Near(20.3, Stat(full, "EnergyPower"), difficulty + " power scaling caps at one and floors");
                Near(.0305, Stat(full, "EnergyCap"), difficulty + " cap scaling caps at one and floors");
                Near(.0305, Stat(full, "MagicCap"), difficulty + " combined cap remains symmetric");
            }
            difficultyField.SetValue(settings, Enum.Parse(difficultyField.FieldType, "normal"));

            var duplicate = RuntimeItem(100, 0f, 0f);
            SetSpec(duplicate, 1, "NGU", 100f);
            SetSpec(duplicate, 2, "NGU", 200f);
            var summed = Call("BuildCandidate", duplicate);
            Near(3d, Stat(summed, "NguSpeed"), "duplicate NGU slots sum once to three percent");
            Near(1.03, Score("EnergyNgu", summed), "three percent NGU yields a 1.03 score");
            Equal(1, Stats(summed).Count, "duplicate NGU creates one normalized stat");
            SetField(Get(duplicate, "equipment"), "bossRequired", 200);
            SetSpec(duplicate, 1, "NGU", 101.75f);
            SetSpec(duplicate, 2, "NGU", 203.75f);
            SetSpec(duplicate, 3, "NGU", 51.75f);
            Near(1.76, Stat(Call("BuildCandidate", duplicate), "NguSpeed"),
                "three duplicate slots floor separately then sum once");
            SetField(Get(duplicate, "equipment"), "bossRequired", 100);
            SetSpec(duplicate, 1, "NGU", 100f);
            SetSpec(duplicate, 2, "NGU2", 20000f);
            SetSpec(duplicate, 3, "None", 0f);
            Near(3d, Stat(Call("BuildCandidate", duplicate), "NguSpeed"), "mixed NGU tiers normalize separately then accumulate");

            foreach (var blockedByChallenge in new[] {true, false})
            {
                SetField(noEquipment, "inChallenge", blockedByChallenge);
                SetField(inventory, "disabled", !blockedByChallenge);
                var blocked = Call("BuildCandidate", underBoss);
                var scenario = blockedByChallenge ? "no equipment challenge" : "disabled inventory";
                Equal(2, Stats(blocked).Count, scenario + " suppresses all spec stats");
                Near(50d, Stat(blocked, "Power"), scenario + " leaves per-item attack formula intact");
                Near(102d, Stat(blocked, "Toughness"), scenario + " leaves per-item defense formula intact");
                Near(1d, Score("EnergyNgu", blocked), scenario + " has no resource or NGU bonus");
            }
            SetField(noEquipment, "inChallenge", false);
            SetField(inventory, "disabled", false);
            var noneFirst = RuntimeItem(100, 0f, 0f);
            SetSpec(noneFirst, 1, "None", 999f);
            SetSpec(noneFirst, 2, "NGU", 200f);
            SetSpec(noneFirst, 3, "EnergyPower2", 300f);
            var empty = Call("BuildCandidate", noneFirst);
            Equal(0, Stats(empty).Count, "None in slot one suppresses nonempty later specs");
            Near(1d, Score("EnergyNgu", empty), "None slot-one game early return keeps baseline score");
            SetSpec(noneFirst, 1, "EnergyCap", 0f);
            var zeroFirst = Call("BuildCandidate", noneFirst);
            Equal(2, Stats(zeroFirst).Count, "zero-valued non-None slot one allows later specs");
            Near(2d, Stat(zeroFirst, "NguSpeed"), "later NGU survives zero-valued slot one");
            Near(30d, Stat(zeroFirst, "EnergyPower"), "later power survives zero-valued slot one");

            CheckRuntimeSnapshots(assembly, character, controller, inventory);
        }
        finally
        {
            controllerCharacter.SetValue(controller, previousControllerCharacter);
            characterField.SetValue(null, previousCharacter);
        }
    }

    private static void CheckRuntimeSnapshots(Assembly assembly, object character, object controller, object inventory)
    {
        var itemInfo = FixtureField(controller, "itemInfo");
        var namesField = itemInfo.GetType().GetField("itemName", InstanceMembers);
        var names = Array.CreateInstance(namesField.FieldType.GetElementType(), 197);
        names.SetValue("Empty", 0);
        names.SetValue("Check Head", 100);
        names.SetValue("Ring of Utility", 138);
        namesField.SetValue(itemInfo, names);
        var wishes = FixtureField(character, "wishes");
        var wishesField = wishes.GetType().GetField("wishes", InstanceMembers);
        var wishType = wishesField.FieldType.GetGenericArguments()[0];
        var wishList = List(wishType);
        for (var i = 0; i <= 109; i++) wishList.Add(FormatterServices.GetUninitializedObject(wishType));
        wishesField.SetValue(wishes, wishList);
        // Exercise the native unlock getter, not a replacement slot-count formula.
        FixtureField(character, "purchases");
        FixtureField(character, "arbitrary");
        FixtureField(character.GetType().GetField("challenges", InstanceMembers).GetValue(character), "trollChallenge");
        var troll = FixtureField(FixtureField(character, "allChallenges"), "trollChallenge");
        SetField(troll, "character", character);
        SetField(troll, "maxCompletions", 7);
        foreach (var input in new[] {
            new {Owner = FixtureField(FixtureField(character, "adventure"), "itopod"), Field = "perkLevel", Count = 30},
            new {Owner = FixtureField(character, "beastQuest"), Field = "quirkLevel", Count = 19}
        })
        {
            var field = input.Owner.GetType().GetField(input.Field, InstanceMembers);
            var type = field.FieldType.GetGenericArguments()[0];
            var levels = List(type);
            for (var i = 0; i < input.Count; i++) levels.Add(Activator.CreateInstance(type));
            field.SetValue(input.Owner, levels);
        }
        FixtureField(character, "diggers");
        FixtureField(character, "yggdrasil");
        SetField(FixtureField(character, "stats"), "rebirthNumber", 7L);
        SetField(FixtureField(character, "rebirthTime"), "totalseconds", 100d);
        SetField(FixtureField(character, "totalPlaytime"), "totalseconds", 1000d);
        Equal(2, (int) controller.GetType().GetMethod("accessorySpaces", InstanceMembers).Invoke(controller, null),
            "native accessory getter has two unlocked slots");
        Equal(false, (bool) controller.GetType().GetMethod("weapon2Unlocked", InstanceMembers).Invoke(controller, null),
            "native offhand getter remains locked");
        var head = RuntimeItem(100, 0f, 0f);
        var headType = Get(head, "equipment").GetType().GetField("type", InstanceMembers);
        headType.SetValue(Get(head, "equipment"), Enum.Parse(headType.FieldType, "Head"));
        SetField(inventory, "head", Get(head, "equipment"));
        foreach (var slot in new[] {"chest", "legs", "boots", "weapon", "weapon2"})
            FixtureField(inventory, slot);
        var first = Get(RuntimeItem(138, 0f, 0f), "equipment");
        var second = Get(RuntimeItem(138, 0f, 0f), "equipment");
        var accessoriesField = inventory.GetType().GetField("accs", InstanceMembers);
        accessoriesField.SetValue(inventory, List(accessoriesField.FieldType.GetGenericArguments()[0], first, second));
        var converted = (IEnumerable) assembly.GetType("NGUInjector.Extensions", true)
            .GetMethod("GetConvertedEquips", StaticMembers).Invoke(null, new[] {inventory});
        var equipped = new List<object>();
        foreach (var item in converted) equipped.Add(item);
        Equal(3, equipped.Count, "runtime converted equips omit empty slots and locked offhand");
        Equal(-1, (int) Get(equipped[0], "slot"), "runtime head slot encoding");
        Equal("Check Head", (string) Get(equipped[0], "name"), "runtime wrapper uses itemInfo name");
        Equal(true, ReferenceEquals(Get(head, "equipment"), Get(equipped[0], "equipment")), "runtime head identity");
        Equal(10000, (int) Get(equipped[1], "slot"), "runtime first accessory slot encoding");
        Equal(10001, (int) Get(equipped[2], "slot"), "runtime second accessory slot encoding");
        Equal(138, (int) Get(equipped[1], "id"), "runtime first same-ID accessory");
        Equal(138, (int) Get(equipped[2], "id"), "runtime second same-ID accessory");
        Equal(true, ReferenceEquals(first, Get(equipped[1], "equipment")), "runtime first accessory identity");
        Equal(true, ReferenceEquals(second, Get(equipped[2], "equipment")), "runtime second accessory identity");

        var loadouts = assembly.GetType("NGUInjector.Managers.LoadoutManager", true);
        var capture = loadouts.GetMethod("CaptureExactLoadout", StaticMembers);
        var epochType = loadouts.GetNestedType("SnapshotEpoch", BindingFlags.NonPublic);
        var savedEpoch = Activator.CreateInstance(epochType, true);
        var saved = (Array) capture.Invoke(null, null);
            Equal(7, saved.Length, "runtime saved exact map includes five fixed slots and two accessories");
            Equal(true, (bool) epochType.GetMethod("Matches", InstanceMembers).Invoke(savedEpoch, null),
                "exact capture belongs to the actual fixture epoch");
            var replacement = Get(RuntimeItem(138, 0f, 0f), "equipment");
            ((IList) accessoriesField.GetValue(inventory))[0] = replacement;
            var temp = (Array) capture.Invoke(null, null);
            Equal(7, temp.Length, "runtime temporary exact map includes empty fixed slots");
            Equal(false, ReferenceEquals(saved, temp), "runtime saved and temp arrays are independent");
            var savedMap = saved.Cast<object>().ToDictionary(item => (int) Get(item, "slot"));
            var tempMap = temp.Cast<object>().ToDictionary(item => (int) Get(item, "slot"));
            foreach (var slot in new[] {-1, -2, -3, -4, -5, 10000, 10001})
            {
                Equal(true, savedMap.ContainsKey(slot) && tempMap.ContainsKey(slot), "both exact snapshots retain destination " + slot);
                Equal(false, ReferenceEquals(savedMap[slot], tempMap[slot]), "exact snapshots create independent wrappers at " + slot);
            }
            foreach (var slot in new[] {-2, -3, -4, -5})
            {
                Equal(0, (int) Get(savedMap[slot], "id"), "saved snapshot explicitly retains empty destination " + slot);
                Equal(0, (int) Get(tempMap[slot], "id"), "temporary snapshot explicitly retains empty destination " + slot);
                Equal(true, ReferenceEquals(Get(savedMap[slot], "equipment"), Get(tempMap[slot], "equipment")),
                    "empty equipment identity survives both captures at " + slot);
            }
            Equal(false, savedMap.ContainsKey(-6) || tempMap.ContainsKey(-6), "locked offhand is absent from both exact maps");
            Equal(true, ReferenceEquals(Get(head, "equipment"), Get(savedMap[-1], "equipment")), "saved exact map retains head identity");
            Equal(true, ReferenceEquals(first, Get(savedMap[10000], "equipment")), "saved snapshot retains replaced same-ID identity");
            Equal(true, ReferenceEquals(replacement, Get(tempMap[10000], "equipment")), "temp snapshot captures replacement same-ID identity");
            Equal(true, ReferenceEquals(second, Get(savedMap[10001], "equipment")), "saved snapshot retains second same-ID identity");
            Equal(true, ReferenceEquals(second, Get(tempMap[10001], "equipment")), "temp snapshot retains second same-ID identity");
            var tempEpoch = Activator.CreateInstance(epochType, true);
            Equal(false, ReferenceEquals(savedEpoch, tempEpoch), "independent captures own independent epoch receipts");
            Equal(true, (bool) tempEpoch.GetType().GetMethod("Matches", InstanceMembers).Invoke(tempEpoch, null),
                "temporary snapshot captures the actual fixture epoch");
    }

    private static object FixtureField(object owner, string name)
    {
        var field = owner.GetType().GetField(name, InstanceMembers);
        var fixture = FormatterServices.GetUninitializedObject(field.FieldType);
        field.SetValue(owner, fixture);
        return fixture;
    }

    private static object RuntimeItem(int id, float attack, float defense, int bossRequired = 100)
    {
        var item = Get(Candidate(id), "Item");
        var equipment = Get(item, "equipment");
        SetField(equipment, "id", id);
        SetField(equipment, "curAttack", attack);
        SetField(equipment, "curDefense", defense);
        SetField(equipment, "bossRequired", bossRequired);
        var typeField = equipment.GetType().GetField("type", InstanceMembers);
        typeField.SetValue(equipment, Enum.Parse(typeField.FieldType, "Accessory"));
        return item;
    }

    private static void SetSpec(object item, int slot, string type, float value)
    {
        var equipment = Get(item, "equipment");
        SetField(equipment, "spec" + slot + "Type", Enum.Parse(_spec, type));
        SetField(equipment, "spec" + slot + "Cur", value);
    }

    private static void SetField(object value, string field, object data) { value.GetType().GetField(field, InstanceMembers).SetValue(value, data); }

    private static void CheckNormalization()
    {
        // Expected percentages are fixed game-scale constants, not calculated with production helpers.
        var cases = new[]
        {
            new {Spec = "EnergyPower", Raw = 12.5f, Percent = 12.5, Stats = "EnergyPower"},
            new {Spec = "EnergyPower2", Raw = 125f, Percent = 12.5, Stats = "EnergyPower"},
            new {Spec = "EnergyPower3", Raw = 12500f, Percent = 12.5, Stats = "EnergyPower"},
            new {Spec = "MagicPower", Raw = 12.5f, Percent = 12.5, Stats = "MagicPower"},
            new {Spec = "MagicPower2", Raw = 125f, Percent = 12.5, Stats = "MagicPower"},
            new {Spec = "MagicPower3", Raw = 12500f, Percent = 12.5, Stats = "MagicPower"},
            new {Spec = "AllPower", Raw = 12500f, Percent = 12.5, Stats = "EnergyPower,MagicPower"},
            new {Spec = "EnergyCap", Raw = 1250f, Percent = 12.5, Stats = "EnergyCap"},
            new {Spec = "EnergyCap3", Raw = 125000f, Percent = 12.5, Stats = "EnergyCap"},
            new {Spec = "MagicCap", Raw = 1250f, Percent = 12.5, Stats = "MagicCap"},
            new {Spec = "MagicCap3", Raw = 125000f, Percent = 12.5, Stats = "MagicCap"},
            new {Spec = "AllCap", Raw = 125000f, Percent = 12.5, Stats = "EnergyCap,MagicCap"},
            new {Spec = "EnergyPerBar", Raw = 12.5f, Percent = 12.5, Stats = "EnergyBars"},
            new {Spec = "EnergyPerBar2", Raw = 125f, Percent = 12.5, Stats = "EnergyBars"},
            new {Spec = "EnergyPerBar3", Raw = 12500f, Percent = 12.5, Stats = "EnergyBars"},
            new {Spec = "MagicPerBar", Raw = 12.5f, Percent = 12.5, Stats = "MagicBars"},
            new {Spec = "MagicPerBar2", Raw = 125f, Percent = 12.5, Stats = "MagicBars"},
            new {Spec = "MagicPerBar3", Raw = 12500f, Percent = 12.5, Stats = "MagicBars"},
            new {Spec = "AllPerBar", Raw = 12500f, Percent = 12.5, Stats = "EnergyBars,MagicBars"},
            new {Spec = "NGU2", Raw = 456000f, Percent = 45.6, Stats = "NguSpeed"},
            new {Spec = "NGU", Raw = 6050f, Percent = 60.5, Stats = "NguSpeed"},
            new {Spec = "Looting", Raw = 125f, Percent = 12.5, Stats = "Drop"},
            new {Spec = "Looting2", Raw = 125000f, Percent = 12.5, Stats = "Drop"},
            new {Spec = "GoldDropAmount", Raw = 12.5f, Percent = 12.5, Stats = "Gold"},
            new {Spec = "GoldDrop2", Raw = 12500f, Percent = 12.5, Stats = "Gold"},
            new {Spec = "Wandoos98", Raw = 1250f, Percent = 12.5, Stats = "WandoosSpeed"},
            new {Spec = "Wandoos2", Raw = 125000f, Percent = 12.5, Stats = "WandoosSpeed"},
            new {Spec = "AdvTraining", Raw = 1250f, Percent = 12.5, Stats = "AdvancedTrainingSpeed"},
            new {Spec = "AdvTraining2", Raw = 125000f, Percent = 12.5, Stats = "AdvancedTrainingSpeed"},
            new {Spec = "Cooldown", Raw = 1250f, Percent = 12.5, Stats = "MoveCooldown"},
            new {Spec = "Res3Power", Raw = 12500000f, Percent = 12.5, Stats = "Res3Power"},
            new {Spec = "Res3Cap", Raw = 125000000f, Percent = 12.5, Stats = "Res3Cap"},
            new {Spec = "Beards", Raw = 12500f, Percent = 12.5, Stats = "BeardSpeed"},
            new {Spec = "Beards2", Raw = 125000f, Percent = 12.5, Stats = "BeardSpeed"},
            new {Spec = "Augs", Raw = 125000f, Percent = 12.5, Stats = "AugmentSpeed"},
            new {Spec = "HackSpeed", Raw = 125000000f, Percent = 12.5, Stats = "HackSpeed"},
            new {Spec = "WishSpeed", Raw = 125000000f, Percent = 12.5, Stats = "WishSpeed"}
        };
        foreach (var test in cases)
        {
            var candidate = Candidate();
            AddSpec(candidate, test.Spec, test.Raw);
            var stats = test.Stats.Split(',');
            Equal(stats.Length, Stats(candidate).Count, test.Spec + " writes only mapped stats");
            foreach (var stat in stats)
                Near(test.Percent, Stat(candidate, stat), test.Spec + " normalized " + stat);
            var zero = Candidate();
            AddSpec(zero, test.Spec, 0f);
            Equal(0, Stats(zero).Count, test.Spec + " zero does not add a stat");
        }
        foreach (var spec in new[] {"None", "QuestDrop", "GoldDropRNG", "Res3Bar"})
        {
            var ignored = Candidate();
            AddSpec(ignored, spec, 1000000f);
            Equal(0, Stats(ignored).Count, spec + " excluded from optimizer stats");
            Near(1d, Score("Drop", ignored), spec + " does not improve Drop");
            Near(1d, Score("Gold", ignored), spec + " does not improve Gold");
        }
        var repeated = Candidate();
        AddSpec(repeated, "NGU", 6050f);
        AddSpec(repeated, "NGU", 6050f);
        AddSpec(repeated, "NGU2", 456000f);
        Equal(1, Stats(repeated).Count, "duplicate and mixed-tier NGU share one stat");
        Near(166.6, Stat(repeated, "NguSpeed"), "duplicate specs accumulate normalized bonuses");
        var combined = Candidate();
        AddSpec(combined, "AllPower", 12500f);
        AddSpec(combined, "EnergyPower2", 125f);
        Near(25d, Stat(combined, "EnergyPower"), "combined and individual specs accumulate");
        Near(12.5, Stat(combined, "MagicPower"), "individual energy spec does not affect magic");
        var drops = Candidate();
        AddSpec(drops, "Looting2", 125000f);
        AddSpec(drops, "QuestDrop", 1000000f);
        Near(1.125, Score("Drop", drops), "Drop uses Looting2 but not QuestDrop");
        var gold = Candidate();
        AddSpec(gold, "GoldDrop2", 12500f);
        AddSpec(gold, "GoldDropRNG", 1000000f);
        Near(1.125, Score("Gold", gold), "Gold uses amount but not RNG");
    }

    private static void CheckNguRanking()
    {
        var highRaw = Candidate(196);
        AddSpec(highRaw, "NGU2", 456000f);
        AddSpec(highRaw, "EnergyCap3", 200000f);
        AddSpec(highRaw, "MagicCap3", 300000f);
        AddSpec(highRaw, "EnergyPower3", 40000f);
        AddSpec(highRaw, "MagicPower3", 50000f);
        var ring = Candidate(138);
        AddSpec(ring, "NGU", 6050f);
        AddSpec(ring, "EnergyCap", 2000f);
        AddSpec(ring, "MagicCap", 3000f);
        AddSpec(ring, "EnergyPower2", 400f);
        AddSpec(ring, "MagicPower2", 500f);
        var goals = new[] {"EnergyNgu", "MagicNgu", "AllNgu"};
        var resourceScores = new[] {1.2 * 1.4, 1.3 * 1.5, Math.Sqrt(1.2 * 1.4 * 1.3 * 1.5)};
        for (var i = 0; i < goals.Length; i++)
        {
            var goal = goals[i];
            var relevant = Relevant(goal);
            Near(1d, Score(goal), goal + " empty selection baseline");
            Near(resourceScores[i] * 1.456, Score(goal, highRaw), goal + " NGU2 score");
            Near(resourceScores[i] * 1.605, Score(goal, ring), goal + " ring score");
            Equal(true, Score(goal, ring) > Score(goal, highRaw), goal + " lower raw NGU ring wins");
            Equal(true, (bool) Call("Dominates", ring, highRaw, relevant), goal + " normalized dominance");
            Equal(false, (bool) Call("Dominates", highRaw, ring, relevant), goal + " raw values cannot reverse dominance");
            Equal(false, (bool) Call("Dominates", ring, ring, relevant), goal + " equality is not strict dominance");
            foreach (var order in new[] {new[] {highRaw, ring}, new[] {ring, highRaw}})
            {
                var survivors = (IList) Call("RemoveDominated", List(_candidate, order), relevant, 1);
                Equal(1, survivors.Count, goal + " removes dominated gear regardless of order");
                Equal(true, ReferenceEquals(ring, survivors[0]), goal + " keeps the ring instance");
            }
            Equal(2, ((IList) Call("RemoveDominated", List(_candidate, highRaw, ring), relevant, 2)).Count,
                goal + " two-slot cutoff preserves second-best gear");
            var sameId = Candidate(138);
            AddSpec(sameId, "NGU", 100f);
            Equal(1, ((IList) Call("RemoveDominated", List(_candidate, sameId, ring), relevant, 2)).Count,
                goal + " copies of one ID cannot occupy both selectable slots");
            var tradeoff = Candidate();
            AddSpec(tradeoff, "AllCap", 1000000f);
            Equal(false, (bool) Call("Dominates", ring, tradeoff, relevant), goal + " higher cap is a tradeoff");
            Equal(false, (bool) Call("Dominates", tradeoff, ring, relevant), goal + " cap alone does not dominate power and NGU");
        }
        var halfSelection = Activator.CreateInstance(_selected, true);
        Set(halfSelection, "Candidate", ring);
        Set(halfSelection, "Factor", .5d);
        Near(1.1 * 1.2 * 1.3025, (double) Call("ScoreItems", Enum.Parse(_goal, "EnergyNgu"),
            Enum.Parse(_fightMode, "SqrtPowerToughness"), Relevant("EnergyNgu"), List(_selected, halfSelection)),
            "offhand factor scales normalized bonuses, not the baseline");
        Near(1.4 * 1.8 * 2.210, Score("EnergyNgu", ring, ring), "scores sum bonuses before multiplying stats");
    }

    private static void CheckCombatPrepScores()
    {
        var power = Candidate(100);
        AddSpec(power, "EnergyPower", 300f);
        AddSpec(power, "MagicPower", 800f);
        Near(2, Score("AdvancedTraining", power), "AT uses square-root energy power");
        Near(1, Score("Wandoos", power), "Wandoos excludes both resource powers");
        var cap = Candidate(101);
        AddSpec(cap, "EnergyCap", 20000f);
        AddSpec(cap, "MagicCap", 80000f);
        AddSpec(cap, "AdvTraining", 10000f);
        AddSpec(cap, "Wandoos98", 10000f);
        Near(6, Score("AdvancedTraining", cap), "AT cap and speed remain linear");
        Near(6 * Math.Sqrt(3), Score("Wandoos", cap), "Wandoos geometric caps and linear speed");
        Equal(3, ((Array) Relevant("Wandoos")).Length, "Wandoos dominance and bounds exclude resource powers");
    }

    private static void CheckCache()
    {
        var first = Candidate(100);
        var second = Candidate(100);
        AddSpec(first, "NGU", 6050f);
        AddSpec(second, "NGU2", 456000f);
        Equal(false, CacheMatches(List(_candidate, first, second)), "cleared cache never matches");
        var cacheField = _optimizer.GetField("CachedCandidates", StaticMembers);
        var snapshot = (IDictionary) Activator.CreateInstance(cacheField.FieldType);
        var firstSnapshot = CopyCandidate(first);
        snapshot.Add(Equipment(first), firstSnapshot);
        snapshot.Add(Equipment(second), CopyCandidate(second));
        cacheField.SetValue(null, snapshot);
        _optimizer.GetField("CachedAccessorySlots", StaticMembers).SetValue(null, 2);
        _optimizer.GetField("CachedWeapon2Unlocked", StaticMembers).SetValue(null, true);
        _optimizer.GetField("CachedWeapon2Factor", StaticMembers).SetValue(null, .5f);
        Equal(true, CacheMatches(List(_candidate, first, second)), "same-ID distinct instances match cache");
        Equal(true, CacheMatches(List(_candidate, second, first)), "candidate identity order is irrelevant");
        Equal(true, CacheMatches(List(_candidate, CopyCandidate(second), CopyCandidate(first))),
            "new wrappers with same equipment identities and stats match");
        Equal(false, CacheMatches(List(_candidate, first)), "removed candidate invalidates cache");
        var replacement = Candidate(100);
        AddSpec(replacement, "NGU2", 456000f);
        Equal(false, CacheMatches(List(_candidate, first, replacement)), "same-ID same-stat replacement invalidates cache");
        var changed = CopyCandidate(first);
        AddSpec(changed, "NGU", 100f);
        Near(60.5, Stat(firstSnapshot, "NguSpeed"), "cached candidate stats remain an independent snapshot");
        Equal(false, CacheMatches(List(_candidate, changed, second)), "changed normalized stat invalidates cache");
        changed = CopyCandidate(first);
        AddSpec(changed, "Cooldown", 1000f);
        Equal(false, CacheMatches(List(_candidate, changed, second)), "added stat invalidates cache");
        changed = CopyCandidate(first);
        Stats(changed).Clear();
        Equal(false, CacheMatches(List(_candidate, changed, second)), "removed stat invalidates cache");
        changed = CopyCandidate(first);
        Stats(changed).Clear();
        AddSpec(changed, "Wandoos98", 6050f);
        Equal(false, CacheMatches(List(_candidate, changed, second)), "different stat key with same count and value invalidates cache");
        changed = CopyCandidate(first);
        Set(Get(changed, "Item"), "id", 101);
        Equal(false, CacheMatches(List(_candidate, changed, second)), "changed item ID invalidates cache");
        var candidates = List(_candidate, first, second);
        Equal(false, CacheMatches(candidates, 1), "accessory slot decrease invalidates cache");
        Equal(false, CacheMatches(candidates, 3), "accessory slot increase invalidates cache");
        Equal(false, CacheMatches(candidates, 2, false), "offhand unlock change invalidates cache");
        Equal(false, CacheMatches(candidates, 2, true, .6f), "offhand factor change invalidates cache");
        Equal(true, CacheMatches(candidates), "invalid comparisons do not mutate the cached snapshot");
        Call("ClearCache");
        Equal(false, CacheMatches(candidates), "ClearCache invalidates a populated snapshot");
    }

    private static void CheckEquipPreflight(FieldInfo characterField)
    {
        Equal("Cannot equip an empty loadout.", TypedEquipFailure(null), "null loadout preflight");
        Equal("Cannot equip an empty loadout.", TypedEquipFailure(Array.CreateInstance(_item, 0)),
            "empty loadout preflight");
        var character = FormatterServices.GetUninitializedObject(characterField.FieldType);
        var inventoryField = character.GetType().GetField("inventory", InstanceMembers);
        var inventory = FormatterServices.GetUninitializedObject(inventoryField.FieldType);
        inventoryField.SetValue(character, inventory);
        characterField.SetValue(null, character);
        var gear = Array.CreateInstance(_item, 1);
        Equal("Loadout contains missing or duplicate equipment instances.", TypedEquipFailure(gear),
            "null item rejected before game actions");
        gear.SetValue(FormatterServices.GetUninitializedObject(_item), 0);
        Equal("Loadout contains missing or duplicate equipment instances.", TypedEquipFailure(gear),
            "missing equipment reference rejected before game actions");
        var head = Candidate(100);
        var equipment = Equipment(head);
        var partField = equipment.GetType().GetField("type", InstanceMembers);
        partField.SetValue(equipment, Enum.Parse(partField.FieldType, "Head"));
        inventory.GetType().GetField("head", InstanceMembers).SetValue(inventory, equipment);
        inventory.GetType().GetField("item1", InstanceMembers).SetValue(inventory, 123);
        inventory.GetType().GetField("item2", InstanceMembers).SetValue(inventory, 456);
        gear = Array.CreateInstance(_item, 2);
        gear.SetValue(Get(head, "Item"), 0);
        gear.SetValue(Get(CopyCandidate(head), "Item"), 1);
        Equal("Loadout contains missing or duplicate equipment instances.", TypedEquipFailure(gear),
            "distinct wrappers of one equipped instance rejected before resources or swaps");
        Equal(123, (int) inventory.GetType().GetField("item1", InstanceMembers).GetValue(inventory), "preflight preserves destination slot");
        Equal(456, (int) inventory.GetType().GetField("item2", InstanceMembers).GetValue(inventory), "preflight preserves source slot");
        Equal(true, ReferenceEquals(equipment, inventory.GetType().GetField("head", InstanceMembers).GetValue(inventory)),
            "preflight preserves equipped identity");
        // Missing owned items and snapshot saves require wishes/itemInfo; successful swaps require live Unity.
    }

    private static string TypedEquipFailure(Array gear)
    {
        var footprint = Enum.ToObject(_optimizer.Assembly.GetType("NGUInjector.Managers.GearRecallFootprint", true), 0);
        var result = Call("EquipGearObserved", gear, footprint, false, null, null);
        return (string)result.GetType().GetProperty("LegacyFailure", InstanceMembers).GetValue(result);
    }

    private static object Candidate(int id = 100)
    {
        var candidate = Activator.CreateInstance(_candidate, true);
        var item = FormatterServices.GetUninitializedObject(_item);
        var equipmentType = _item.GetProperty("equipment", InstanceMembers).PropertyType;
        Set(item, "equipment", FormatterServices.GetUninitializedObject(equipmentType));
        Set(item, "id", id);
        Set(candidate, "Item", item);
        return candidate;
    }

    private static object CopyCandidate(object source)
    {
        var copy = Candidate((int) Get(Get(source, "Item"), "id"));
        Set(Get(copy, "Item"), "equipment", Equipment(source));
        foreach (DictionaryEntry stat in Stats(source))
            Stats(copy).Add(stat.Key, stat.Value);
        return copy;
    }

    private static void AddSpec(object candidate, string spec, float value)
    {
        Call("AddSpec", candidate, Enum.Parse(_spec, spec), value);
    }

    private static IDictionary Stats(object candidate) { return (IDictionary) Get(candidate, "Stats"); }
    private static object Equipment(object candidate) { return Get(Get(candidate, "Item"), "equipment"); }
    private static double Stat(object candidate, string stat) { return (double) Stats(candidate)[Enum.Parse(_stat, stat)]; }
    private static object Relevant(string goal) { return Call("GetRelevantStats", Enum.Parse(_goal, goal)); }

    private static double Score(string goal, params object[] candidates)
    {
        var selected = List(_selected);
        foreach (var candidate in candidates)
        {
            var gear = Activator.CreateInstance(_selected, true);
            Set(gear, "Candidate", candidate);
            selected.Add(gear);
        }
        return (double) Call("ScoreItems", Enum.Parse(_goal, goal), Enum.Parse(_fightMode, "SqrtPowerToughness"), Relevant(goal), selected);
    }

    private static bool CacheMatches(IList candidates, int slots = 2, bool offhand = true, float factor = .5f)
    {
        return (bool) Call("CacheMatches", candidates, slots, offhand, factor);
    }

    private static IList List(Type type, params object[] items)
    {
        var list = (IList) Activator.CreateInstance(typeof(List<>).MakeGenericType(type));
        foreach (var item in items) list.Add(item);
        return list;
    }

    private static object Get(object value, string property) { return value.GetType().GetProperty(property, InstanceMembers).GetValue(value); }
    private static void Set(object value, string property, object data) { value.GetType().GetProperty(property, InstanceMembers).SetValue(value, data); }
    private static object Call(string method, params object[] args) { return _optimizer.GetMethod(method, StaticMembers).Invoke(null, args); }

    private static void Equal<T>(T expected, T actual, string scenario)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception("Gear optimizer " + scenario + ": expected " + expected + ", got " + actual);
    }

    private static void Near(double expected, double actual, string scenario)
    {
        _assertions++;
        if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(expected - actual) > Math.Max(1d, Math.Abs(expected)) * 0.000001d)
            throw new Exception("Gear optimizer " + scenario + ": expected approximately " + expected + ", got " + actual);
    }
}
