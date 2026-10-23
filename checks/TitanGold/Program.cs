using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Real compiled producer/executor/selection. Only native input and presentation
// boundaries are substituted; timer reset is an explicitly supplied observation.
public static class TitanGoldChecks
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Assembly Plugin, Game;
    static string Root;
    static int Assertions, Cases, Failures;
    static float AttackScale, DefenseScale;
    static int AttackUlps, DefenseUlps, RegenUlps, RoundedHead;
    static readonly List<int> StatHeads = new List<int>(), Moves = new List<int>();
    static readonly Dictionary<string, float[]> NativeRequirements = new Dictionary<string, float[]>();
    static object MainObject => G(typeof(Program), "MainInstance");
    static object Character => G(typeof(Program), "Character");
    static object Settings => G(typeof(Program), "Settings");
    static object Inventory => G(Character, "inventory");
    static object Adventure => G(Character, "adventure");
    static object AdventureController => G(Character, "adventureController");
    static Type MainType => (Type)G(typeof(Program), "MainType");
    static Type Manager(string name) => Plugin.GetType("NGUInjector.Managers." + name, true);
    static int Swaps => (int)G(typeof(Program), "Swaps");
    static bool Active => (bool)G(MainObject, "_customTitanGoldActive");
    static IDictionary Spawns => (IDictionary)G(MainObject, "_customTitanGoldSpawns");
    static object Process(bool fight = false) => G(MainObject, fight ? "_customTitanFightProcess" : "_customTitanGoldProcess");
    static object Claim(bool fight = false) => G(Process(fight), "Claim");
    static bool Done(int index = 0) => ((bool[])G(Settings, "TitanMoneyDone"))[index];
    static IList Bag => (IList)G(Inventory, "inventory");
    static int Head => (int)G(G(Inventory, "head"), "id");

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("TitanGoldChecks <built injector DLL> <game Managed directory>");
            Root = Path.Combine(Path.GetTempPath(), "opencode", "TitanGoldChecks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            var managed = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
                var name = new AssemblyName(e.Name).Name;
                if (name == "Assembly-CSharp" && Game != null) return Game;
                var path = Path.Combine(managed, name + ".dll"); return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Game = (Assembly)P("LoadFixtureGame", PrepareGame(Path.Combine(managed, "Assembly-CSharp.dll")));
            S(typeof(Program), "Game", Game);
            using (var plugin = AssemblyDefinition.ReadAssembly(Path.GetFullPath(args[0])))
            {
                NativeSettingsJsonBoundary.Configure(plugin);
                using (var bytes = new MemoryStream()) { plugin.Write(bytes); Plugin = Assembly.Load(bytes.ToArray()); }
            }
            S(typeof(Program), "Plugin", Plugin); S(typeof(Program), "MainType", Plugin.GetType("NGUInjector.Main", true)); S(typeof(Program), "Temp", Root);
            Console.WriteLine("Injector MVID=" + Plugin.ManifestModule.ModuleVersionId);
            Check("all 33 native requirement versions", CheckRequirements);
            foreach (var successor in new[] { false, true })
            foreach (var fight in new[] { false, true })
                Check("CURRENT successor=" + successor + " Fight=" + fight, () => CheckRelease(successor, fight));
            foreach (var fallback in new[] { false, true })
            foreach (var simultaneous in new[] { false, true })
                Check("co-due fallback=" + fallback + " simultaneous=" + simultaneous, () => CheckBatch(fallback, simultaneous));
            foreach (var fallback in new[] { false, true })
                Check("joining/progress fallback=" + fallback, () => CheckJoining(fallback));
            foreach (var mode in new[] { "co-due", "late", "impossible", "render", "shortcut" })
                Check("stronger obligation " + mode, () => CheckUpgrade(mode));
            foreach (var mode in new[] { "attack", "regen", "native-disabled", "setting", "target", "timeout", "window", "timer" })
                Check("cancellation " + mode, () => CheckCancellation(mode));
            foreach (var mode in new[] { "attack", "native-disabled", "setting", "target" })
                Check("revocation before reset " + mode, () => CheckCancellation(mode, true));
            foreach (var mode in new[] { "gear", "epoch", "support", "route" })
            foreach (var reset in new[] { false, true })
                Check("foreign " + mode + "/" + reset, () => CheckForeign(mode, reset));
            foreach (var mode in new[] { "render", "permission", "epoch", "foreign-route", "partial", "unknown" })
                Check("setup boundary " + mode, () => CheckSetupFailure(mode));
            foreach (var imminent in new[] { false, true }) Check("Fight priority " + imminent, () => CheckPriority(imminent));
            Check("failed incoming selector preserves Fight", CheckFailedPriority);
            foreach (var later in new[] { 1, 2 }) Check("cooled Fight scan " + later, () => CheckCooledFight(later));
            Check("cooled Gold candidate", CheckCooledGold);
            foreach (var dimension in new[] { "power", "toughness", "regen" }) Check("bottleneck " + dimension, () => CheckBottleneck(dimension));
            foreach (var dimension in new[] { "power", "toughness", "regen" })
            foreach (var ulps in new[] { -7, -8 }) Check("capture parity " + dimension + "/" + ulps, () => CheckParity(dimension, ulps));
            foreach (var alternate in new[] { false, true }) Check("preflight rounding headroom " + alternate, () => CheckRounding(alternate));
            foreach (var gate in new[] { "disabled", "boss", "item", "kills", "unlock", "version", "equipment", "challenge" })
                Check("dispatch gate " + gate, () => CheckGate(gate));
            foreach (var kills in new[] { 23, 24 }) Check("native bestiary shortcut " + kills, () => CheckShortcut(kills));
            foreach (var disabled in new[] { false, true }) Check("optional beards unavailable " + disabled, () => CheckOptionalBeards(disabled));
            Console.WriteLine((Failures == 0 ? "PASS: " : "FAIL: ") + Cases + " cases, " + Assertions + " compiled-native assertions; failures=" + Failures + "; offline only.");
            return Failures == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (Root != null && Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    static string PrepareGame(string path)
    {
        using (var game = AssemblyDefinition.ReadAssembly(path))
        {
            var adventure = game.MainModule.GetType("AdventureController");
            var manage = adventure.Methods.Single(x => x.Name == "manageFight");
            var early = new[] { "totalAdvAttack", "totalAdvDefense", "totalAdvHPRegen" }.Select(name => manage.Body.Instructions
                .Where(x => x.Operand is MethodReference && ((MethodReference)x.Operand).Name == name && x.Next.OpCode == OpCodes.Ldc_R4)
                .Select(x => (float)x.Next.Operand).ToArray()).ToArray();
            for (var boss = 0; boss < 5; boss++) NativeRequirements.Add(boss + ":0", new[] { early[0][boss], early[1][boss], boss < 3 ? 0f : early[2][boss - 3] });
            for (var boss = 5; boss < 12; boss++)
            for (var version = 0; version < 4; version++)
                NativeRequirements.Add(boss + ":" + version, adventure.Methods.Single(x => x.Name == "autokillTitan" + (boss + 1) + "V" + (version + 1) + "Achieved")
                    .Body.Instructions.Where(x => x.Operand is MethodReference && new[] { "totalAdvAttack", "totalAdvDefense", "totalAdvHPRegen" }.Contains(((MethodReference)x.Operand).Name)
                        && x.Next.OpCode == OpCodes.Ldc_R4).Select(x => (float)x.Next.Operand).ToArray());
            foreach (var method in game.MainModule.GetType("Character").Methods.Where(x => new[] { "totalAdvAttack", "totalAdvDefense", "totalAdvHPRegen" }.Contains(x.Name)))
            foreach (var ret in method.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
                method.Body.GetILProcessor().InsertBefore(ret, Instruction.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(TitanGoldChecks).GetMethod(
                    method.Name == "totalAdvAttack" ? nameof(ObserveAttack) : method.Name == "totalAdvDefense" ? nameof(ObserveDefense) : nameof(ObserveRegen), All))));
            foreach (var type in game.MainModule.Types)
            foreach (var method in type.Methods.Where(x => x.HasBody))
            {
                if (new[] { "adventureBonus", "adventureBonus2", "totalAdventureBonus", "beastModeBonus" }.Contains(method.Name)
                    && (method.ReturnType.FullName == "System.Single" || method.ReturnType.FullName == "System.Double")) P("Constant", method, 1d);
                if (type.Name == "AllAdvancedTraining" && new[] { "adventurePowerBonus", "adventureToughnessBonus" }.Contains(method.Name))
                    Boundary(game.MainModule, method, method.Name == "adventurePowerBonus" ? nameof(NativePowerBonus) : nameof(NativeToughnessBonus));
                if (type.Name == "InventoryController" && method.Name == "updateHead") P("Constant", method, 0d);
            }
            Boundary(game.MainModule, game.MainModule.GetType("ZoneSelector").Methods.Single(x => x.Name == "changeZone"), nameof(ChangeZone));
            var result = Path.Combine(Root, "game-input.dll"); game.Write(result); return result;
        }
    }
    static void Boundary(ModuleDefinition module, MethodDefinition method, string name)
    {
        method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
        foreach (var parameter in method.Parameters) il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(TitanGoldChecks).GetMethod(name, All))); il.Emit(OpCodes.Ret);
    }
    public static float NativePowerBonus(int id) => AttackScale - 1;
    public static float NativeToughnessBonus(int id) => DefenseScale - 1;
    static float Observe(float value, int ulps)
    {
        StatHeads.Add(Head);
        return ulps == 0 ? value : BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(value), 0) + ulps), 0);
    }
    public static float ObserveAttack(float value) => Observe(value, RoundedHead == 0 || Head == RoundedHead ? AttackUlps : 0);
    public static float ObserveDefense(float value) => Observe(value, DefenseUlps);
    public static float ObserveRegen(float value) => Observe(value, RegenUlps);
    public static void ChangeZone(int zone) { Moves.Add(zone); S(Adventure, "zone", zone); }

    static void Setup(int index = 0, bool fallback = false)
    {
        AttackScale = fallback ? 20 : 40; DefenseScale = 30; AttackUlps = DefenseUlps = RegenUlps = RoundedHead = 0;
        Program.NativeGearSwapped = null; P("Setup"); P("Child", Character, "res3");
        foreach (var name in new[] { "beards", "diggers" })
        {
            var b = P("Child", G(Character, "buttons"), name); S(b, "m_Interactable", true); S(b, "m_GroupsAllowInteraction", true);
        }
        foreach (var name in new[] { "allBeards", "allDiggers" }) S(G(Character, name), "character", Character);
        S(G(Character, "allBeards"), "usesEnergy", new bool[7]);
        S(G(G(Character, "challenges"), "trollChallenge"), "curCompletions", 7);
        P("Child", Character, "machine"); S(G(Character, "machine"), "realBaseGold", 100d); P("Child", Character, "timeMachineController");
        S(P("Child", Character, "bloodMagicController"), "character", Character);
        S(G(Character, "allChallenges"), "character", Character);
        var diggers = G(Character, "allDiggers");
        foreach (var name in new[] { "baseGPSDrain", "gpsGrowthRate", "startingBoost", "boostPerLevel", "consumesGPS" })
        {
            var field = diggers.GetType().GetField(name, All); var list = (IList)Activator.CreateInstance(field.FieldType);
            for (var i = 0; i < 12; i++) list.Add(Convert.ChangeType(name == "gpsGrowthRate" ? 2 : 0, field.FieldType.GetGenericArguments()[0]));
            S(diggers, name, list);
        }
        P("ListField", diggers, "pods", 0); P("ListField", P("Child", Character, "bestiary"), "enemies", 400);
        S(MainType, "OutputWriter", null); S(MainType, "PitSpinWriter", null); S(MainType, "Test", false);
        S(MainObject, "_customTitanGoldPendingIndex", -1); S(MainObject, "_customTitanGoldIndex", -1); S(MainObject, "_customTitanFightIndex", -1); S(MainObject, "_manualTitanIndex", -1);
        Setting("CustomAdventureEnabled", false); Setting("ManageGoldLoadouts", true);
        Setting("TitanGoldTargets", new bool[12]); Setting("TitanMoneyDone", new bool[12]); Setting("TitanSwapTargets", new bool[12]);
        Targets[index] = true; S(G(Character, "settings"), "autoKillTitans", true); S(Character, "bossID", 1000); S(Character, "highestBoss", 1000);
        var button = P("Child", G(Character, "buttons"), "adventure"); S(button, "m_Interactable", true); S(button, "m_GroupsAllowInteraction", true);
        P("Child", Character, "adventure"); S(Adventure, "zone", -1); S(Adventure, "boss5Kills", 3);
        S(G(Inventory, "itemList"), "itemMaxxed", Enumerable.Repeat(false, 1000).ToList()); ((IList)G(G(Inventory, "itemList"), "itemMaxxed"))[135] = true;
        S(AdventureController, "character", Character); P("Child", AdventureController, "zoneSelector"); P("ListField", AdventureController, "enemyList", 0);
        S(MainType, "PlayerController", P("Child", AdventureController, "playerController")); P("Child", AdventureController, "enemyAI");
        S(MainObject, "_combManager", Activator.CreateInstance(Manager("CombatManager"), true));
        for (var i = 0; i < 12; i++)
        {
            P("Child", Adventure, "boss" + (i + 1) + "Spawn");
            if (i >= 5) { S(Adventure, "titan" + (i + 1) + "Unlocked", true); S(Adventure, "titan" + (i + 1) + "Version", 0); }
        }
        S(((IList)G(Inventory, "accs"))[1], "id", 0); S(G(Inventory, "head"), "curAttack", 1f); S(G(Inventory, "head"), "curDefense", 1f);
        var gold = NewHead(60, 100); P("SetEnum", gold, "spec1Type", "GoldDropAmount"); S(gold, "spec1Cur", 1e9f);
        Bag[0] = gold; Bag[1] = NewHead(70, 10000);
        S(Adventure, "attack", 0f); S(Adventure, "defense", 0f); S(Adventure, "regen", .25f); S(Inventory, "cubePower", 0f); S(Inventory, "cubeToughness", 0f);
        P("RefreshBonuses"); I(Manager("GearOptimizer"), "ClearCache"); StatHeads.Clear(); Moves.Clear(); Remaining(index, 3);
    }
    static bool[] Targets => (bool[])G(Settings, "TitanGoldTargets");
    static object NewHead(int id, float stat)
    {
        var item = P("Blank", G(Inventory, "head").GetType()); S(item, "id", id); S(item, "bossRequired", 1); S(item, "removable", true);
        S(item, "curAttack", stat); S(item, "curDefense", stat); P("SetEnum", item, "type", "Head"); return item;
    }
    static void Remaining(int index, double seconds) => S(G(Adventure, "boss" + (index + 1) + "Spawn"), "totalseconds",
        (float)I(AdventureController, "boss" + (index + 1) + "SpawnTime") - seconds);
    static void Reset(int index) => S(G(Adventure, "boss" + (index + 1) + "Spawn"), "totalseconds", 0d);
    static bool Route()
    {
        var entries = Moves.Count;
        try { return (bool)M("TryRouteCustomTitanGoldKill"); }
        finally { Equal(true, Moves.Skip(entries).All(zone => zone == -1), "Gold may only retire to Safe Zone, never manually enter a titan"); }
    }
    static object Capture() => I(Manager("LoadoutManager"), "CaptureExactLoadout");
    static void Exact(object map, string message) => Equal(true, I(Manager("LoadoutManager"), "ExactLoadoutMatches", map), message);
    static void Retired(object selected, string message, bool fight = false)
    {
        Equal(false, G(MainObject, fight ? "_customTitanFightActive" : "_customTitanGoldActive"), message + ": process inactive");
        Equal(null, Process(fight), message + ": process reference retired");
        Equal(false, M("ExecutionOwnsDomain", Domain("Gear, Support, Route, NativeSettings")), message + ": actual executor authority retired");
        Exact(selected, message + ": no successor preserves CURRENT native map");
    }
    static object Domain(string value) => Enum.Parse(MainType.GetNestedType("ExecutionDomain", All), value);
    static object PublishSuccessor(int head = 80)
    {
        Bag[3] = NewHead(head, 400); var current = Capture();
        var array = (Array)current;
        foreach (var row in array) if ((int)G(row, "slot") == -1) { S(row, "equipment", Bag[3]); S(row, "id", head); }
        var setup = Activator.CreateInstance(MainType.GetNestedType("SelectedSetupRequirements", All), true);
        S(setup, "Owner", "titan-current-baseline"); S(setup, "Acquisition", "baseline-B"); S(setup, "Gear", current);
        var model = Character; var inventory = Inventory; var owner = MainObject; var settings = Settings;
        var rebirth = G(G(model, "stats"), "rebirthNumber");
        var material = array.Cast<object>().Select(x => M("LoadoutPresetPrimitiveKey", G(x, "equipment")).ToString()).ToArray();
        S(setup, "Support", Property(M("CaptureSupportState"), "Selection"));
        S(setup, "Authorized", (Func<bool>)(() => ReferenceEquals(model, Character) && ReferenceEquals(inventory, Inventory)
            && ReferenceEquals(owner, MainObject) && ReferenceEquals(settings, Settings) && Equals(rebirth, G(G(model, "stats"), "rebirthNumber"))));
        S(setup, "MaterialValid", (Func<bool>)(() => !array.Cast<object>().Where((x, i) => material[i] != M("LoadoutPresetPrimitiveKey", G(x, "equipment")).ToString()).Any()));
        M("PublishExecutionBaseline", setup); return current;
    }

    static void CheckRelease(bool successor, bool fight)
    {
        Setup(0, fight); var original = Capture(); var before = Swaps;
        Remaining(0, 3.01); Equal(false, Route(), "outside preparation window"); Equal(before, Swaps, "early selection has no swaps"); Remaining(0, 3);
        if (fight) Targets[0] = false;
        Equal(true, fight ? M("TryRouteCustomTitanFightKill") : (object)Route(), "actual producer acquires");
        Equal(fight ? 70 : 60, Head, "pure selection equips only the feasible map"); Equal(before + 1, Swaps, "no speculative Gold trial");
        var process = Process(fight); var claim = Claim(fight); var selected = Capture(); var start = Swaps;
        var baseline = fight ? null : Spawns[0];
        foreach (var remaining in new[] { 2d, 0d, -.5d })
        {
            Remaining(0, remaining); Equal(true, fight ? M("TryRouteCustomTitanFightKill") : (object)Route(), "maintains current process through due window");
            Equal(true, ReferenceEquals(claim, Claim(fight)), "same real claim"); Equal(start, Swaps, "waiting does not reselect/re-equip");
            if (!fight) Equal(baseline, Spawns[0], "original elapsed baseline"); Equal(false, Done(), "due time is not reward evidence");
        }
        var expected = successor ? PublishSuccessor() : selected;
        if (fight) M("CancelCustomTitanFightKill", "release check"); else { Reset(0); Equal(false, Route(), "reset retires Gold"); Equal(true, Done(), "full Gold reset credited"); }
        if (successor) { Equal(80, Head, "CURRENT B installed, not original A"); Equal(start + 1, Swaps, "one direct successor application"); }
        else { Retired(expected, "no successor", fight); Equal(start, Swaps, "no historical restore"); }
        Exact(expected, "exact CURRENT successor/native map"); Equal(false, I(Manager("LoadoutManager"), "ExactLoadoutMatches", original), "original A was not secretly restored");
        var settled = Swaps; M(fight ? "CancelCustomTitanFightKill" : "CancelCustomTitanGoldKill", "repeat"); Equal(settled, Swaps, "retirement is idempotent");
    }
    static void CheckBatch(bool fallback, bool simultaneous)
    {
        Setup(0, fallback); if (!fallback) { AttackScale = 100; DefenseScale = 80; }
        Targets[1] = true; Remaining(1, 3); var before = Swaps; Equal(true, Route(), "batch admitted");
        Equal(fallback ? 70 : 60, Head, "batch selected map"); Equal(before + 1, Swaps, "one selected application"); Equal(2, Spawns.Count, "both obligations enrolled");
        var claim = Claim(); var selected = Capture(); var swaps = Swaps; var second = Spawns[1];
        Equal(true, Z("TitanAutokillAchieved", 0), "native primary"); Equal(true, Z("TitanAutokillAchieved", 1), "native secondary");
        Reset(0);
        if (!simultaneous)
        {
            Equal(true, Route(), "outstanding secondary retains batch"); Equal(!fallback, Done(), "primary reward only in full Gold"); Equal(false, Done(1), "secondary not yet credited");
            Equal(true, ReferenceEquals(claim, Claim()), "same batch claim"); Equal(second, Spawns[1], "secondary elapsed retained"); Exact(selected, "same selected map");
        }
        Reset(1); Equal(false, Route(), "both observed resets retire"); Equal(!fallback, Done(), "primary credit"); Equal(!fallback, Done(1), "secondary credit");
        Retired(selected, "batch complete"); Equal(swaps, Swaps, "no-successor batch has no release swap");
        Equal(0, Spawns.Count, "all tracking removed"); var publication = G(Settings, "TitanMoneyDone"); Equal(false, Route(), "reset cannot restart pending target");
        Equal(true, ReferenceEquals(publication, G(Settings, "TitanMoneyDone")), "completion published only once");
    }
    static void CheckJoining(bool fallback)
    {
        Setup(0, fallback); if (!fallback) { AttackScale = 100; DefenseScale = 80; }
        Targets[1] = true; Remaining(1, 3.01); Equal(true, Route(), "primary starts"); Equal(1, Spawns.Count, "early secondary excluded");
        var claim = Claim(); var selected = Capture(); var swaps = Swaps; var old = DateTime.UtcNow.AddMinutes(-1); S(MainObject, "_customTitanGoldStartedAt", old);
        Remaining(1, 3); Reset(0); Equal(true, Route(), "confirmed primary progress admits due secondary even on aged batch");
        Equal(!fallback, Done(), "primary Gold credit only when full Gold"); Equal(1, Spawns.Count, "sole outstanding secondary"); Equal(true, Spawns.Contains(1), "secondary tracked");
        Equal(Z("TitanSpawnElapsedSeconds", 1), Spawns[1], "join captures native elapsed"); Equal(true, (DateTime)G(MainObject, "_customTitanGoldStartedAt") > old.AddSeconds(30), "real progress renews stall timeout");
        Equal(true, ReferenceEquals(claim, Claim()), "joining retains original acquisition"); Equal(swaps, Swaps, "joining no swap"); Reset(1); Equal(false, Route(), "secondary reset retires"); Retired(selected, "joined completion");
    }
    static void CheckUpgrade(string mode)
    {
        Setup(); Targets[1] = true; Remaining(1, mode == "late" ? 3.01 : 3);
        if (mode == "impossible") { S(Bag[1], "curAttack", 200f); S(Bag[1], "curDefense", 200f); }
        if (mode == "shortcut") { Targets[8] = true; Remaining(8, 3); S(Adventure, "titan9Version", 2); Bestiary(24); }
        Equal(true, Route(), "easy full-Gold primary admitted"); Equal(60, Head, "initial Gold selection"); Equal(false, Spawns.Contains(1), "stronger target not admitted prematurely");
        var claim = Claim(); var started = G(MainObject, "_customTitanGoldStartedAt"); var elapsed = Spawns[0]; var gold = Capture(); var swaps = Swaps;
        if (mode == "late") { Equal(true, Route(), "early target cannot upgrade"); Equal(swaps, Swaps, "early target no swap"); Remaining(1, 3); }
        if (mode == "render") S(typeof(Program), "FailRefreshAtSwap", swaps + 1);
        Equal(true, Route(), "active batch services pure replacement selection");
        Equal(true, ReferenceEquals(claim, Claim()), "upgrade retains actual claim"); Equal(started, G(MainObject, "_customTitanGoldStartedAt"), "upgrade never renews stall deadline"); Equal(elapsed, Spawns[0], "old obligation elapsed retained");
        if (mode == "impossible")
        {
            Exact(gold, "infeasible selection preserves current Gold"); Equal(swaps, Swaps, "no speculative impossible application"); Equal(false, Spawns.Contains(1), "impossible obligation not enrolled");
            Equal(true, ((DateTime[])G(MainObject, "_customTitanAutokillCooldownUntil"))[1] > DateTime.UtcNow.AddMinutes(59), "only failed obligation cools");
        }
        else
        {
            Equal(70, Head, "selected P/T replacement applied once"); Equal(swaps + 1, Swaps, "no rollback/trial");
            Equal(false, G(MainObject, "_customTitanGoldUsesGoldGear"), "fallback cannot earn later Gold credit");
            Equal(true, Route(), "native verification admits retained replacement"); Equal(true, Spawns.Contains(1), "stronger obligation enrolled only after native verification");
            if (mode == "render") Equal(-1, G(typeof(Program), "FailRefreshAtSwap"), "real render fault fired after model swap");
            foreach (int index in Spawns.Keys) Equal(true, Z("TitanAutokillAchieved", index), "all old/new native obligations verify: " + index);
        }
        var selected = Capture(); swaps = Swaps; Reset(0); if (mode == "shortcut") Reset(8);
        Equal(mode != "impossible", Route(), "primary reset retains only accepted stronger target"); Equal(mode == "impossible", Done(), "only retained full Gold earns reward");
        if (mode != "impossible") { Reset(1); Equal(false, Route(), "stronger reset completes"); Equal(false, Done(1), "P/T reset no Gold reward"); }
        Retired(selected, "upgrade completion"); Equal(swaps, Swaps, "no final historical return");
    }
    static void CheckCancellation(string mode, bool reset = false)
    {
        Setup(0); Equal(true, Route(), "cancellation process starts"); var selected = Capture(); var swaps = Swaps;
        if (mode == "attack") AttackScale = 1;
        if (mode == "regen") { S(Adventure, "regen", 0f); DefenseScale = 0; }
        if (mode == "native-disabled") S(G(Character, "settings"), "autoKillTitans", false);
        if (mode == "setting") Setting("ManageGoldLoadouts", false);
        if (mode == "target") Targets[0] = false;
        if (mode == "timeout") S(MainObject, "_customTitanGoldStartedAt", DateTime.UtcNow.AddMinutes(-1));
        if (mode == "window") Remaining(0, -21);
        if (mode == "timer") S(G(Adventure, "boss1Spawn"), "totalseconds", double.NaN);
        if (reset) Reset(0);
        Equal(false, Route(), "unsafe batch cancels"); Equal(false, Done(), "cancellation has no reward proof"); Retired(selected, mode); Equal(swaps, Swaps, "cancellation no historical swap");
    }
    static void CheckForeign(string mode, bool reset)
    {
        Setup(); AttackScale = 100; DefenseScale = 80; Targets[1] = true; Remaining(1, 3); Equal(true, Route(), "foreign fixture admits both targets");
        if (mode == "gear") { var foreign = NewHead(72, 123); var old = G(Inventory, "head"); Bag[2] = old; S(Inventory, "head", foreign); }
        if (mode == "epoch") S(G(Character, "stats"), "rebirthNumber", 8L);
        if (mode == "support")
        {
            var state = G(Character, "beards"); var active = (IList)G(state, "activeBeards");
            var id = active.Contains(0) ? 1 : 0; S(((IList)G(state, "beards"))[id], "active", true); active.Add(id);
        }
        if (mode == "route") S(Adventure, "zone", 4);
        var selected = Capture(); var swaps = Swaps; var zone = G(Adventure, "zone"); if (reset) { Reset(0); Reset(1); }
        Equal(false, Route(), "foreign authority retires without reward"); Equal(false, Done() || Done(1), "foreign reset cannot earn credit");
        Equal(0, Spawns.Count, "old tracking cleared"); Exact(selected, "foreign map preserved"); Equal(swaps, Swaps, "no foreign gear write"); Equal(zone, G(Adventure, "zone"), "foreign route not adopted for retirement");
    }
    static void CheckSetupFailure(string mode)
    {
        Setup(); var before = Swaps; var calls = 0; object selected = null, attempt = null;
        if (mode == "partial")
        {
            var accessory = NewHead(71, 0); P("SetEnum", accessory, "type", "Accessory"); P("SetEnum", accessory, "spec1Type", "GoldDropAmount"); S(accessory, "spec1Cur", 1e9f); Bag[2] = accessory;
        }
        if (mode == "render") S(typeof(Program), "FailRefreshAtSwap", before + 1);
        else Program.NativeGearSwapped = _ => {
            if (++calls != 1) return; Program.NativeGearSwapped = null; attempt = Process();
            if (mode == "permission") Setting("CustomAdventureEnabled", true);
            if (mode == "epoch") S(G(Character, "stats"), "rebirthNumber", 8L);
            if (mode == "foreign-route") S(Adventure, "zone", 4);
            if (mode == "partial") throw new InvalidOperationException("bounded partial native swap");
            if (mode == "unknown") S(Inventory, "inventory", null);
        };
        var bag = Bag; Exception failure = null; bool routed = false;
        try { routed = Route(); } catch (Exception error) { failure = error; }
        finally { Program.NativeGearSwapped = null; S(Inventory, "inventory", bag); }
        if (mode == "render")
        {
            Equal(true, routed, "verified model survives render-only failure"); Equal(-1, G(typeof(Program), "FailRefreshAtSwap"), "render fault actually fired");
            selected = Capture(); M("CancelCustomTitanGoldKill", "render check"); Retired(selected, "render outcome");
        }
        else
        {
            Equal(true, calls > 0, "injection reached native inventory boundary"); Equal(false, routed, "unverified setup cannot admit dependent combat");
            Equal(false, Active, "failed owner retires"); Equal(false, Done(), "failed setup never earns reward"); Equal(before + 1, Swaps, "failure never rolls back historical map");
            Equal(mode == "foreign-route" ? 4 : -1, G(Adventure, "zone"), "no dependent/foreign route entry");
            if (mode == "unknown") Equal(true, failure != null, "unobservable outcome is surfaced, not fake success");
            if (mode == "partial") Equal("KnownPartial", G(G(G(attempt, "Setup"), "GearOutcome"), "Disposition").ToString(), "actual bounded partial outcome, not a completed render-only application");
        }
    }
    static void CheckPriority(bool imminent)
    {
        Setup(); Remaining(0, 20); Remaining(2, 19); Equal(true, M("TryRoutePriorityTitan"), "ordinary Fight admitted while Gold early");
        Equal(2, G(MainObject, "_customTitanFightIndex"), "distinct Fight target"); var claim = Claim(true); var fight = Capture(); var swaps = Swaps;
        Remaining(0, imminent ? 3 : 3.01); Equal(true, M("TryRoutePriorityTitan"), "priority services current/incoming process");
        if (!imminent)
        {
            Equal(true, ReferenceEquals(claim, Claim(true)), "early Gold preserves Fight acquisition"); Exact(fight, "early Gold preserves prerequisites"); Equal(swaps, Swaps, "no early application");
            Remaining(2, -21); Equal(false, M("TryRoutePriorityTitan"), "expired Fight retires even while Gold early"); Retired(fight, "expired Fight", true); return;
        }
        Equal(false, G(MainObject, "_customTitanFightActive"), "imminent Gold retires Fight"); Equal(true, G(claim, "Ended"), "old real claim ended"); Equal(true, Active, "Gold successor admitted");
        Equal(60, Head, "Gold current map"); Equal(swaps + 1, Swaps, "direct temporary-to-temporary handoff, no A flash"); Equal(-1, G(Adventure, "zone"), "Gold waits in Safe Zone");
        var gold = Capture(); Reset(0); Equal(false, Route(), "Gold reset completes"); Equal(true, Done(), "Gold reward proof"); Retired(gold, "priority completion");
    }
    static void CheckFailedPriority()
    {
        Setup(3); Remaining(3, 20); Remaining(0, 19); Equal(true, M("TryRoutePriorityTitan"), "weaker safe Fight starts");
        var claim = Claim(true); var selected = Capture(); var swaps = Swaps; Remaining(3, 3);
        Equal(true, M("TryRoutePriorityTitan"), "infeasible stronger Gold cannot preempt safe Fight maintenance");
        Equal(true, Z("TitanAutokillAchieved", 0), "existing Fight prerequisites genuinely remain natively safe");
        Equal(true, ((DateTime[])G(MainObject, "_customTitanAutokillCooldownUntil"))[3] > DateTime.UtcNow.AddMinutes(59), "only infeasible incoming obligation cools");
        Equal(true, ReferenceEquals(claim, Claim(true)), "failed incoming pure selection cannot withdraw Fight claim"); Exact(selected, "failed incoming selection preserves Fight setup"); Equal(swaps, Swaps, "failed selector no mutation");
    }
    static void CheckCooledFight(int later)
    {
        Setup(0, true); Targets[0] = false; Remaining(0, 19); Remaining(later, 19);
        if (later == 2) { Targets[1] = true; Remaining(1, 19); }
        M("EnsureTitanAutokillCooldownArrays"); var until = DateTime.UtcNow.AddHours(1); ((DateTime[])G(MainObject, "_customTitanAutokillCooldownUntil"))[0] = until;
        Equal(true, M("TryRouteCustomTitanFightKill"), "scan skips cooled/active Gold targets"); Equal(later, G(MainObject, "_customTitanFightIndex"), "later actual target admitted");
        Equal(true, Z("TitanAutokillAchieved", later), "later native requirement"); Equal(until, ((DateTime[])G(MainObject, "_customTitanAutokillCooldownUntil"))[0], "original cooldown retained");
        var selected = Capture(); M("CancelCustomTitanFightKill", "scan cleanup"); Retired(selected, "cooled scan", true);
    }
    static void CheckCooledGold()
    {
        Setup(); AttackScale = 100; DefenseScale = 80; Targets[1] = true; Remaining(1, 3); M("EnsureTitanAutokillCooldownArrays");
        var until = DateTime.UtcNow.AddHours(1); ((DateTime[])G(MainObject, "_customTitanAutokillCooldownUntil"))[0] = until;
        Equal(1, M("FindCustomTitanGoldPreparation"), "cooled first never masks later Gold"); Equal(true, Route(), "later Gold admitted"); Equal(1, G(MainObject, "_customTitanGoldIndex"), "correct actual target");
        Equal(false, Spawns.Contains(0), "cooled primary not adopted"); Reset(1); Equal(false, Route(), "later reset completes"); Equal(false, Done(), "cooled target no credit"); Equal(true, Done(1), "later target credited");
    }
    static void CheckBottleneck(string dimension)
    {
        Setup(0, true); var trap = NewHead(70, 10000); var feasible = NewHead(71, 200);
        if (dimension == "toughness") S(trap, "curDefense", 50f);
        if (dimension == "power") S(trap, "curAttack", 50f);
        var index = dimension == "regen" ? 3 : 0;
        if (index == 3) { Targets[0] = false; Targets[3] = true; Remaining(3, 3); AttackScale = 8001; DefenseScale = 4001; S(trap, "curDefense", 100f); S(feasible, "curAttack", 120f); S(feasible, "curDefense", 110f); }
        Bag[1] = trap; Bag[2] = feasible; P("RefreshBonuses"); var swaps = Swaps;
        var goal = Enum.Parse(Plugin.GetType("NGUInjector.Managers.GearOptimizationGoal", true), "Fight");
        var product = I(Manager("GearOptimizer"), "GetCachedOrOptimize", goal, Enum.Parse(Plugin.GetType("NGUInjector.Managers.FightScoreMode", true), "PowerTimesToughness"));
        Equal(true, G(product, "Success"), "generic product control selected"); Equal(true, Route(), "requirement-aware selection admitted"); Equal(71, Head, "lower product feasible map wins");
        Equal(swaps + 1, Swaps, "pure selection never equips generic trap/Gold trial"); Equal(false, StatHeads.Contains(70), "infeasible generic winner never equipped"); Equal(true, Z("TitanAutokillAchieved", index), "native limiting dimension verifies");
    }
    static void CheckParity(string dimension, int ulps)
    {
        Setup(0, true); if (dimension == "power") AttackUlps = ulps; if (dimension == "toughness") DefenseUlps = ulps; if (dimension == "regen") RegenUlps = ulps;
        var swaps = Swaps; var original = Capture(); Exception failure = null; bool result = false;
        try { result = Route(); } catch (Exception error) { failure = error; }
        Equal(ulps == -7, result, "real capture parity threshold");
        if (ulps == -7) { Equal(70, Head, "bounded drift selects verified fallback"); Equal(true, Z("TitanAutokillAchieved", 0), "native postcheck"); }
        else
        {
            Equal(null, failure, "pure capture rejection returns a bounded producer result");
            Equal(true, ((string[])G(MainObject, "_customTitanAutokillCooldownReason"))[0].Contains("Forward adventure stats do not match native getters"), "actual unsupported capture reason retained");
            Equal(swaps, Swaps, "parity rejection before any speculative equip"); Exact(original, "parity rejection unchanged");
            Equal(true, ((DateTime[])G(MainObject, "_customTitanAutokillCooldownUntil"))[0] > DateTime.UtcNow.AddMinutes(59), "unsupported capture retains one-hour failure cooldown");
            Equal(false, Route(), "unsupported capture cannot search every combat tick"); Equal(swaps, Swaps, "cooled capture remains read-only");
        }
    }
    static void CheckRounding(bool alternate)
    {
        Setup(0, true); DefenseScale = 1; AttackScale = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(1f), 0) + 1), 0);
        var near = NewHead(70, 10000); S(near, "curAttack", 3000f); Bag[1] = near; if (alternate) Bag[2] = NewHead(71, 4000);
        AttackUlps = -1; RoundedHead = 70; var swaps = Swaps; var original = Capture(); Equal(alternate, Route(), "preflight eight-ULP headroom excludes fragile boundary");
        Equal(false, StatHeads.Contains(70), "fragile projected boundary never applied");
        if (alternate) { Equal(71, Head, "robust alternate selected before mutation"); Equal(swaps + 1, Swaps, "only robust selection applied"); Equal(true, Z("TitanAutokillAchieved", 0), "robust native result"); }
        else { Equal(swaps, Swaps, "no feasible inventory no mutation"); Exact(original, "infeasible inventory unchanged"); Equal(true, ((DateTime[])G(MainObject, "_customTitanAutokillCooldownUntil"))[0] > DateTime.UtcNow.AddMinutes(59), "infeasible target cooled"); }
    }
    static void CheckGate(string gate)
    {
        var index = gate == "item" ? 3 : gate == "kills" ? 4 : gate == "unlock" || gate == "version" ? 5 : 0; Setup(index);
        if (gate == "disabled") S(G(Character, "settings"), "autoKillTitans", false);
        if (gate == "boss") S(Character, "bossID", 0);
        if (gate == "item") ((IList)G(G(Inventory, "itemList"), "itemMaxxed"))[135] = false;
        if (gate == "kills") S(Adventure, "boss5Kills", 2);
        if (gate == "unlock") S(Adventure, "titan6Unlocked", false);
        if (gate == "version") S(Adventure, "titan6Version", 4);
        if (gate == "equipment") S(Inventory, "disabled", true);
        if (gate == "challenge") S(G(G(Character, "challenges"), "noEquipmentChallenge"), "inChallenge", true);
        var selected = Capture(); var swaps = Swaps; bool result = false; try { result = Route(); } catch (Exception error) { Equal(true, error.ToString().Contains("Equipment bonuses are disabled"), "actual equipment gate rejection"); }
        Equal(false, result, "non-stat/disabled equipment gate"); Equal(swaps, Swaps, "gate before application"); Exact(selected, "gate preserves native setup");
    }
    static void Bestiary(int kills)
    {
        var enemies = (IList)P("ListField", P("Child", Character, "bestiary"), "enemies", 400); S(enemies[346], "kills", kills);
    }
    static void CheckShortcut(int kills)
    {
        Setup(8); S(Adventure, "titan9Version", 2); Bestiary(kills); AttackScale = DefenseScale = 1;
        Equal(kills == 24, I(AdventureController, "autokillTitan9V3Achieved"), "native bestiary 23/24 boundary");
        Equal(kills == 24, Route(), "actual producer respects native shortcut without impossible numeric minima");
        if (kills == 24) { Equal(60, Head, "shortcut still selects Gold"); Reset(8); Equal(false, Route(), "shortcut still requires actual timer reset"); Equal(true, Done(8), "Gold reward after observed reset"); }
    }
    static void CheckOptionalBeards(bool disabled)
    {
        Setup();
        if (disabled) S(G(Character, "beards"), "disabled", true);
        else S(G(G(Character, "buttons"), "beards"), "m_Interactable", false);
        Equal(true, Route(), "optional Gold beard absence cannot block native-achievable titan");
        Equal(true, Z("TitanAutokillAchieved", 0), "native requirement remains authoritative without optional utility");
        Equal(0, ((int[])Property(Property(M("CaptureSupportState"), "Selection"), "Beards")).Length, "locked/disabled support is not activated");
        var selected = Capture(); Reset(0); Equal(false, Route(), "native reset finishes optional-beard case"); Retired(selected, "optional beard completion");
    }
    static void CheckRequirements()
    {
        var versions = 0;
        for (var index = 0; index < 12; index++)
        for (var version = 0; version < (index < 5 ? 1 : 4); version++)
        {
            Setup(index); if (index >= 5) S(Adventure, "titan" + (index + 1) + "Version", version);
            S(G(Character, "beards"), "disabled", true);
            foreach (var name in new[] { "head", "chest", "legs", "boots", "weapon", "weapon2" })
            { S(G(Inventory, name), "curAttack", 0f); S(G(Inventory, name), "curDefense", 0f); }
            foreach (var item in (IList)G(Inventory, "accs")) { S(item, "curAttack", 0f); S(item, "curDefense", 0f); }
            var required = (float[])Z("TitanAutokillRequirements", index); Equal(3, required.Length, "three native requirement dimensions");
            var native = NativeRequirements[index + ":" + version];
            for (var stat = 0; stat < 3; stat++)
            {
                var literal = native[stat];
                if (index < 3 && stat < 2) literal = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(literal), 0) + 1), 0);
                Equal(literal, required[stat], "table matches installed native ldc.r4 independently of production requirements");
            }
            foreach (var dimension in new[] { 0, 1, 2 })
            foreach (var offset in new[] { -1, 0, 1 })
            {
                if (required[dimension] == 0 && offset < 0) continue;
                var values = (float[])required.Clone(); if (values[dimension] > 0) values[dimension] = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(values[dimension]), 0) + offset), 0);
                // Native AT multipliers are inputs; actual equipment getters, float
                // arithmetic and all later achieved bodies remain installed game code.
                AttackScale = DefenseScale = 1; S(Adventure, "attack", values[0]); S(Adventure, "defense", values[1]); S(Adventure, "regen", values[2]);
                S(G(Inventory, "head"), "curAttack", 0f); S(G(Inventory, "head"), "curDefense", 0f); P("RefreshBonuses");
                // Direct native requirement dispatch is independently compared with
                // production ZoneHelpers, including strict first-three thresholds.
                var expected = offset >= 0;
                if (index >= 5) Equal(expected, I(AdventureController, "autokillTitan" + (index + 1) + "V" + (version + 1) + "Achieved"), "native selected-version adjacent float");
                Equal(expected, Z("TitanAutokillAchieved", index), "production/native table adjacent float " + index + "/" + version + "/" + dimension + "/" + offset);
            }
            versions++;
        }
        Equal(33, versions, "all selected native versions covered");
    }
    static object P(string name, params object[] args) => I(typeof(Program), name, args);
    static object M(string name, params object[] args) => I(MainObject, name, args);
    static object Z(string name, params object[] args) => I(Manager("ZoneHelpers"), name, args);
    static object I(object owner, string name, params object[] args)
    {
        try { return typeof(Program).GetMethod("Invoke", All).Invoke(null, new object[] { owner, name, args }); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    static object G(object owner, string name) => typeof(Program).GetMethod("Get", All).Invoke(null, new[] { owner, name });
    static object Property(object owner, string name) => owner.GetType().GetProperty(name, All).GetValue(owner);
    static void S(object owner, string name, object value) => typeof(Program).GetMethod("Set", All).Invoke(null, new[] { owner, name, value });
    static void Setting(string name, object value) => S(Settings, name, value);
    static void Check(string name, Action check)
    {
        Cases++; try { check(); Console.WriteLine("PASS: " + name); } catch (Exception error) { Failures++; while (error.InnerException != null) error = error.InnerException; Console.Error.WriteLine("FAIL: " + name + ": " + error.Message + Environment.NewLine + error.StackTrace); }
    }
    static void Equal(object expected, object actual, string message)
    {
        Assertions++; if (!Equals(expected, actual)) throw new Exception(message + ": expected " + expected + ", actual " + actual);
    }
}
