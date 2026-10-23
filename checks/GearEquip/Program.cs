using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

// The injector is loaded unchanged. Native bonus conversions, Character capacity getters,
// controller swap guards/rollback and Inventory swaps remain real. Only rendering, slot
// availability, external progression multipliers and recall boundaries are substituted.
public static partial class GearResourceChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Assembly Game, Plugin;
    private static Type MainType, Optimizer, ItemType;
    private static object Character, Controller;
    private static int Assertions, Swaps, Refreshes;
    private static bool DragDuringPreflight;
    private static readonly int[] Recalls = new int[3];
    private static readonly List<Tuple<object, string>> Resources = new List<Tuple<object, string>>();

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Pass NGUInjector.dll and the game's Managed directory.");
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
                var name = new AssemblyName(request.Name).Name;
                if (name == "Assembly-CSharp") return Game;
                var path = Path.Combine(args[1], name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Game = LoadGame(Path.Combine(args[1], "Assembly-CSharp.dll"));
            Plugin = Assembly.Load(File.ReadAllBytes(args[0]));
            Console.WriteLine("Injector MVID=" + Plugin.ManifestModule.ModuleVersionId);
            MainType = Plugin.GetType("NGUInjector.Main", true);
            Optimizer = Plugin.GetType("NGUInjector.Managers.GearOptimizer", true);
            ItemType = Plugin.GetType("NGUInjector.Managers.ih", true);
            CheckSafeSwap();
            CheckDragging();
            CheckSafeCapacityBoundary();
            CheckCapacityReductions();
            CheckCurrentCapacity();
            CheckNoOpAndLegacy();
            CheckUnmanagedCapacity();
            CheckOptimizerAndLock();
            CheckTypedActuator();
            Console.WriteLine("Resource-preserving actuator checks passed: " + Assertions + " assertions.");
            Assertions += GearEquipChecks.Run(Plugin);
            Assertions += GearOptimizerChecks.Run(Plugin);
            Assertions += GearSearchChecks.Run(Plugin);
            Assertions += GearSingleStatChecks.Run(Plugin);
            Console.WriteLine("PASS: " + Assertions + " compiled gear assertions; no live game/save access.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static Assembly LoadGame(string path)
    {
        using (var game = AssemblyDefinition.ReadAssembly(path))
        using (var bytes = new MemoryStream())
        {
            var swaps = 0; var recalls = 0;
            foreach (var type in game.MainModule.Types)
            foreach (var method in type.Methods.Where(x => x.HasBody))
            {
                if (new[] { "ItopodPerkController", "BeastQuestPerkController", "WishesController" }.Contains(type.Name)
                    && new[] { "totalEnergyCapBonus", "totalMagicCapBonus", "totalRes3CapBonus" }.Contains(method.Name)
                    || type.Name == "AllArbitrary" && method.Name == "potionModifier") Constant(method, 1);
                // Control generation input only; native over-cap total/idle debits stay real.
                if (type.Name == "Character" && new[] { "totalEnergyBar", "totalMagicBar", "totalMagicSpeed" }.Contains(method.Name))
                    Constant(method, method.Name == "totalMagicSpeed" ? 50 : 1);
                if (type.Name == "Energy" && new[] { "updateEnergyText", "updateSlider" }.Contains(method.Name)
                    || type.Name == "MagicDisplay" && new[] { "updateMagicText", "updateSlider" }.Contains(method.Name)) Constant(method, 0);
                if (type.Name == "InventoryController")
                {
                    if (new[] { "curSpaces", "accessorySpaces", "weapon2Unlocked", "weapon2Factor" }.Contains(method.Name))
                    {
                        if (method.Name == "curSpaces") Boundary(game.MainModule, method, "FixtureInventorySpaces");
                        else Constant(method, method.Name == "accessorySpaces" ? 2 : 0);
                    }
                    if (new[] { "updateHead", "updateChest", "updateLegs", "updateBoots", "updateWeapon", "updateWeapon2", "updateBonuses" }.Contains(method.Name))
                        Boundary(game.MainModule, method, "RefreshBonuses");
                    if (method.Name == "updateInventory") Boundary(game.MainModule, method, "RefreshInventory");
                    if (new[] { "swapHead", "swapChest", "swapLegs", "swapBoots", "swapWeapon", "swapWeapon2", "swapAcc" }.Contains(method.Name)
                        && method.Parameters.Count == 0)
                    {
                        // Observe, do not replace the controller's native capacity checks.
                        method.Body.GetILProcessor().InsertBefore(method.Body.Instructions[0], Instruction.Create(OpCodes.Call,
                            game.MainModule.ImportReference(typeof(GearResourceChecks).GetMethod("CountSwap"))));
                        swaps++;
                    }
                }
                if (type.Name == "Character" && new[] { "removeMostEnergy", "removeMostMagic", "removeAllRes3" }.Contains(method.Name))
                {
                    Boundary(game.MainModule, method, method.Name == "removeMostEnergy" ? "RecallEnergy" : method.Name == "removeMostMagic" ? "RecallMagic" : "RecallRes3");
                    recalls++;
                }
            }
            Equal(7, swaps, "seven native controller swap paths observed");
            Equal(3, recalls, "all E/M/R3 recall boundaries observed");
            game.Write(bytes);
            return Assembly.Load(bytes.ToArray());
        }
    }

    private static void Constant(MethodDefinition method, int value)
    {
        method.Body = new Mono.Cecil.Cil.MethodBody(method);
        var il = method.Body.GetILProcessor();
            if (method.ReturnType.FullName == "System.Single") il.Emit(OpCodes.Ldc_R4, (float)value);
            else if (method.ReturnType.FullName == "System.Int64") il.Emit(OpCodes.Ldc_I8, (long)value);
        else if (method.ReturnType.FullName == "System.Int32" || method.ReturnType.FullName == "System.Boolean") il.Emit(OpCodes.Ldc_I4, value);
        else if (method.ReturnType.FullName != "System.Void") throw new Exception("Unexpected fixture signature: " + method.FullName);
        il.Emit(OpCodes.Ret);
    }

    private static void Boundary(ModuleDefinition module, MethodDefinition method, string name)
    {
        method.Body = new Mono.Cecil.Cil.MethodBody(method);
        method.Body.GetILProcessor().Emit(OpCodes.Call, module.ImportReference(typeof(GearResourceChecks).GetMethod(name)));
        method.Body.GetILProcessor().Emit(OpCodes.Ret);
    }

    public static void CountSwap() { Swaps++; SwapStarted?.Invoke(); }
    public static void RefreshInventory() { InventoryRefreshed?.Invoke(); }
    public static void RecallEnergy()
    {
        Recalls[0]++;
        var total = (long)Get(Character, "idleEnergy");
        var at = (long[])Get(Get(Character, "advancedTraining"), "energy");
        for (var i = 0; i < at.Length; i++) { total += at[i]; at[i] = 0; }
        foreach (var aug in (IEnumerable)Get(Get(Character, "augments"), "augs"))
            foreach (var name in new[] { "augEnergy", "upgradeEnergy" }) { total += (long)Get(aug, name); Set(aug, name, 0L); }
        foreach (var owner in ((IEnumerable)Get(Get(Character, "NGU"), "skills")).Cast<object>()
            .Concat(((IEnumerable)Get(Get(Character, "wishes"), "wishes")).Cast<object>()))
        { total += (long)Get(owner, "energy"); Set(owner, "energy", 0L); }
        var machine = Get(Character, "machine"); total += (long)Get(machine, "speedEnergy"); Set(machine, "speedEnergy", 0L);
        var w = Get(Character, "wandoos98"); total += (long)Get(w, "wandoosEnergy"); Set(w, "wandoosEnergy", 0L);
        Set(Character, "idleEnergy", total);
        RecallCompleted?.Invoke(0);
    }
    public static void RecallMagic()
    {
        Recalls[1]++;
        var magic = Get(Character, "magic"); var total = (long)Get(magic, "idleMagic");
        foreach (var owner in ((IEnumerable)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>()
            .Concat(((IEnumerable)Get(Get(Character, "bloodMagic"), "ritual")).Cast<object>())
            .Concat(((IEnumerable)Get(Get(Character, "wishes"), "wishes")).Cast<object>()))
        { total += (long)Get(owner, "magic"); Set(owner, "magic", 0L); }
        var machine = Get(Character, "machine"); total += (long)Get(machine, "goldMultiMagic"); Set(machine, "goldMultiMagic", 0L);
        var w = Get(Character, "wandoos98"); total += (long)Get(w, "wandoosMagic"); Set(w, "wandoosMagic", 0L);
        Set(magic, "idleMagic", total);
        RecallCompleted?.Invoke(1);
    }
    public static void RecallRes3()
    {
        Recalls[2]++;
        var res3 = Get(Character, "res3"); var total = (long)Get(res3, "idleRes3");
        foreach (var owner in ((IEnumerable)Get(Get(Character, "hacks"), "hacks")).Cast<object>()
            .Concat(((IEnumerable)Get(Get(Character, "wishes"), "wishes")).Cast<object>()))
        { total += (long)Get(owner, "res3"); Set(owner, "res3", 0L); }
        Set(res3, "idleRes3", total);
        RecallCompleted?.Invoke(2);
    }
    public static int FixtureInventorySpaces()
    {
        if (DragDuringPreflight) { DragDuringPreflight = false; Set(Controller, "midDrag", true); }
        return 10;
    }
    public static void RefreshBonuses()
    {
        Refreshes++;
        var spec = Game.GetType("specType", true);
        var bonuses = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(spec, typeof(float)));
        foreach (var type in Enum.GetValues(spec)) bonuses[type] = Call(Controller, "getBonusFactor", Call(Controller, "specBonus", type), type);
        Set(Controller, "bonuses", bonuses);
        if (Swaps > 0)
            for (var lane = 0; lane < 2; lane++)
            {
                var owner = lane == 0 ? Character : Get(Character, "magic");
                var total = lane == 0 ? "curEnergy" : "curMagic";
                if ((NativeCapLossLanes & 1 << lane) == 0 || (long)Get(owner, total) <= Cap(lane == 0 ? "energy" : "magic")) continue;
                var generator = Blank(Game.GetType(lane == 0 ? "Energy" : "MagicDisplay", true));
                Set(generator, "character", Character);
                Call(generator, lane == 0 ? "addEnergy" : "updateMagicBar");
                NativeClampCounts[lane]++;
            }
        BonusesRefreshed?.Invoke();
    }

    private static void Setup()
    {
        Resources.Clear(); Array.Clear(Recalls, 0, 3); Swaps = 0; Refreshes = 0;
        DragDuringPreflight = false;
        SwapStarted = null; BonusesRefreshed = null; InventoryRefreshed = null;
        RecallCompleted = null;
        NativeCapLossLanes = 0; Array.Clear(NativeClampCounts, 0, NativeClampCounts.Length);
        Character = Blank(MainType.GetField("Character", All).FieldType);
        Controller = Child(Character, "inventoryController"); Set(Controller, "character", Character);
        Set(MainType, "Character", Character); Set(MainType, "Controller", Controller);
        var settings = Activator.CreateInstance(Plugin.GetType("NGUInjector.SavedSettings", true), All, null, new object[] { null }, null);
        Set(MainType, "Settings", settings);
        foreach (var name in new[] { "ManageEnergy", "ManageMagic", "ManageR3" })
            settings.GetType().GetProperty(name, All).SetValue(settings, true);
        Set(MainType, "OutputWriter", new StreamWriter(Stream.Null));
        Set(Character, "bossID", 100); Child(Character, "settings");
        Child(Character, "stats"); Child(Character, "rebirthTime"); Child(Character, "totalPlaytime");
        Child(Character, "diggers"); Child(Character, "yggdrasil");
        Child(Child(Character, "challenges"), "noEquipmentChallenge");
        Child(Child(Character, "adventureController"), "itopod"); Child(Character, "beastQuestPerkController"); Child(Character, "wishesController");
        var arbitrary = Child(Character, "arbitrary"); Child(arbitrary, "energyPotion1Time"); Child(arbitrary, "magicPotion1Time");
        Set(Character, "capEnergy", 1000L); Set(Child(Character, "magic"), "capMagic", 1000L); Set(Child(Character, "res3"), "capRes3", 1000L);
        var inv = Child(Character, "inventory"); Set(inv, "macguffinBonuses", Enumerable.Repeat(1f, 30).ToList());
        var id = 40;
        foreach (var slot in new[] { "head", "chest", "legs", "boots", "weapon", "weapon2" })
            Set(inv, slot, Equipment(slot == "weapon2" ? 0 : id++, slot == "head" ? "Head" : slot == "chest" ? "Chest" : slot == "legs" ? "Legs" : slot == "boots" ? "Boots" : "Weapon"));
        var accs = List(inv, "accs", 2); foreach (var item in accs) { Set(item, "id", id++); Set(item, "bossRequired", 1); SetEnum(item, "type", "Accessory"); }
        List(inv, "inventory", 10); List(inv, "loadouts", 0);
        Set(Child(Controller, "itemInfo"), "itemName", Enumerable.Range(0, 1000).Select(x => "Fixture " + x).ToArray());
        Resource(Character, "curEnergy", 900L); Resource(Character, "idleEnergy", 300L);
        Resource(Get(Character, "magic"), "curMagic", 900L); Resource(Get(Character, "magic"), "idleMagic", 400L);
        Resource(Get(Character, "res3"), "curRes3", 900L); Resource(Get(Character, "res3"), "idleRes3", 700L);
        var training = Child(Character, "training"); Resource(training, "_attackEnergy", new[] { 100L, 0, 0, 0, 0, 0 }); Resource(training, "_defenseEnergy", new long[6]);
        Resource(Child(Character, "advancedTraining"), "energy", new[] { 100L, 0, 0, 0, 0 });
        var ngu = Child(Character, "NGU");
        var skills = List(ngu, "skills", 9); for (var i = 0; i < skills.Count; i++) Resource(skills[i], "energy", i == 0 ? 100L : 0L);
        var magicSkills = List(ngu, "magicSkills", 7); for (var i = 0; i < magicSkills.Count; i++) Resource(magicSkills[i], "magic", i == 0 ? 100L : 0L);
        var tm = Child(Character, "machine"); Resource(tm, "speedEnergy", 100L); Resource(tm, "goldMultiMagic", 100L);
        var wandoos = Child(Character, "wandoos98"); Resource(wandoos, "wandoosEnergy", 100L); Resource(wandoos, "wandoosMagic", 100L);
        var augs = Child(Character, "augments"); var augField = augs.GetType().GetField("augs", All);
        var augArray = Array.CreateInstance(augField.FieldType.GetElementType(), 1); var aug = Blank(augField.FieldType.GetElementType()); augArray.SetValue(aug, 0); augField.SetValue(augs, augArray);
        Resource(aug, "augEnergy", 50L); Resource(aug, "upgradeEnergy", 50L);
        Resource(List(Child(Character, "bloodMagic"), "ritual", 1)[0], "magic", 100L);
        var wish = List(Child(Character, "wishes"), "wishes", 1)[0]; Resource(wish, "energy", 0L); Resource(wish, "magic", 100L); Resource(wish, "res3", 100L);
        Resource(List(Child(Character, "hacks"), "hacks", 1)[0], "res3", 100L);
        Call(Optimizer, "ClearCache"); RefreshBonuses();
    }

    private static void CheckSafeSwap()
    {
        Setup(); var inv = Get(Character, "inventory"); var old = Get(inv, "head");
        var selected = Equipment(100, "Head"); ((IList)Get(inv, "inventory"))[0] = selected;
        var state = State(); Equal(null, Equip(selected), "safe non-recalling swap succeeds");
        Equal(true, ReferenceEquals(selected, Get(inv, "head")), "real controller equips selected instance");
        Equal(true, ReferenceEquals(old, ((IList)Get(inv, "inventory"))[0]), "real controller preserves displaced instance");
        Equal(1, Swaps, "safe swap executes real native controller once"); Unchanged(state, "safe swap");
    }

    private static void CheckDragging()
    {
        foreach (var duringPreflight in new[] { false, true })
        {
            Setup(); var inv = Get(Character, "inventory"); var old = Get(inv, "head");
            var selected = Equipment(100, "Head"); Set(selected, "curAttack", 100f); Set(selected, "curDefense", 100f);
            var bag = (IList)Get(inv, "inventory"); bag[0] = selected;
            Set(inv, "item1", 123); Set(inv, "item2", 456);
            Set(Controller, "midDrag", !duringPreflight); DragDuringPreflight = duringPreflight;
            var state = State(); var refreshes = Refreshes;
            Contains(Equip(selected), "drag", duringPreflight ? "drag beginning during preflight blocks mutation" : "active drag blocks actuator entry");
            Equal(false, DragDuringPreflight, "preflight drag fixture was consumed");
            Equal(true, Get(Controller, "midDrag"), "actuator does not cancel the user's drag");
            Contains(Equip(old), "drag", "drag also rejects no-op at entry");
            var goal = Enum.Parse(Plugin.GetType("NGUInjector.Managers.GearOptimizationGoal"), "ManualAdventure");
            var mode = Enum.Parse(Plugin.GetType("NGUInjector.Managers.FightScoreMode"), "SqrtPowerToughness");
            var selection = Call(Optimizer, "GetCachedOrOptimize", goal, mode);
            var result = Typed((Array)Property(selection, "Items"));
            Disposition("RejectedUnchanged", result, "selected preserving path rejects drag");
            Contains((string)Get(result, "Reason"), "drag", "typed actuator reports drag failure");
            Equal(0, Swaps, "drag never invokes native swap"); Equal(refreshes, Refreshes, "drag never refreshes bonuses");
            Equal(123, Get(inv, "item1"), "drag preserves item1"); Equal(456, Get(inv, "item2"), "drag preserves item2");
            Equal(true, ReferenceEquals(old, Get(inv, "head")), "drag preserves equipped instance");
            Equal(true, ReferenceEquals(selected, bag[0]), "drag preserves inventory instance");
            Unchanged(state, "drag rejection");
        }
    }

    private static void CheckCapacityReductions()
    {
        foreach (var lane in new[] { "energy", "magic", "R3" })
        foreach (var idleOnly in new[] { false, true })
        {
            Setup(); var inv = Get(Character, "inventory"); var accs = (IList)Get(inv, "accs"); var capItem = accs[1];
            Spec(capItem, lane == "energy" ? "EnergyCap" : lane == "magic" ? "MagicCap" : "Res3Cap", lane == "R3" ? 1000000000f : 10000f);
            if (idleOnly)
                foreach (var resource in Resources)
                {
                    var value = Get(resource.Item1, resource.Item2);
                    if (value is long[]) Array.Clear((long[])value, 0, ((long[])value).Length);
                    else Set(resource.Item1, resource.Item2, resource.Item2.StartsWith("cur") || resource.Item2.StartsWith("idle") ? 900L : 0L);
                }
            var owner = lane == "energy" ? Character : Get(Character, lane == "magic" ? "magic" : "res3");
            Set(owner, lane == "energy" ? "curEnergy" : lane == "magic" ? "curMagic" : "curRes3", 1500L);
            Set(owner, lane == "energy" ? "idleEnergy" : lane == "magic" ? "idleMagic" : "idleRes3", idleOnly ? 1500L : lane == "energy" ? 900L : lane == "magic" ? 1000L : 1300L);
            RefreshBonuses(); Equal(2000L, Cap(lane), lane + " starts with safe native capacity");
            var state = State(); var failure = Equip(capItem);
            Contains(failure, lane, lane + " rejection identifies resource"); Contains(failure, "intermediate", lane + " safe-final accessory move rejects staging loss");
            Equal(true, ReferenceEquals(capItem, accs[1]), "rejection preserves original accessory slot");
            Equal(0, Swaps, "entire unsafe plan rejected before first native swap"); Unchanged(state, lane + " intermediate rejection");

            // A direct replacement loses the cap in the final map, without staging.
            var replacement = Equipment(101, "Accessory"); ((IList)Get(inv, "inventory"))[0] = replacement;
            failure = Equip(replacement, accs[0]);
            Contains(failure, lane, lane + " final capacity loss rejected"); Equal(0, Swaps, "unsafe final map never starts"); Unchanged(state, lane + " final rejection");
        }
    }

    private static void CheckSafeCapacityBoundary()
    {
        foreach (var lane in new[] { "energy", "magic", "R3" })
        {
            Setup(); var inv = Get(Character, "inventory"); var accs = (IList)Get(inv, "accs"); var capItem = accs[1];
            Spec(capItem, lane == "energy" ? "EnergyCap" : lane == "magic" ? "MagicCap" : "Res3Cap", lane == "R3" ? 1000000000f : 10000f);
            var owner = lane == "energy" ? Character : Get(Character, lane == "magic" ? "magic" : "res3");
            Set(owner, lane == "energy" ? "curEnergy" : lane == "magic" ? "curMagic" : "curRes3", 1000L);
            Set(owner, lane == "energy" ? "idleEnergy" : lane == "magic" ? "idleMagic" : "idleRes3", lane == "energy" ? 400L : lane == "magic" ? 500L : 800L);
            RefreshBonuses(); var state = State();
            Equal(null, Equip(capItem), lane + " staging at exact retained capacity succeeds");
            Equal(2, Swaps, lane + " safe staging really runs both native swaps"); Equal(2000L, Cap(lane), lane + " final capacity restored"); Unchanged(state, lane + " exact intermediate boundary");
            var replacement = Equipment(101, "Accessory"); ((IList)Get(inv, "inventory"))[0] = replacement;
            Equal(null, Equip(replacement), lane + " safe final capacity reduction succeeds"); Equal(1000L, Cap(lane), lane + " final capacity may equal retained total"); Unchanged(state, lane + " exact final boundary");
        }
    }

    private static void CheckCurrentCapacity()
    {
        foreach (var lane in new[] { "energy", "magic", "R3" })
        {
            Setup(); var inv = Get(Character, "inventory"); var selected = Equipment(100, "Head"); ((IList)Get(inv, "inventory"))[0] = selected;
            var owner = lane == "energy" ? Character : Get(Character, lane == "magic" ? "magic" : "res3");
            // Even stale cur* cannot hide a larger idle amount.
            Set(owner, lane == "energy" ? "idleEnergy" : lane == "magic" ? "idleMagic" : "idleRes3", 1001L);
            var state = State(); Contains(Equip(selected), lane + " capacity", "over-cap current idle rejected"); Equal(0, Swaps, "current overflow rejected before mutation"); Unchanged(state, "current capacity rejection");
            Setup(); inv = Get(Character, "inventory"); selected = Equipment(100, "Head"); ((IList)Get(inv, "inventory"))[0] = selected;
            var bonuses = (IDictionary)Get(Controller, "bonuses"); bonuses[Enum.Parse(Game.GetType("specType"), lane == "energy" ? "EnergyCap" : lane == "magic" ? "MagicCap" : "Res3Cap")] = 1f;
            state = State(); Contains(Equip(selected), "Cannot verify retained " + lane, "native/projection mismatch fails closed"); Equal(0, Swaps, "unverifiable capacity never starts"); Unchanged(state, "capacity parity rejection");
        }
    }

    private static void CheckNoOpAndLegacy()
    {
        Setup(); var head = Get(Get(Character, "inventory"), "head"); var state = State(); var refreshes = Refreshes;
        Equal(null, Equip(head), "preserving no-op succeeds");
        Equal(null, Property(Typed(Gear(head), 7), "LegacyFailure"), "explicit recall-footprint no-op succeeds");
        Equal(0, Swaps, "no-op never swaps"); Equal(refreshes, Refreshes, "no-op never refreshes bonuses"); Unchanged(state, "both no-op APIs");
        var selected = Equipment(100, "Head"); ((IList)Get(Get(Character, "inventory"), "inventory"))[0] = selected;
        Equal(null, Property(Typed(Gear(selected)), "LegacyFailure"), "explicit no-recall transition succeeds"); Unchanged(state, "no-recall transition");
        selected = Equipment(101, "Head"); ((IList)Get(Get(Character, "inventory"), "inventory"))[0] = selected;
        Equal(null, Property(Typed(Gear(selected), 7), "LegacyFailure"), "explicit full-footprint changed swap succeeds");
        foreach (var count in Recalls) Equal(1, count, "legacy changed swap still calls every recall boundary");
        Equal(800L, Get(Character, "idleEnergy"), "legacy recall returns all non-basic energy");
        Equal(900L, Get(Get(Character, "magic"), "idleMagic"), "legacy recall returns all magic");
        Equal(900L, Get(Get(Character, "res3"), "idleRes3"), "legacy recall returns all R3");
        Equal("100,0,0,0,0,0", string.Join(",", (long[])Get(Get(Character, "training"), "_attackEnergy")), "legacy recall preserves basic attack training");
        Equal("0,0,0,0,0,0", string.Join(",", (long[])Get(Get(Character, "training"), "_defenseEnergy")), "legacy recall preserves basic defense training");
        foreach (var resource in Resources.Where(x => !x.Item2.StartsWith("cur") && !x.Item2.StartsWith("idle") && !x.Item2.StartsWith("_")))
        {
            var value = Get(resource.Item1, resource.Item2);
            if (value is long[]) Equal(true, ((long[])value).All(x => x == 0), "legacy recall clears " + resource.Item2);
            else Equal(0L, value, "legacy recall clears " + resource.Item2);
        }
    }

    private static void CheckOptimizerAndLock()
    {
        Setup(); var inv = Get(Character, "inventory"); var selected = Equipment(100, "Head"); Set(selected, "curAttack", 100f); Set(selected, "curDefense", 100f);
        ((IList)Get(inv, "inventory"))[0] = selected; var state = State();
        var goal = Enum.Parse(Plugin.GetType("NGUInjector.Managers.GearOptimizationGoal"), "ManualAdventure");
        var mode = Enum.Parse(Plugin.GetType("NGUInjector.Managers.FightScoreMode"), "SqrtPowerToughness");
        var selection = Call(Optimizer, "GetCachedOrOptimize", goal, mode);
        var result = Typed((Array)Property(selection, "Items"));
        Disposition("AppliedVerified", result, "real Manual Adventure selection uses preserving actuator");
        Equal(true, ReferenceEquals(selected, Get(inv, "head")), "optimizer's winning head actually equipped"); Unchanged(state, "optimizer preserving API");
        var swaps = Swaps; selection = Call(Optimizer, "GetCachedOrOptimize", goal, mode); result = Typed((Array)Property(selection, "Items"));
        Disposition("AppliedVerified", result, "cached preserving selection no-op succeeds"); Equal(swaps, Swaps, "cached result no-op never swaps"); Unchanged(state, "cached optimizer no-op");
        result = Typed(Gear(selected), authorized: () => false);
        Contains((string)Get(result, "Reason"), "authorized", "direct preserving API rejects revoked authority");
        Disposition("RejectedUnchanged", result, "revoked selected authority rejects before native work"); Unchanged(state, "revoked authority");

        Setup(); inv = Get(Character, "inventory"); Spec(Get(inv, "head"), "MagicCap", 10000f);
        Set(Get(Character, "magic"), "curMagic", 1500L); Set(Get(Character, "magic"), "idleMagic", 1000L);
        selected = Equipment(100, "Head"); Set(selected, "curAttack", 100f); Set(selected, "curDefense", 100f);
        ((IList)Get(inv, "inventory"))[0] = selected; RefreshBonuses(); state = State();
        selection = Call(Optimizer, "GetCachedOrOptimize", goal, mode); result = Typed((Array)Property(selection, "Items"));
        Disposition("RejectedUnchanged", result, "selected unsafe winning loadout cannot recall to make room");
        Contains((string)Get(result, "Reason"), "magic capacity", "typed actuator reports safety failure");
        Equal(0, Swaps, "optimizer capacity failure never starts swapping"); Unchanged(state, "unsafe optimizer winner");
    }

    private static void CheckUnmanagedCapacity()
    {
        foreach (var lane in new[] { "energy", "magic", "R3" })
        foreach (var idleOnly in new[] { false, true })
        foreach (var staging in new[] { false, true })
        {
            Setup();
            var settings = Get(MainType, "Settings");
            settings.GetType().GetProperty(lane == "energy" ? "ManageEnergy" : lane == "magic" ? "ManageMagic" : "ManageR3", All).SetValue(settings, false);
            var inv = Get(Character, "inventory"); var accs = (IList)Get(inv, "accs"); var capItem = accs[1];
            Spec(capItem, lane == "energy" ? "EnergyCap" : lane == "magic" ? "MagicCap" : "Res3Cap", lane == "R3" ? 1000000000f : 10000f);
            if (idleOnly)
                foreach (var resource in Resources)
                {
                    var value = Get(resource.Item1, resource.Item2);
                    if (value is long[]) Array.Clear((long[])value, 0, ((long[])value).Length);
                    else Set(resource.Item1, resource.Item2, resource.Item2.StartsWith("cur") || resource.Item2.StartsWith("idle") ? 900L : 0L);
                }
            var owner = lane == "energy" ? Character : Get(Character, lane == "magic" ? "magic" : "res3");
            var current = lane == "energy" ? "curEnergy" : lane == "magic" ? "curMagic" : "curRes3";
            var idle = lane == "energy" ? "idleEnergy" : lane == "magic" ? "idleMagic" : "idleRes3";
            var funded = idleOnly ? 0L : lane == "energy" ? 600L : lane == "magic" ? 500L : 200L;
            Set(owner, current, 1500L); Set(owner, idle, 1500L - funded);
            var replacement = Equipment(101, "Accessory"); ((IList)Get(inv, "inventory"))[0] = replacement;
            var gear = staging ? Gear(capItem) : Gear(replacement, accs[0]);
            for (var i = 0; i < gear.Length; i++) ItemType.GetProperty("slot", All).SetValue(gear.GetValue(i), 10000 + i);
            var loadouts = Plugin.GetType("NGUInjector.Managers.LoadoutManager", true);
            var wanted = Call(loadouts, "ComposeExactLoadout", Call(loadouts, "CaptureExactLoadout"), gear);
            RefreshBonuses(); Equal(2000L, Cap(lane), "unmanaged " + lane + " seed fits current capacity");
            var state = State();
            Contains((string)Call(Optimizer, "CheckExactSwapCapacity", wanted, 0m, 0m), lane + " capacity", "exact preflight protects unmanaged total without caller reservations");
            Equal(0, Swaps, "exact unmanaged preflight never starts swapping");
            Unchanged(state, "exact unmanaged preflight");
            var failure = (string)Property(Typed(gear, Convert.ToInt32(Call(Optimizer, "LegacyRecallFootprint"))), "LegacyFailure");
            Contains(failure, lane + " capacity", "legacy swap protects unmanaged " + lane + (idleOnly ? " idle" : " idle plus funded"));
            Contains(failure, "intermediate or final", "legacy unmanaged capacity checked across the whole plan");
            Equal(0, Swaps, "unsafe unmanaged map is rejected before any native swap");
            Equal(true, ReferenceEquals(capItem, accs[1]), "unmanaged rejection preserves original cap item");
            Unchanged(state, "legacy unmanaged rejection");

            Set(owner, current, 1000L); Set(owner, idle, 1000L - funded); state = State();
            var laneIndex = lane == "energy" ? 0 : lane == "magic" ? 1 : 2;
            var retained = State(laneIndex);
            var training = Get(Character, "training");
            var attack = string.Join(",", (long[])Get(training, "_attackEnergy"));
            var defense = string.Join(",", (long[])Get(training, "_defenseEnergy"));
            Equal(null, Call(Optimizer, "CheckExactSwapCapacity", wanted, 0m, 0m), "exact unmanaged preflight accepts exact retained-capacity boundary");
            Unchanged(state, "exact safe unmanaged preflight");
            Equal(null, Property(Typed(gear, Convert.ToInt32(Call(Optimizer, "LegacyRecallFootprint"))), "LegacyFailure"), "explicit unmanaged exact-capacity boundary succeeds");
            Equal(2, Swaps, "legacy safe plan executes both native accessory swaps");
            Equal(staging ? 2000L : 1000L, Cap(lane), "legacy safe plan has expected final capacity");
            Equal(retained, State(laneIndex), "safe legacy swap preserves every unmanaged resource amount");
            Equal(attack, string.Join(",", (long[])Get(training, "_attackEnergy")), "safe legacy swap preserves basic attack allocations");
            Equal(defense, string.Join(",", (long[])Get(training, "_defenseEnergy")), "safe legacy swap preserves basic defense allocations");
            Equal(lane == "energy" ? 1000L : 900L, Get(Character, "curEnergy"), "safe legacy swap preserves total energy");
            Equal(lane == "magic" ? 1000L : 900L, Get(Get(Character, "magic"), "curMagic"), "safe legacy swap preserves total magic");
            Equal(lane == "R3" ? 1000L : 900L, Get(Get(Character, "res3"), "curRes3"), "safe legacy swap preserves total R3");
            for (var i = 0; i < Recalls.Length; i++)
                Equal(i == (lane == "energy" ? 0 : lane == "magic" ? 1 : 2) ? 0 : 1, Recalls[i], "legacy swap recalls only eligible managed lanes");
        }
    }

    private static long Cap(string lane) { return (long)Call(Character, lane == "energy" ? "totalCapEnergy" : lane == "magic" ? "totalCapMagic" : "totalCapRes3"); }
    private static string Equip(params object[] equipment)
    {
        var footprint = Enum.Parse(Plugin.GetType("NGUInjector.Managers.GearRecallFootprint", true), "None");
        var outcome = Call(Optimizer, "EquipGearObserved", Gear(equipment), footprint, false, null, null);
        return (string)Property(outcome, "LegacyFailure");
    }
    private static Array Gear(params object[] equipment)
    {
        var gear = Array.CreateInstance(ItemType, equipment.Length);
        for (var i = 0; i < equipment.Length; i++)
        {
            var item = Activator.CreateInstance(ItemType, true);
            ItemType.GetProperty("equipment", All).SetValue(item, equipment[i]); ItemType.GetProperty("id", All).SetValue(item, Get(equipment[i], "id"));
            gear.SetValue(item, i);
        }
        return gear;
    }
    private static void Resource(object owner, string name, object value) { Set(owner, name, value); Resources.Add(Tuple.Create(owner, name)); }
    private static string State(int lane = -1)
    {
        return string.Join(";", Resources.Where(x => {
            var resourceLane = x.Item2.IndexOf("magic", StringComparison.OrdinalIgnoreCase) >= 0 ? 1
                : x.Item2.IndexOf("res3", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 0;
            return lane < 0 || resourceLane == lane;
        }).Select(x => { var value = Get(x.Item1, x.Item2); return value is IEnumerable ? string.Join(",", ((IEnumerable)value).Cast<object>()) : value.ToString(); }));
    }
    private static void Unchanged(string state, string scenario) { Equal(state, State(), scenario + " preserves all actual allocations/totals/idle amounts"); foreach (var count in Recalls) Equal(0, count, scenario + " never recalls E/M/R3"); }
    private static object Equipment(int id, string part) { var item = Blank(Game.GetType("Equipment", true)); Set(item, "id", id); Set(item, "bossRequired", 1); Set(item, "removable", true); SetEnum(item, "type", part); return item; }
    private static void Spec(object item, string type, float value) { SetEnum(item, "spec1Type", type); Set(item, "spec1Cur", value); }
    private static object Blank(Type type) { return FormatterServices.GetUninitializedObject(type); }
    private static object Child(object owner, string name) { var field = owner.GetType().GetField(name, All); var value = Blank(field.FieldType); field.SetValue(owner, value); return value; }
    private static IList List(object owner, string name, int count) { var field = owner.GetType().GetField(name, All); var list = (IList)Activator.CreateInstance(field.FieldType); for (var i = 0; i < count; i++) list.Add(Blank(field.FieldType.GetGenericArguments()[0])); field.SetValue(owner, list); return list; }
    private static object Get(object owner, string name) { return (owner as Type ?? owner.GetType()).GetField(name, All).GetValue(owner is Type ? null : owner); }
    private static void Set(object owner, string name, object value) { (owner as Type ?? owner.GetType()).GetField(name, All).SetValue(owner is Type ? null : owner, value); }
    private static void SetEnum(object owner, string name, string value) { Set(owner, name, Enum.Parse(owner.GetType().GetField(name, All).FieldType, value)); }
    private static object Property(object owner, string name) { return owner.GetType().GetProperty(name, All).GetValue(owner); }
    private static object Call(object owner, string name, params object[] args)
    {
        var method = (owner as Type ?? owner.GetType()).GetMethods(All).Single(x => x.Name == name && x.GetParameters().Length == args.Length);
        return method.Invoke(owner is Type ? null : owner, args);
    }
    private static void Contains(string actual, string expected, string scenario) { Equal(true, actual != null && actual.Contains(expected), scenario + ": " + actual); }
    private static void Equal(object expected, object actual, string scenario) { Assertions++; if (!Equals(expected, actual)) throw new Exception(scenario + ": expected " + expected + ", got " + actual); }
}
