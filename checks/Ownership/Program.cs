using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Native model/accounting and compiled executor authority stay intact. Only unavailable
// scene rendering, external RNG/slots and Unity shutdown callbacks are substituted.
public static partial class OwnershipChecks
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Assembly Plugin, Game;
    static string Root;
    static int Checks, Scenarios, PitCalls;
    static bool ObserveFocusAuthority;
    static readonly List<string> FocusAuthorityFailures = new List<string>();
    static object MainObject => G(typeof(Program), "MainInstance");
    static object Character => G(typeof(Program), "Character");
    static object Settings => G(typeof(Program), "Settings");
    static Type MainType => (Type)G(typeof(Program), "MainType");
    static Type Manager(string name) => Plugin.GetType("NGUInjector.Managers." + name, true);

    public static int Main(string[] args)
    {
        try
        {
            var selectors = new[] { "manual-preset-handoff", "money-pit", "yggdrasil", "fruit-lanes", "magic-focus", "gold-recovery", "harvest-recovery", "inventory-rescan" };
            if (args.Length < 2 || args.Length > 3 || args.Length == 3 && !selectors.Contains(args[2]))
                throw new ArgumentException("OwnershipChecks <built injector DLL> <game Managed directory> [" + string.Join("|", selectors) + "]");
            Root = Path.Combine(Path.GetTempPath(), "opencode", "OwnershipChecks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            var managed = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
                var name = new AssemblyName(e.Name).Name;
                if (name == "Assembly-CSharp" && Game != null) return Game;
                var path = Path.Combine(managed, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Game = (Assembly)P("LoadFixtureGame", PrepareGame(Path.Combine(managed, "Assembly-CSharp.dll")));
            S(typeof(Program), "Game", Game);
            Plugin = PreparePlugin(Path.GetFullPath(args[0]));
            S(typeof(Program), "Plugin", Plugin); S(typeof(Program), "MainType", Plugin.GetType("NGUInjector.Main", true));
            var selector = args.Length == 3 ? args[2] : null;
            if (selector == null || selector == "manual-preset-handoff") Case(CheckStageReceipts);
            if (selector == null || selector == "gold-recovery") CheckGoldRecoveryRegressions();
            if (selector == null) { Case(CheckExactMaps); Case(CheckNativeCapacityGuard); CheckSupportClaims(); CheckEpochs(); Case(CheckUnload); }
            if (selector == null || selector == "magic-focus") { Case(CheckMagicFocus); Case(CheckRejectedFocusHandoff); Case(CheckFocusMetadataGear); }
            if (selector == null || selector == "money-pit") CheckMoneyPitRegressions();
            if (selector == null || selector == "fruit-lanes" || selector == "yggdrasil") CheckFruitResourceLanes();
            if (selector == null || selector == "yggdrasil") CheckYggdrasilRegressions();
            if (selector == null || selector == "harvest-recovery" || selector == "yggdrasil") CheckYggRestorationRecovery();
            if (selector == null || selector == "inventory-rescan") CheckInventoryRescans();
            Console.WriteLine("PASS: " + Checks + " Ownership assertions / " + Scenarios + " scenarios; compiled authority, native finite leaves, offline only.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Program.NativeGearSwapped = null; if (Root != null && Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    static string PrepareGame(string path)
    {
        using (var a = AssemblyDefinition.ReadAssembly(path))
        {
            SubstitutePitInputs(a.MainModule); PreparePitRendering(a.MainModule); ConfigureSupportText(a.MainModule);
            foreach (var type in a.MainModule.Types)
            foreach (var method in type.Methods.Where(x => x.HasBody))
            {
                if (type.Name == "Character" && method.Name == "machineBossMulti"
                    || type.Name == "TimeMachineController" && new[] { "barFillsPerSecond", "speedGoldMultiBonus", "goldMultiBonus" }.Contains(method.Name)
                    || type.Name == "AllBloodMagicController" && method.Name == "goldBonus"
                    || type.Name == "AllNGUController" && method.Name == "timeMachineBonus"
                    || type.Name == "TimeMachineChallengeController" && method.Name == "totalGPSbonus"
                    || type.Name == "AllGoldDiggerController" && method.Name == "totalLevelBonus") Constant(method, 1);
                if (type.Name == "Character" && method.Name == "refreshMenus") GameBoundary(a.MainModule, method, nameof(PitPresentation));
                if (type.Name == "ButtonShower" && method.Name == "updateButtons") Constant(method, 0);
                if (type.Name == "AllBeardsController" && (method.Name == "beardSize" || method.Name == "capBeards")) Constant(method, method.Name == "beardSize" ? 7 : 3);
                if (type.Name == "AllGoldDiggerController" && method.Name == "refreshMenu") GameBoundary(a.MainModule, method, nameof(DiggerPresentation));
                if (type.Name == "AllGoldDiggerController" && method.Name == "maxDiggerSlots") Constant(method, 2);
                if (type.Name == "GoldDiggerUIController" && method.Name == "updateUI" || type.Name == "AllBeardsController" && method.Name == "refreshMenu") Constant(method, 0);
                if (type.Name == "InventoryController" && method.Name == "updateHead") GameBoundary(a.MainModule, method, nameof(NativeHeadRefresh));
                if ((type.Name == "NGUController" || type.Name == "NGUMagicController") && method.Name == "updateText") Constant(method, 0);
                if (type.Name == "TimeMachineController" && (method.Name == "updateSpeedText" || method.Name == "updateGoldMultiText")) Constant(method, 0);
                if (type.Name == "TimeMachineController" && (method.Name == "baseSpeedDivider" || method.Name == "baseGoldMultiDivider")
                    || method.Name == "TMSpeedBonus" || method.Name == "totalTMSpeedBonus"
                    || method.Name == "totalRes3CapBonus" && method.ReturnType.FullName == "System.Single") Constant(method, 1);
                if (type.Name == "Character" && method.Name == "display" && method.ReturnType.FullName == "System.String")
                { method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldstr, "fixture display"); il.Emit(OpCodes.Ret); }
            }
            var inventory = a.MainModule.GetType("InventoryController");
            var native = inventory.Methods.Single(x => x.Name == "swapHead" && x.Parameters.Count == 0);
            native.Name = "OwnershipNativeSwapHead";
            var facade = new MethodDefinition("swapHead", native.Attributes, a.MainModule.TypeSystem.Void);
            facade.Body.GetILProcessor().Emit(OpCodes.Ret); inventory.Methods.Add(facade);
            ConfigureInventoryRescanGameFixture(a.MainModule);
            var result = Path.Combine(Root, "game-input.dll"); a.Write(result); return result;
        }
    }

    static Assembly PreparePlugin(string path)
    {
        using (var a = AssemblyDefinition.ReadAssembly(path))
        {
            Console.WriteLine("Injector MVID=" + a.MainModule.Mvid);
            NativeSettingsJsonBoundary.Configure(a);
            SubstitutePitInputs(a.MainModule); ConfigureSupportText(a.MainModule);
            ConfigureInventoryRescanPluginFixture(a.MainModule);
            var main = a.MainModule.GetType("NGUInjector.Main");
            var focus = main.Methods.Single(x => x.Name == "FocusSetupCurrent"); var il = focus.Body.GetILProcessor();
            foreach (var ret in focus.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
            {
                il.InsertBefore(ret, il.Create(OpCodes.Dup)); il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
                il.InsertBefore(ret, il.Create(OpCodes.Call, a.MainModule.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(ObserveNativeFocusAuthority), All))));
            }
            foreach (var instruction in main.Methods.Single(x => x.Name == "RequestMoneyPit").Body.Instructions)
            {
                var call = instruction.Operand as MethodReference;
                if (call?.DeclaringType.FullName != "UnityEngine.UI.Text" || call.Name != "get_text") continue;
                instruction.OpCode = OpCodes.Call; instruction.Operand = a.MainModule.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(PitTextRead), All));
            }
            var pit = main.Methods.Single(x => x.Name == "RequestMoneyPit");
            var acquired = pit.Body.Instructions.Single(x => (x.Operand as MethodReference)?.Name == "AcquireFiniteActionSetup");
            pit.Body.GetILProcessor().InsertAfter(acquired, Instruction.Create(OpCodes.Call,
                a.MainModule.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(PitSetupObservation), All))));
            var invoked = pit.Body.Instructions.Single(x => (x.Operand as MethodReference)?.Name == "RunFiniteAction");
            var duplicate = Instruction.Create(OpCodes.Dup); pit.Body.GetILProcessor().InsertAfter(invoked, duplicate);
            pit.Body.GetILProcessor().InsertAfter(duplicate, Instruction.Create(OpCodes.Call,
                a.MainModule.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(PitResultObservation), All))));
            var cancellations = 0;
            foreach (var method in a.MainModule.Types.SelectMany(Types).SelectMany(x => x.Methods).Where(x => x.HasBody && x.Name.Contains("<Unload>")))
            foreach (var instruction in method.Body.Instructions)
            {
                var call = instruction.Operand as MethodReference;
                if (call?.DeclaringType.FullName != "UnityEngine.MonoBehaviour" || call.Name != "CancelInvoke" && call.Name != "StopAllCoroutines") continue;
                instruction.OpCode = OpCodes.Pop; instruction.Operand = null; cancellations++;
            }
            Equal(2, cancellations, "only unavailable native unload cancellation leaves substituted");
            using (var bytes = new MemoryStream()) { a.Write(bytes); return Assembly.Load(bytes.ToArray()); }
        }
    }

    static void Setup()
    {
        // A fresh native world and temporary directory per scenario. Static managers
        // carry no restoration authority between worlds.
        var directory = Path.Combine(Root, "world-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        S(typeof(Program), "Temp", directory); P("Setup"); Program.NativeGearSwapped = null;
        S(MainType, "OutputWriter", null); S(MainType, "PitSpinWriter", null);
        foreach (var name in new[] { "beards", "diggers", "adventure", "yggdrasil" }) Child(G(Character, "buttons"), name);
        foreach (var field in Character.GetType().GetFields(All).Where(x => !x.IsStatic && x.FieldType.IsClass
            && x.FieldType.Assembly == Game && x.GetValue(Character) == null && x.Name.EndsWith("Controller"))) Child(Character, field.Name);
        Child(Character, "machine");
        var r3 = Child(Character, "res3"); S(r3, "capRes3", 1000L); S(r3, "curRes3", 1000L); S(r3, "idleRes3", 1000L);
        P("ListField", Child(Character, "hacks"), "hacks", 2); P("ListField", Child(Character, "bloodMagic"), "ritual", 0);
        var dc = G(Character, "allDiggers");
        S(dc, "baseGPSDrain", Enumerable.Repeat(1d, 12).ToList()); S(dc, "gpsGrowthRate", Enumerable.Repeat(2d, 12).ToList());
        S(dc, "consumesGPS", Enumerable.Repeat(true, 12).ToList()); S(dc, "startingBoost", Enumerable.Repeat(0d, 12).ToList());
        S(dc, "boostPerLevel", Enumerable.Repeat(0d, 12).ToList()); S(dc, "baseGoldCost", Enumerable.Repeat(1e200, 12).ToList());
        foreach (var name in new[] { "allBeards", "allDiggers", "timeMachineController", "bloodMagicController", "NGUController" }) S(G(Character, name), "character", Character);
        S(G(Character, "allBeards"), "usesEnergy", new bool[7]); S(G(Character, "machine"), "realBaseGold", 100d);
        S(G(G(Character, "allChallenges"), "timeMachineChallenge"), "character", Character);
        S(G(G(Character, "buttons"), "beards"), "m_Interactable", false); S(G(G(Character, "buttons"), "diggers"), "m_Interactable", false);
        S(Child(Character, "adventure"), "zone", -1);
        Setting("SpecialBoostBlacklist", new int[0]); Setting("UpgradeDiggers", false); Setting("CastBloodSpells", false);
        Invoke(Manager("GearOptimizer"), "ClearCache"); ResetPitInputs(); DiggerRendered = null; DiggerRenders = 0;
    }

    static object Domain(string names) => Enum.Parse(MainType.GetNestedType("ExecutionDomain", All), names);
    static object ExactSetup(Array map, string owner, Func<bool> current = null)
    {
        var model = Character; var coordinator = MainObject;
        var setup = Activator.CreateInstance(MainType.GetNestedType("SelectedSetupRequirements", All), true);
        S(setup, "Owner", owner); S(setup, "Acquisition", owner); S(setup, "Gear", map);
        S(setup, "Support", G(M("CaptureSupportState"), "Selection"));
        S(setup, "Recall", Enum.Parse(Manager("GearRecallFootprint"), "None"));
        S(setup, "Authorized", current ?? (Func<bool>)(() => ReferenceEquals(Character, model) && ReferenceEquals(MainObject, coordinator)));
        return setup;
    }
    static Array WithGear(params object[] destinations) => (Array)Invoke(Manager("LoadoutManager"), "ComposeExactLoadout", CapturePitGear(), PitMap(destinations));
    static object BeginClaim(string owner, string domains = "Gear, Support, Route", Func<bool> current = null) =>
        M("BeginExecutionClaim", owner, owner + ":attempt", Domain(domains), current ?? (Func<bool>)(() => true));
    static object ApplyClaim(object claim, Array map)
    {
        var setup = ExactSetup(map, (string)G(claim, "Owner"), () => (bool)M("ExecutionClaimCurrent", claim));
        M("ApplyExecutionClaim", claim, setup); return setup;
    }
    static void Publish(Array map, string owner = "current-B", Func<bool> current = null)
    { M("PublishExecutionBaseline", ExactSetup(map, owner, current)); }
    static void Case(Action action) { action(); Scenarios++; }
    static void Boundary(ModuleDefinition module, MethodDefinition method, string name)
    {
        method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
        foreach (var parameter in method.Parameters) il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(OwnershipChecks).GetMethod(name, All))); il.Emit(OpCodes.Ret);
    }
    static void GameBoundary(ModuleDefinition module, MethodDefinition method, string name)
    {
        method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Call, module.ImportReference(typeof(OwnershipChecks).GetMethod(name, All))); il.Emit(OpCodes.Ret);
    }
    public static void ObserveNativeFocusAuthority(bool current, object selection)
    {
        if (ObserveFocusAuthority && !current && selection != null) FocusAuthorityFailures.Add("requested=" + G(MainObject, "_customRequestedFocusGoal") + "; acquired=" + G(selection, "Goal"));
    }
    public static void NativeHeadRefresh() { P("RefreshBonuses"); }
    static void Constant(MethodDefinition method, double value) { P("Constant", method, value); }
    static object Helper(object equipment, int slot)
    {
        var item = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ih", true));
        S(item, "equipment", equipment); S(item, "slot", slot); S(item, "id", G(equipment, "id")); S(item, "name", "fixture"); return item;
    }
    static object NewGear(int id, string type)
    {
        var item = P("Blank", G(G(Character, "inventory"), "head").GetType());
        S(item, "id", id); S(item, "bossRequired", 1); S(item, "removable", true); P("SetEnum", item, "type", type); return item;
    }
    static object NewPlan() => Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlan", true));
    static IEnumerable<TypeDefinition> Types(TypeDefinition t) { yield return t; foreach (var nested in t.NestedTypes.SelectMany(Types)) yield return nested; }
    static void Setting(string name, object value) { S(Settings, name, value); }
    static object Child(object target, string name) => P("Child", target, name);
    static object M(string name, params object[] args) => Invoke(MainObject, name, args);
    static object P(string name, params object[] args) => typeof(Program).GetMethods(All).Single(x => x.Name == name && x.GetParameters().Length == args.Length).Invoke(null, args);
    static object G(object target, string name) => P("Get", target, name);
    static void S(object target, string name, object value) { if (target == null) throw new InvalidOperationException("Missing fixture parent for " + name); P("Set", target, name, value); }
    static object Invoke(object target, string name, params object[] args) => P("Invoke", target, name, args);
    static void Check(bool value, string name) { Checks++; if (!value) throw new Exception("ASSERTION: " + name); }
    static void Equal(object expected, object actual, string name) { Check(Equals(expected, actual), name + ": expected=" + expected + ", actual=" + actual); }
    static Exception Failure(Action action)
    { try { action(); return null; } catch (Exception error) { while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException; return error; } }
    static void Reject(Action action, string name) { Check(Failure(action) != null, name); }
}
