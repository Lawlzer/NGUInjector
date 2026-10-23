using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Reflection adapters expose the old case inputs, not replacement game accounting or execution policy.
public sealed class NativeBoundary : IDisposable
{
    internal const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly Type Fixture = typeof(PresetIntegrationChecks);
    private static readonly Type FiniteFixture = Fixture.Assembly.GetType("Program", true);
    internal static Assembly Game, Plugin;
    internal static Type MainType;
    internal static object MainInstance;
    internal static readonly List<object> Results = new();
    internal static Action CostObserved;
    internal static bool InAutoExp;
    internal static int TotalStatCalls;
    internal static Purchases Active;
    private static bool privatePending, rendering;
    private readonly string temp;

    private NativeBoundary(string path) { temp = path; }

    internal static NativeBoundary Initialize(string plugin, string managed)
    {
        var temp = Path.Combine(Path.GetTempPath(), "opencode", "AutoExp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var lifetime = new NativeBoundary(temp);
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
                var name = new AssemblyName(request.Name).Name;
                if (name == "Assembly-CSharp") return Game;
                if (name == "UnityEngine.UI") return (Assembly)Invoke(Fixture, "LoadUi", Path.Combine(managed, name + ".dll"));
                var path = Path.Combine(managed, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            // Add boundary observers before the existing fixture substitutes scene rendering.
            var gamePath = Path.Combine(temp, "Assembly-CSharp.dll");
            using (var image = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll")))
            {
                var r3 = image.MainModule.GetType("Character").Fields.Single(x => x.Name == "res3Purchases").FieldType.FullName;
                foreach (var lane in new[] { "EnergyPurchases", "MagicPurchases", r3 })
                {
                    var type = image.MainModule.Types.Single(x => x.FullName == lane);
                    foreach (var method in type.Methods.Where(x => x.HasBody && x.Name.StartsWith("buyCustom", StringComparison.Ordinal)))
                    {
                        if (lane == r3) { Entry(image.MainModule, method, nameof(ForbiddenR3), false); continue; }
                        var kind = method.Name == "buyCustomPower" ? 0 : method.Name == "buyCustomCap" ? 1 : method.Name == "buyCustomBar" ? 2 : -1;
                        if (kind < 0) continue;
                        var first = method.Body.Instructions[0]; var il = method.Body.GetILProcessor();
                        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                        il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4, (lane == "MagicPurchases" ? 3 : 0) + kind));
                        il.InsertBefore(first, Instruction.Create(OpCodes.Call, image.MainModule.ImportReference(typeof(NativeBoundary).GetMethod(nameof(PurchaseStarting)))));
                        var debits = method.Body.Instructions.Where(x => x.OpCode == OpCodes.Stfld && x.Operand is FieldReference field && field.Name == "realExp").ToArray();
                        if (debits.Length != 1) throw new InvalidOperationException("Expected one native EXP debit in " + method.FullName);
                        il.InsertAfter(debits[0], Instruction.Create(OpCodes.Call, image.MainModule.ImportReference(typeof(NativeBoundary).GetMethod(nameof(ExpWritten)))));
                    }
                    if (lane == r3)
                    {
                        foreach (var method in type.Methods.Where(x => x.HasBody && x.Name.StartsWith("custom", StringComparison.Ordinal) && x.Name.EndsWith("Cost", StringComparison.Ordinal)))
                            Entry(image.MainModule, method, nameof(ForbiddenR3), false);
                        continue;
                    }
                    Entry(image.MainModule, type.Methods.Single(x => x.Name == "refresh"), nameof(RefreshStarting), true);
                    foreach (var method in type.Methods.Where(x => new[] { "customPowerCost", "customCapCost", "customBarCost" }.Contains(x.Name)))
                    foreach (var ret in method.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
                    {
                        var il = method.Body.GetILProcessor(); ret.OpCode = OpCodes.Ldarg_0;
                        var amount = Instruction.Create(OpCodes.Ldarg_1); il.InsertAfter(ret, amount);
                        var convert = Instruction.Create(OpCodes.Conv_I8); il.InsertAfter(amount, convert);
                        var kind = Instruction.Create(OpCodes.Ldc_I4, method.Name == "customPowerCost" ? 0 : method.Name == "customCapCost" ? 1 : 2);
                        il.InsertAfter(convert, kind);
                        var call = Instruction.Create(OpCodes.Call, image.MainModule.ImportReference(typeof(NativeBoundary).GetMethod(nameof(CostReturned))));
                        il.InsertAfter(kind, call); il.InsertAfter(call, Instruction.Create(OpCodes.Ret));
                    }
                }
                foreach (var name in new[] { "totalEnergyPower", "totalCapEnergy", "totalEnergyBar", "totalMagicPower", "totalCapMagic", "totalMagicBar" })
                foreach (var method in image.MainModule.GetType("Character").Methods.Where(x => x.Name == name && x.HasBody))
                    Entry(image.MainModule, method, nameof(TotalStatReading), false);
                // Hardcap values are controlled scalar inputs; native cost/purchase bodies stay intact.
                foreach (var name in new[] { "hardCap", "hardCapPowBar" })
                foreach (var ret in image.MainModule.GetType("Character").Methods.Single(x => x.Name == name).Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
                {
                    ret.OpCode = OpCodes.Call;
                    ret.Operand = image.MainModule.ImportReference(typeof(NativeBoundary).GetMethod(name == "hardCap" ? nameof(CapLimit) : nameof(PowerLimit)));
                    image.MainModule.GetType("Character").Methods.Single(x => x.Name == name).Body.GetILProcessor().InsertAfter(ret, Instruction.Create(OpCodes.Ret));
                }
                image.Write(gamePath);
            }
            Game = (Assembly)Invoke(Fixture, "LoadGame", gamePath);
            Set(Fixture, "Game", Game);
            var pluginPath = Path.Combine(temp, "NGUInjector.dll");
            using (var image = AssemblyDefinition.ReadAssembly(plugin))
            {
                var main = image.MainModule.GetType("NGUInjector.Main");
                foreach (var name in new[] { "Log", "LogError" })
                {
                    var method = main.Methods.Single(x => x.Name == name);
                    method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
                    foreach (var parameter in method.Parameters) il.Emit(OpCodes.Ldarg, parameter);
                    il.Emit(OpCodes.Call, image.MainModule.ImportReference(typeof(NativeBoundary).GetMethod(name == "Log" ? nameof(Log) : nameof(LogError)))); il.Emit(OpCodes.Ret);
                }
                var runner = main.Methods.Single(x => x.Name == "RunFiniteAction");
                foreach (var ret in runner.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
                {
                    var il = runner.Body.GetILProcessor(); ret.OpCode = OpCodes.Dup;
                    var observe = Instruction.Create(OpCodes.Call, image.MainModule.ImportReference(typeof(NativeBoundary).GetMethod(nameof(ResultObserved))));
                    il.InsertAfter(ret, observe); il.InsertAfter(observe, Instruction.Create(OpCodes.Ret));
                }
                image.Write(pluginPath);
            }
            Plugin = (Assembly)Invoke(Fixture, "LoadPlugin", pluginPath);
            MainType = Plugin.GetType("NGUInjector.Main", true);
            foreach (var owner in new[] { Fixture, FiniteFixture })
            {
                Set(owner, "Game", Game); Set(owner, "Plugin", Plugin); Set(owner, "MainType", MainType);
            }
            Set(FiniteFixture, "Temp", temp);
            Set(FiniteFixture, "FinitePurchaseRendered", (Action)Presentation);
            Console.WriteLine("AutoExp uses compiled Main.CustomExp, Main.PurchaseActions, Main.FiniteActions, execution core and disk ledger; native game purchases. No live actions.");
            return lifetime;
        }
        catch { lifetime.Dispose(); throw; }
    }

    private static void Entry(ModuleDefinition module, MethodDefinition method, string callback, bool instance)
    {
        var first = method.Body.Instructions[0]; var il = method.Body.GetILProcessor();
        if (instance) il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, Instruction.Create(OpCodes.Call, module.ImportReference(typeof(NativeBoundary).GetMethod(callback))));
    }

    internal static object Reset()
    {
        CostObserved = null; Results.Clear(); privatePending = rendering = InAutoExp = false; TotalStatCalls = 0; Active = null;
        Invoke(FiniteFixture, "Setup");
        var character = Get(FiniteFixture, "Character"); MainInstance = Get(FiniteFixture, "MainInstance");
        Set(Fixture, "Character", character); Set(Fixture, "MainInstance", MainInstance); Set(Fixture, "Controller", Get(FiniteFixture, "Controller"));
        Invoke(Fixture, "PrepareSupport");
        Set(Fixture, "InlineObservedError", null);
        return character;
    }

    public static long CapLimit(long native) => AutoExpGame.Character?.CapLimit ?? native;
    public static long PowerLimit(long native) => AutoExpGame.Character?.PowerBarLimit ?? native;
    public static void Log(string message) => AutoExpGame.Logs.Add(message);
    public static string LogError(string context, Exception error)
    {
        AutoExpGame.Errors.Add((context, error));
        return "ERROR in " + context + ": " + error.Message;
    }
    public static void ResultObserved(object result) => Results.Add(result);
    public static void TotalStatReading()
    {
        if (!InAutoExp) return;
        TotalStatCalls++;
        throw new InvalidOperationException("AutoExp must read BASE, not total stats");
    }
    public static void ForbiddenR3() { AutoExpGame.Character.res3Purchases.Calls++; throw new InvalidOperationException("R3 purchase forbidden"); }
    public static void ExpWritten()
    {
        var c = AutoExpGame.Character;
        c.MinimumExp = Math.Min(c.MinimumExp, c.realExp);
    }
    public static void RefreshStarting(object controller) { Active = Controller(controller); privatePending = false; }
    private static Purchases Controller(object controller) => ReferenceEquals(controller, AutoExpGame.Character.energyPurchases?.Native)
        ? AutoExpGame.Character.energyPurchases : AutoExpGame.Character.magicPurchases;
    public static long CostReturned(long cost, object controller, long amount, int kind)
    {
        var boundary = Controller(controller);
        boundary.CostQueries.Add((kind == 0 ? "power" : kind == 1 ? "cap" : "bar", amount, rendering));
        var result = boundary.LastCost = cost + boundary.CostOffset;
        if (!rendering) CostObserved?.Invoke();
        return result;
    }
    public static void PurchaseStarting(object controller, int stat)
    {
        Active = Controller(controller); privatePending = true;
        var c = AutoExpGame.Character; var amount = c.settings.Inputs()[stat];
        var cost = Active.LastCost;
        c.Calls.Add((stat, amount, cost, c.settings.Inputs()));
        c.MinimumExp = Math.Min(c.MinimumExp, c.realExp);
        if (Active.ThrowBeforeStat == stat) throw new InvalidOperationException("private purchase before mutation " + stat);
    }
    public static void Presentation()
    {
        var c = AutoExpGame.Character; var boundary = Active;
        var pending = privatePending; privatePending = false;
        if (pending) { boundary.AfterPurchase?.Invoke(); c.MinimumExp = Math.Min(c.MinimumExp, c.realExp); }
        boundary.RefreshCalls++;
        if (c.menuID == (boundary.Magic ? 32 : 13))
        {
            var amounts = c.settings.Inputs().Skip(boundary.Magic ? 3 : 0).Take(3).ToArray();
            rendering = true;
            try
            {
                var costs = amounts.Select((amount, kind) => boundary.Cost(kind, amount)).ToArray();
                boundary.Display = new PurchaseDisplay(amounts.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray(), costs, costs.Sum());
                boundary.Displays.Add(boundary.Display);
            }
            finally { rendering = false; }
        }
        if (pending && boundary.ThrowAfterStat == c.Calls.Last().Stat)
            throw new InvalidOperationException("private purchase after mutation " + c.Calls.Last().Stat);
        boundary.Presented?.Invoke();
    }

    internal static object Call(string method, params object[] args) => Invoke(MainInstance, method, args);
    internal static object Domain(string value) => Enum.Parse(MainType.GetNestedType("ExecutionDomain", All), value);
    internal static object Invoke(object target, string name, params object[] args)
    {
        var methods = (target as Type ?? target.GetType()).GetMethods(All).Where(x => x.Name == name).ToArray();
        var method = methods.SingleOrDefault(x => x.GetParameters().Length == args.Length)
            ?? methods.Single(x => x.GetParameters().Length > args.Length && x.GetParameters().Skip(args.Length).All(p => p.IsOptional));
        args = args.Concat(method.GetParameters().Skip(args.Length).Select(x => x.DefaultValue)).ToArray();
        try { return method.Invoke(method.IsStatic ? null : target, args); }
        catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    internal static FieldInfo Field(object target, string name)
    {
        for (var type = target as Type ?? target.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, All); if (field != null) return field; }
        throw new MissingFieldException(name);
    }
    internal static object Get(object target, string name) => Field(target, name).GetValue(target is Type ? null : target);
    internal static void Set(object target, string name, object value)
    {
        var field = Field(target, name);
        if (value != null && !field.FieldType.IsInstanceOfType(value)) value = Convert.ChangeType(value, field.FieldType);
        field.SetValue(target is Type ? null : target, value);
    }
    internal static object Child(object target, string name) => Invoke(Fixture, "Child", target, name);
    internal static object Property(object target, string name) => target.GetType().GetProperty(name, All).GetValue(target);
    public void Dispose() { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
}

internal sealed class AutoExpGame
{
    private static Character character;
    internal static Character Character { get => character; set { character = value; NativeBoundary.Set(NativeBoundary.MainType, "Character", value?.Native); } }
    internal static ExpSettings Settings;
    internal static readonly List<string> Logs = new();
    internal static readonly List<(string Context, Exception Error)> Errors = new();
    internal void Tick()
    {
        NativeBoundary.InAutoExp = true;
        try { NativeBoundary.Call("RunCustomAutoExp"); }
        finally { NativeBoundary.InAutoExp = false; }
    }
}

internal abstract class NativeObject
{
    internal readonly object Native;
    protected NativeObject(object native) { Native = native; }
    protected T Get<T>(string name) => (T)Convert.ChangeType(NativeBoundary.Get(Native, name), typeof(T));
    protected void Set(string name, object value) => NativeBoundary.Set(Native, name, value);
}

internal sealed class ExpSettings : NativeObject
{
    internal ExpSettings() : base(NativeBoundary.Get(NativeBoundary.MainType, "Settings"))
    { CustomAutoExpEnabled = true; SetRatios(new[] { 5, 1, 5, 160000, 4, 5, 160000, 4 }); }
    internal bool CustomAutoExpEnabled { get => Get<bool>("_customAutoExpEnabled"); set => Set("_customAutoExpEnabled", value); }
    internal void SetRatios(int[] values)
    {
        var names = new[] { "Energy", "Magic", "EnergyPower", "EnergyCap", "EnergyBars", "MagicPower", "MagicCap", "MagicBars" };
        for (var i = 0; i < names.Length; i++) Set("_customExp" + names[i] + "Ratio", values[i]);
    }
}

internal sealed class GameSettings : NativeObject
{
    internal GameSettings(object native) : base(native) { }
    internal static readonly string[] Fields = { "customPowerAmount", "customCapAmount", "customBarAmount", "customMagicPowerAmount", "customMagicCapAmount", "customMagicBarAmount", "customRes3PowerAmount", "customRes3CapAmount", "customRes3BarAmount" };
    internal long[] Inputs() => Fields.Select(x => Get<long>(x)).ToArray();
    internal bool expPopups => Get<bool>("expPopups");
}

internal sealed class MagicState : NativeObject
{
    internal MagicState(object native) : base(native) { }
    internal float magicPower { get => Get<float>("magicPower"); set => Set("magicPower", value); }
    internal long capMagic { get => Get<long>("capMagic"); set => Set("capMagic", value); }
    internal long magicPerBar { get => Get<long>("magicPerBar"); set => Set("magicPerBar", value); }
    internal float magicBarSpeed => Get<float>("magicBarSpeed");
}

internal sealed class Res3State : NativeObject
{
    internal Res3State(object native) : base(native) { }
    internal float res3Power => Get<float>("res3Power");
    internal long capRes3 => Get<long>("capRes3");
    internal long res3PerBar => Get<long>("res3PerBar");
}

internal sealed class Character : NativeObject
{
    internal readonly List<(int Stat, long Amount, long Cost, long[] Inputs)> Calls = new();
    internal readonly R3Purchases res3Purchases = new();
    internal long CapLimit = 1000000000000000000L, PowerBarLimit = 1000000000000000000L, MinimumExp = 13680;
    private EnergyPurchases energy;
    private MagicPurchases magicController;
    internal Character() : base(NativeBoundary.Reset())
    {
        highestBoss = 37; menuID = 13; energyPower = 25; capEnergy = 800000; energyBars = 20;
        magic.magicPower = 5; magic.capMagic = 160000; magic.magicPerBar = 4;
        energy = new EnergyPurchases(NativeBoundary.Child(Native, "energyPurchases"));
        magicController = new MagicPurchases(NativeBoundary.Child(Native, "magicPurchases"));
        NativeBoundary.Set(NativeBoundary.Child(Native, "res3Purchases"), "character", Native);
        foreach (var controller in new Purchases[] { energy, magicController })
        {
            NativeBoundary.Set(controller.Native, "character", Native); NativeBoundary.Child(controller.Native, "tooltip");
            foreach (var field in new[] { "powerInput", "capInput", "barInput" }) NativeBoundary.Child(controller.Native, field);
        }
        var original = new long[] { -17, 3000000123, 29, 43, 4000000321, -59, 61, 5000000456, 67 };
        for (var i = 0; i < original.Length; i++) NativeBoundary.Set(settings.Native, GameSettings.Fields[i], original[i]);
        NativeBoundary.Set(Native, "energySpeed", 41f); NativeBoundary.Set(magic.Native, "magicBarSpeed", 37f);
        if (NativeBoundary.Get(Native, "res3") == null) NativeBoundary.Child(Native, "res3");
        NativeBoundary.Set(res3.Native, "res3Power", 71f); NativeBoundary.Set(res3.Native, "capRes3", 730000L); NativeBoundary.Set(res3.Native, "res3PerBar", 79L);
        NativeBoundary.Set(settings.Native, "expPopups", true);
    }
    internal GameSettings settings { get { var value = NativeBoundary.Get(Native, "settings"); return value == null ? null : new GameSettings(value); } set => Set("settings", value?.Native); }
    internal MagicState magic { get { var value = NativeBoundary.Get(Native, "magic"); return value == null ? null : new MagicState(value); } set => Set("magic", value?.Native); }
    internal Res3State res3 => new(NativeBoundary.Get(Native, "res3"));
    internal EnergyPurchases energyPurchases { get => energy; set { energy = value; Set("energyPurchases", value?.Native); } }
    internal MagicPurchases magicPurchases { get => magicController; set { magicController = value; Set("magicPurchases", value?.Native); } }
    internal int highestBoss { get => Get<int>("highestBoss"); set => Set("highestBoss", value); }
    internal int menuID { get => Get<int>("menuID"); set => Set("menuID", value); }
    internal float energyPower { get => Get<float>("energyPower"); set => Set("energyPower", value); }
    internal float energySpeed => Get<float>("energySpeed");
    internal long capEnergy { get => Get<long>("capEnergy"); set => Set("capEnergy", value); }
    internal long energyBars { get => Get<long>("energyBars"); set => Set("energyBars", value); }
    internal long realExp { get => Get<long>("realExp"); set { Set("realExp", value); MinimumExp = Math.Min(MinimumExp, value); } }
    internal decimal[] BaseStats() => new[] { (decimal)(double)energyPower, capEnergy, energyBars,
        magic == null ? 0m : (decimal)(double)magic.magicPower, magic?.capMagic ?? 0m, magic?.magicPerBar ?? 0m };
}

internal sealed class R3Purchases { internal int Calls; }
internal sealed class PurchaseInput : NativeObject
{
    internal PurchaseInput(object native) : base(native) { }
    internal bool isFocused { get => (bool)NativeBoundary.Property(Native, "isFocused"); set => Set("m_AllowInput", value); }
}
internal sealed class PurchaseDisplay
{
    internal readonly string[] Inputs;
    internal readonly long[] Costs;
    internal readonly long AllCost;
    internal PurchaseDisplay(string[] inputs, long[] costs, long allCost) { Inputs = inputs; Costs = costs; AllCost = allCost; }
}
internal abstract class Purchases : NativeObject
{
    internal readonly bool Magic;
    internal int? ThrowBeforeStat, ThrowAfterStat;
    internal long CostOffset;
    internal long LastCost;
    internal Action AfterPurchase, Presented;
    internal readonly List<(string Api, long Amount, bool Rendering)> CostQueries = new();
    internal readonly List<PurchaseDisplay> Displays = new();
    internal PurchaseDisplay Display;
    internal int RefreshCalls;
    protected Purchases(object native, bool magic) : base(native) { Magic = magic; }
    internal PurchaseInput powerInput => new(NativeBoundary.Get(Native, "powerInput"));
    internal PurchaseInput capInput => new(NativeBoundary.Get(Native, "capInput"));
    internal PurchaseInput barInput => new(NativeBoundary.Get(Native, "barInput"));
    internal void refresh() => NativeBoundary.Invoke(Native, "refresh");
    internal long Cost(int kind, long amount) => (long)NativeBoundary.Invoke(Native, kind == 0 ? "customPowerCost" : kind == 1 ? "customCapCost" : "customBarCost", kind == 1 ? (object)amount : (int)amount);
}
internal sealed class EnergyPurchases : Purchases { internal EnergyPurchases(object native) : base(native, false) { } }
internal sealed class MagicPurchases : Purchases { internal MagicPurchases(object native) : base(native, true) { } }
