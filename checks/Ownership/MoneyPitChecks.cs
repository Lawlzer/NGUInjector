using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class OwnershipChecks
{
    // Scripted external input, NOT Unity's PRNG. s0 is a cursor into (min, max, result)
    // triples; the remaining native State fields distinguish caller and saved pit states.
    static object PitRngState;
    static int[][] PitScript;
    static readonly List<string> PitDraws = new List<string>();
    static int PitThrowAt = -1, PitAccessoryCount = 2;
    static int PitCases;
    static readonly int[] TierExponents = { 5, 7, 9, 11, 13, 15, 18, 21, 24, 27, 30, 50, 55, 60, 65, 70 };
    static readonly int[] SelectorUpper = { 9, 10, 10, 10, 11, 12, 13, 13, 13, 13, 13, 6, 6, 6, 6, 6 };

    static void SubstitutePitInputs(ModuleDefinition module)
    {
        // Adapt the native value type via boxing so this executable needs no compile-time
        // game/Unity reference. Only external RNG and unlocked-slot inputs are redirected.
        var shim = new TypeDefinition("", "OwnershipPitInputs", Mono.Cecil.TypeAttributes.NotPublic |
            Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(shim);
        var adapters = new Dictionary<string, MethodDefinition>();
        var calls = module.Types.SelectMany(Types).Where(x => x != shim).SelectMany(x => x.Methods)
            .Where(x => x.HasBody).SelectMany(x => x.Body.Instructions).ToArray();
        foreach (var instruction in calls)
        {
            var call = instruction.Operand as MethodReference;
            if (call == null) continue;
            var rng = call.DeclaringType.FullName == "UnityEngine.Random";
            var spaces = call.DeclaringType.FullName == "InventoryController" && call.Name == "accessorySpaces";
            if (!spaces && (!rng || call.Name != "get_state" && call.Name != "set_state" &&
                !(call.Name == "Range" && call.Parameters[0].ParameterType.FullName == "System.Int32"))) continue;
            var key = spaces ? "AccessorySpaces" : call.Name;
            if (!adapters.TryGetValue(key, out var adapter))
            {
                adapter = new MethodDefinition(key, Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, call.ReturnType);
                shim.Methods.Add(adapter);
                if (spaces) adapter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                else foreach (var parameter in call.Parameters) adapter.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
                var il = adapter.Body.GetILProcessor();
                foreach (var parameter in adapter.Parameters) il.Emit(OpCodes.Ldarg, parameter);
                if (key == "set_state") il.Emit(OpCodes.Box, call.Parameters[0].ParameterType);
                var boundary = spaces ? nameof(PitSpaces) : key == "get_state" ? nameof(PitGetState)
                    : key == "set_state" ? nameof(PitSetState) : nameof(PitRange);
                il.Emit(OpCodes.Call, module.ImportReference(typeof(OwnershipChecks).GetMethod(boundary, All)));
                if (key == "get_state") il.Emit(OpCodes.Unbox_Any, call.ReturnType);
                il.Emit(OpCodes.Ret);
                adapters.Add(key, adapter);
            }
            instruction.OpCode = OpCodes.Call; instruction.Operand = adapter;
        }
        Check(adapters.ContainsKey("get_state") && adapters.ContainsKey("set_state") && adapters.ContainsKey("Range"),
            module.Name + " has actual RNG call sites, not a replaced selection method");
    }

    public static object PitGetState() => PitRngState;
    public static void PitSetState(object value) { PitRngState = value; }
    public static int PitSpaces(object controller) => PitAccessoryCount;
    public static int PitRange(int min, int max)
    {
        var cursor = (int)G(PitRngState, "s0");
        PitDraws.Add(min + ":" + max);
        var next = NewPitState(cursor + 1, (int)G(PitRngState, "s1"));
        PitRngState = next;
        if (cursor == PitThrowAt) throw new InvalidOperationException("scripted RNG boundary failure");
        if (PitScript == null) return max - 1; // Existing non-preview ownership checks.
        Check(cursor >= 0 && cursor < PitScript.Length, "no unexpected RNG draw at " + cursor);
        var step = PitScript[cursor];
        Equal(step[0], min, "native/preview inclusive RNG lower bound at draw " + cursor);
        Equal(step[1], max, "native/preview exclusive RNG upper bound at draw " + cursor);
        Check(step[2] >= min && step[2] < max, "scripted result is inside requested range");
        return step[2];
    }

    static object NewPitState(int cursor, int marker)
    {
        var type = G(Character, "pit").GetType().GetField("pitState", All).FieldType;
        var state = Activator.CreateInstance(type);
        S(state, "s0", cursor); S(state, "s1", marker); S(state, "s2", marker + 17); S(state, "s3", marker - 31);
        return state;
    }
    static string StateKey(object state) => string.Join(",", new[] { "s0", "s1", "s2", "s3" }.Select(x => G(state, x)));
    static void ResetPitInputs()
    {
        PitScript = null; PitThrowAt = -1; PitAccessoryCount = 2; PitDraws.Clear(); PitCalls = 0; PitRendered = PitInvoking = PitSetupReady = null; PitText = null; NativePitError = null; LastPitResult = null;
        var pit = Child(Character, "pit"); S(pit, "pitState", NewPitState(0, 111));
        PitRngState = NewPitState(73, 777);
        S(Child(pit, "pitTime"), "totalseconds", 1e9);
        var controller = G(Character, "pitController");
        S(controller, "character", Character); S(controller, "inventoryController", G(Character, "inventoryController"));
        S(controller, "itemInfo", G(G(Character, "inventoryController"), "itemInfo"));
        Setting("MoneyPitThreshold", 200000d);
        S(pit, "tier1TRewarded", true); S(pit, "tier2TRewarded", true); S(pit, "tier3TRewarded", true);
        S(pit, "tier4TRewarded", true); S(pit, "tier5TRewarded", true); S(G(Character, "settings"), "badge2Part2Complete", true);
        P("ListField", Child(Character, "achievements"), "achievementComplete", 512);
        S(Child(Character, "allAchievements"), "character", Character);
        P("ListField", G(G(Character, "inventory"), "itemList"), "itemMaxxed", 600);
        P("ListField", Child(G(Character, "adventure"), "itopod"), "perkLevel", 200);
    }
    static void Script(params int[][] draws) { PitScript = draws; PitDraws.Clear(); }
    static object Preview() => Invoke(Manager("MoneyPitManager"), "PreviewReward");
    static Array PitMap(params object[] items)
    {
        var result = Array.CreateInstance(Plugin.GetType("NGUInjector.Managers.ih", true), items.Length);
        for (var i = 0; i < items.Length; i++) result.SetValue(items[i], i);
        return result;
    }
    static object PitHelper(object gear, int slot) => Invoke(Plugin.GetType("NGUInjector.Extensions", true), "GetInventoryHelper", gear, slot);
    static object PitGear(int id, string type, int level = 0)
    {
        var item = NewGear(id, type); S(item, "level", level); return item;
    }
    static Array CapturePitGear() => (Array)Invoke(Manager("LoadoutManager"), "CaptureExactLoadout");
    static object At(Array map, int slot) => map.Cast<object>().Single(x => (int)G(x, "slot") == slot);
    static Array SelectPit(Array original, Array owned, params int[] slots) =>
        (Array)Invoke(Manager("MoneyPitLoadout"), "Select", original, owned, slots);
    static void SamePitMap(Array expected, Array actual, string reason)
    {
        Equal(expected.Length, actual.Length, reason + " complete topology");
        foreach (var item in expected)
        {
            var other = At(actual, (int)G(item, "slot"));
            Equal(G(item, "id"), G(other, "id"), reason + " ID at " + G(item, "slot"));
            if ((int)G(item, "id") != 0)
                Check(ReferenceEquals(G(item, "equipment"), G(other, "equipment")), reason + " exact instance at " + G(item, "slot"));
        }
    }
    static void PitCase(Action test) { Case(test); PitCases++; }

    static void CheckMoneyPitRegressions()
    {
        var before = Checks; PitCases = 0;
        CheckPitTierBoundaries();
        CheckPitSelectors();
        CheckPitRandomTargets();
        CheckPitPreviewExceptions();
        CheckPitClassification();
        CheckPitInstanceSelection();
        CheckPitCurrentSuccessors();
        CheckPitSetupFailures();
        CheckNativePitMilestones();
        CheckNativePitMilestonePredicates();
        CheckPitSessionFaults();
        Console.WriteLine("Money Pit: " + PitCases + " scenarios, " + (Checks - before) + " assertions (scripted external RNG; actual native feed/accounting and bounded session attempts).");
    }

    static void CheckPitTierBoundaries()
    {
        Setup();
        foreach (var wish in new[] { 0, 1 })
        for (var i = 0; i < TierExponents.Length; i++)
        foreach (var offset in new[] { -.000001, 0d, .000001 })
        PitCase(() => {
            S(((IList)G(G(Character, "wishes"), "wishes"))[4], "level", wish);
            S(Character, "realGold", Math.Pow(10, TierExponents[i] + offset));
            var tier = offset < 0 ? i : i + 1;
            if (wish == 0) tier = Math.Min(11, tier);
            Script(tier == 0 ? new int[0][] : new[] { new[] { 1, SelectorUpper[tier - 1], SelectorUpper[tier - 1] - 1 } });
            var caller = StateKey(PitRngState); var saved = StateKey(G(G(Character, "pit"), "pitState"));
            var map = CapturePitGear(); var resources = P("Resources");
            var preview = Preview();
            Equal(tier, G(preview, "Tier"), "tier at log10(gold)=" + (TierExponents[i] + offset) + ", wish=" + wish);
            Equal(tier == 0 ? 0 : SelectorUpper[tier - 1] - 1, G(preview, "Roll"), "boundary selector result");
            Equal(tier == 0 ? 0 : 1, PitDraws.Count, "no draw below first tier; exactly one selector otherwise");
            Equal(caller, StateKey(PitRngState), "successful preview restores all caller RNG words");
            Equal(saved, StateKey(G(G(Character, "pit"), "pitState")), "successful preview never advances pitState");
            Equal(resources, P("Resources"), "preview preserves resources/settings");
            SamePitMap(map, CapturePitGear(), "preview preserves equipment");
        });
        foreach (var gold in new[] { 0d, -1d, double.NaN, 99999d })
        PitCase(() => {
            S(Character, "realGold", gold); Script(); var caller = StateKey(PitRngState);
            var preview = Preview(); Equal(0, G(preview, "Tier"), "invalid/below-minimum gold has no tier");
            Equal(false, G(preview, "LevelsEquipment"), "invalid/below-minimum gold cannot level gear");
            Equal(0, PitDraws.Count, "invalid/below-minimum gold never draws");
            Equal(caller, StateKey(PitRngState), "early return preserves RNG");
        });
    }

    static void CheckPitSelectors()
    {
        Setup(); S(((IList)G(G(Character, "wishes"), "wishes"))[4], "level", 1);
        for (var tier = 1; tier <= 16; tier++)
        for (var roll = 1; roll < SelectorUpper[tier - 1]; roll++)
        PitCase(() => {
            S(Character, "realGold", Math.Pow(10, TierExponents[tier - 1] + .25));
            var random = tier >= 2 && tier <= 4 && roll == 4;
            var script = new List<int[]> { new[] { 1, SelectorUpper[tier - 1], roll } };
            if (random) for (var n = 0; n < (tier == 4 ? 2 : 1); n++)
                script.AddRange(new[] { new[] { -7, 0, -1 }, new[] { 0, 100, 37 } });
            Script(script.ToArray()); var preview = Preview();
            Equal(tier, G(preview, "Tier"), "selector table tier"); Equal(roll, G(preview, "Roll"), "selector table exact roll");
            var all = tier >= 12 ? roll == 2 : tier >= 5 && roll == 4;
            Equal(all, G(preview, "AllEquipment"), "all-equipment classification for tier/roll " + tier + "/" + roll);
            Equal(all || random, G(preview, "LevelsEquipment"), "only actual equipment rewards request a loadout");
            Equal(random ? tier == 4 ? 2 : 1 : 0, ((int[])G(preview, "RandomSlots")).Length, "random-hit multiplicity");
            Equal(script.Count, PitDraws.Count, "exact preview draw count");
        });
        // Each native tier method retains its selector, switch, equipment calls and levelUp IL.
        for (var tier = 1; tier <= 16; tier++)
        PitCase(() => {
            Setup(); S(((IList)G(G(Character, "wishes"), "wishes"))[4], "level", 1);
            S(Character, "realGold", Math.Pow(10, TierExponents[tier - 1] + .25));
            var roll = tier >= 12 ? 2 : 4;
            var script = new List<int[]> { new[] { 1, SelectorUpper[tier - 1], roll } };
            if (tier >= 2 && tier <= 4) for (var n = 0; n < (tier == 4 ? 2 : 1); n++)
                script.AddRange(new[] { new[] { -7, 0, -1 }, new[] { 0, 100, 37 } });
            Script(script.ToArray()); var preview = Preview(); var trace = string.Join(",", PitDraws);
            var original = CapturePitGear(); PitDraws.Clear();
            Invoke(G(Character, "pitController"), "tier" + tier + "Reward");
            Equal(trace, string.Join(",", PitDraws), "native tier selector/extra-draw parity " + tier);
            AssertNativePitLevels(original, preview, "native tier " + tier);
        });
    }

    static void AssertNativePitLevels(Array original, object preview, string reason)
    {
        var all = (bool)G(preview, "AllEquipment"); var slots = (int[])G(preview, "RandomSlots");
        foreach (var item in original)
        {
            var hits = all ? 1 : slots.Count(x => x == (int)G(item, "slot"));
            Equal(Math.Min(100, (int)G(item, "level") + hits), G(G(item, "equipment"), "level"),
                reason + " actual native levels at slot " + G(item, "slot"));
        }
    }

    static void CheckPitRandomTargets()
    {
        foreach (var spaces in new[] { 0, 2, 5, 7 })
        for (var draw = -5 - spaces; draw < 0; draw++)
        PitCase(() => {
            Setup(); PitAccessoryCount = spaces;
            var accs = (IList)G(G(Character, "inventory"), "accs");
            while (accs.Count < spaces) accs.Add(PitGear(70 + accs.Count, "Accessory"));
            while (accs.Count > spaces) accs.RemoveAt(accs.Count - 1);
            S(Character, "realGold", 1e7);
            Script(new[] { 1, 10, 4 }, new[] { -5 - spaces, 0, draw }, new[] { 0, 100, 1 });
            var map = CapturePitGear(); var preview = Preview();
            var expected = draw >= -5 ? draw : draw >= -10 ? 10000 - draw - 6 : -1;
            Equal(expected.ToString(), string.Join(",", (int[])G(preview, "RandomSlots")), "exact random target including >5-accessory head fallback");
            Equal(3, PitDraws.Count, "cosmetic draw retained even when text says butt");
            PitDraws.Clear(); Invoke(G(Character, "pitController"), "tier2Reward");
            AssertNativePitLevels(map, preview, "native random target " + draw + "/" + spaces);
            Equal(3, PitDraws.Count, "native random target uses identical cosmetic draw");
        });
        foreach (var second in new[] { -7, -1 })
        PitCase(() => {
            Setup(); S(Character, "realGold", 1e11);
            Script(new[] { 1, 10, 4 }, new[] { -7, 0, -1 }, new[] { 0, 100, 1 },
                new[] { -7, 0, second }, new[] { 0, 100, 99 });
            var map = CapturePitGear(); var preview = Preview();
            Equal("-1," + (second == -1 ? -1 : 10001), string.Join(",", (int[])G(preview, "RandomSlots")),
                "tier4 interleaves cosmetics and retains repeated hits");
            Equal(5, PitDraws.Count, "tier4 selector plus two target/cosmetic pairs");
            PitDraws.Clear(); Invoke(G(Character, "pitController"), "tier4Reward");
            AssertNativePitLevels(map, preview, "native tier4 exact/repeated targets");
            Equal(5, PitDraws.Count, "native tier4 draw count");
        });
    }

    static void CheckPitPreviewExceptions()
    {
        for (var fault = 0; fault < 5; fault++)
        PitCase(() => {
            Setup(); S(Character, "realGold", 1e11);
            Script(new[] { 1, 10, 4 }, new[] { -7, 0, -1 }, new[] { 0, 100, 1 }, new[] { -7, 0, -7 }, new[] { 0, 100, 99 });
            PitThrowAt = fault;
            var caller = StateKey(PitRngState); var saved = StateKey(G(G(Character, "pit"), "pitState"));
            var map = CapturePitGear(); var resources = P("Resources");
            Exception failure = null; try { Preview(); } catch (Exception error) { failure = error; }
            Check(failure != null && failure.ToString().Contains("scripted RNG boundary failure"), "external RNG failure propagated at draw " + fault);
            Equal(fault + 1, PitDraws.Count, "exception really occurred at selected draw");
            Equal(caller, StateKey(PitRngState), "finally restores all caller state words after exception");
            Equal(saved, StateKey(G(G(Character, "pit"), "pitState")), "exception never mutates saved pitState");
            Equal(resources, P("Resources"), "exception preview preserves resource state");
            SamePitMap(map, CapturePitGear(), "exception preview preserves gear");
            Equal(0, PitCalls, "preview exception never engages");
        });
    }

    static void CheckPitClassification()
    {
        Setup();
        foreach (var heart in new[] { 119, 129, 162, 171, 196, 212, 293, 297, 344, 390 })
        PitCase(() => {
            var original = CapturePitGear(); var gear = PitGear(heart, "Accessory");
            var owned = PitMap(PitHelper(PitGear(514, "Accessory", 99), 1), PitHelper(PitGear(249, "Accessory", 99), 2),
                PitHelper(PitGear(505, "Accessory", 99), 3), PitHelper(gear, 0));
            var selected = SelectPit(original, owned, 10000);
            Check(ReferenceEquals(gear, G(At(selected, 10000), "equipment")), "explicit heart outranks loot/progression/panties: " + heart);
        });
        foreach (var special in new[] { 53, 76, 94, 142, 170, 229, 295, 388, 430, 504, 67, 128, 169, 230, 296, 389, 431, 505 })
        PitCase(() => {
            var gear = PitGear(special, "Accessory");
            var selected = SelectPit(CapturePitGear(), PitMap(PitHelper(PitGear(514, "Accessory", 99), 1), PitHelper(gear, 0)), 10000);
            Check(ReferenceEquals(gear, G(At(selected, 10000), "equipment")), "explicit pendant/Looty priority: " + special);
        });
        // These synthetic same-part candidates isolate progression rank from level/name/ID order.
        foreach (var pair in new[] { new[] { 249, 503 }, new[] { 249, 505 }, new[] { 503, 468 }, new[] { 503, 479 },
            new[] { 468, 469 }, new[] { 227, 213 }, new[] { 514, 999 } })
        PitCase(() => {
            var winner = pair[1] == 999 ? pair[0] : pair[1];
            var a = PitHelper(PitGear(pair[0], "Accessory", 99), 0); S(a, "name", "Heart Shaped Panties");
            var b = PitHelper(PitGear(pair[1], "Accessory", 1), 1); S(b, "name", "ordinary item");
            Equal(winner, G(At(SelectPit(CapturePitGear(), PitMap(a, b), 10000), 10000), "id"),
                "source stage, not ID/level/name, orders " + pair[0] + " vs " + pair[1]);
        });
        foreach (var versions in new[] { new[] { 189, 190, 193, 195 }, new[] { 243, 245, 247, 249 },
            new[] { 271, 273, 275, 277 }, new[] { 328, 330, 332, 334 }, new[] { 380, 382, 384, 386 },
            new[] { 423, 425, 427, 429 }, new[] { 476, 477, 478, 479 } })
        for (var version = 1; version < versions.Length; version++)
        PitCase(() => {
            var selected = SelectPit(CapturePitGear(), PitMap(PitHelper(PitGear(versions[version - 1], "Accessory", 99), 0),
                PitHelper(PitGear(versions[version], "Accessory", 1), 1)), 10000);
            Equal(versions[version], G(At(selected, 10000), "id"), "later titan version outranks earlier high-level copy");
        });
    }

    static void CheckPitInstanceSelection()
    {
        PitCase(() => {
            Setup(); var original = CapturePitGear(); var good = PitGear(503, "Accessory", 99);
            var maxed = PitGear(390, "Accessory", 100); var boost = PitGear(119, "specBoost", 99);
            var selected = SelectPit(original, PitMap(PitHelper(maxed, 0), PitHelper(boost, 1), PitHelper(PitGear(0, "Accessory"), 2), PitHelper(good, 3)), 10000);
            Check(ReferenceEquals(good, G(At(selected, 10000), "equipment")), "maxed/boost/empty candidates cannot displace level99 equipment");
            SamePitMap(original, SelectPit(original, PitMap(PitHelper(maxed, 0), PitHelper(boost, 1)), 10000), "no eligible candidate retains original");
            SamePitMap(original, CapturePitGear(), "selection does not mutate game");
            Equal(100, G(maxed, "level"), "selection never levels maxed gear"); Equal(99, G(good, "level"), "selection never levels eligible gear");
        });
        PitCase(() => {
            Setup(); var original = CapturePitGear(); var low = PitGear(119, "Accessory", 20); var high = PitGear(119, "Accessory", 90);
            var other = PitGear(129, "Accessory", 80);
            var owned = PitMap(PitHelper(low, 0), PitHelper(high, 1), PitHelper(other, 2));
            var selected = SelectPit(original, owned, 10000, 10001, 10000, 12345);
            Check(ReferenceEquals(high, G(At(selected, 10000), "equipment")), "same-ID reward chooses highest-level exact instance");
            Check(ReferenceEquals(other, G(At(selected, 10001), "equipment")), "second reward uses distinct ID, not another copy");
            SamePitMap(original, CapturePitGear(), "duplicate selection leaves game unchanged");
            Equal(0, G(owned.GetValue(0), "slot"), "owned wrappers retain original locations");
        });
        PitCase(() => {
            Setup(); var inv = G(Character, "inventory"); var equipped = PitGear(119, "Accessory", 50);
            ((IList)G(inv, "accs"))[1] = equipped;
            var original = CapturePitGear(); var copy = PitGear(119, "Accessory", 50);
            var owned = PitMap(PitHelper(copy, 0), At(original, 10001));
            var selected = SelectPit(original, owned, 10001);
            Check(ReferenceEquals(equipped, G(At(selected, 10001), "equipment")), "equal-level duplicate prefers exact equipped instance");
            var higher = PitGear(119, "Accessory", 51);
            selected = SelectPit(original, PitMap(At(original, 10001), PitHelper(higher, 0)), 10001);
            Check(ReferenceEquals(higher, G(At(selected, 10001), "equipment")), "higher-level same-ID inventory instance replaces equipped copy");
            var first = PitGear(129, "Accessory", 50); var later = PitGear(129, "Accessory", 50);
            selected = SelectPit(original, PitMap(PitHelper(later, 8), PitHelper(first, 2)), 10000);
            Check(ReferenceEquals(first, G(At(selected, 10000), "equipment")), "tie uses source slot, not enumeration order");
        });
        PitCase(() => {
            Setup(); S(typeof(Program), "OffhandUnlocked", true);
            var original = CapturePitGear(); var owned = new List<object>();
            foreach (var type in new[] { "Head", "Chest", "Legs", "Boots", "Weapon", "Accessory" })
                owned.Add(PitHelper(PitGear(510 + owned.Count, type), owned.Count));
            var selected = SelectPit(original, PitMap(owned.ToArray()), -1, -2, -3, -4, -6, 10000);
            foreach (var slot in new[] { -1, -2, -3, -4, -6, 10000 })
            {
                var type = slot == -1 ? "Head" : slot == -2 ? "Chest" : slot == -3 ? "Legs" : slot == -4 ? "Boots" : slot == -6 ? "Weapon" : "Accessory";
                Equal(type, G(G(At(selected, slot), "equipment"), "type").ToString(), "reward destination uses native part " + slot);
            }
            Check(ReferenceEquals(G(At(original, -5), "equipment"), G(At(selected, -5), "equipment")), "unrewarded primary weapon retained when offhand is targeted");
            SamePitMap(original, SelectPit(original, PitMap(owned.ToArray()), 99999), "unknown reward destination ignored");
        });
    }

}
