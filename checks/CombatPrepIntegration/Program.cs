using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Runs the BUILT backend and actual game formula getters. Only scene/UI, external time/RNG, input-slot counts,
// unrelated recurring input getters and explicit resource-recall boundaries are replaced in memory.
// Desired-map accounting stays real, with in-memory before/after observers and a preflight observation fault.
// No production DLL, save, running game, planner, capture, allocation or execution policy is patched.
public static partial class Program
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Assembly Plugin, Game;
    private static Type MainType;
    private static object MainInstance, Character, Controller, Settings;
    private static string Temp;
    private static int Assertions, Recalls, MagicRecalls, Swaps;
    private static int FailRefreshAtSwap = -1;
    private static string RefreshFailureInterference;
    private static object FailedApplicationMap;
    private static float NativeBossDeltaSeconds = .25f;
    private static MemoryStream PreemptionLog;
    private static string RequiredDiagnosticBeforeResourceMutation;
    private static int ResourceDiagnosticChecks, ResourceMissingDiagnostics;
    private static int ResourceCommittedWrites, DesiredEnergyPreflightFailures;
    private static bool FailDesiredEnergyPreflight;
    private static Action<object, int, int, long, long> ResourceModelWriteBefore = null, ResourceModelWriteAfter = null;
    private static Action<object, int, int> ResourcePresentationStarting = null;
    private static bool OffhandUnlocked;
    private static float OffhandFactor;
    private static int BeardSlots = 4, DiggerSlots = 1;
    private static bool FailEnergyRecall;
    private static double? ControlledCombatPrepClock;
    private static Action CombatPrepClockRead;

    public static int Main(string[] args)
    {
        try
        {
            var pluginPath = Path.GetFullPath(args[0]); var managed = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
                if (Game != null && new AssemblyName(e.Name).Name == "Assembly-CSharp") return Game;
                if (new AssemblyName(e.Name).Name == "UnityEngine.UI")
                    return (Assembly)Invoke(typeof(Program), "LoadSupportUi", Path.Combine(managed, "UnityEngine.UI.dll"));
                var name = new AssemblyName(e.Name).Name + ".dll";
                var path = Path.Combine(managed, name);
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Game = LoadFixtureGame(Path.Combine(managed, "Assembly-CSharp.dll"));
            Plugin = LoadFixturePlugin(pluginPath); MainType = Plugin.GetType("NGUInjector.Main", true);
            Console.WriteLine("Injector MVID=" + Plugin.ManifestModule.ModuleVersionId);
            Temp = Path.Combine(Path.GetTempPath(), "opencode", "CombatPrepIntegration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Temp);
            Setup();
            if (args.Length == 4 && args[2] == "--check" && args[3].StartsWith("Check", StringComparison.Ordinal))
            {
                Invoke(typeof(Program), args[3]);
                Console.WriteLine("PASS: " + Assertions + " native assertions in " + args[3] + "; no live actions.");
                return 0;
            }
            if (args.Contains("--resource-domains"))
            {
                Invoke(typeof(Program), "CheckSharedResourceDomains");
                Invoke(typeof(Program), "CheckSharedCombatResourceExecution");
                Console.WriteLine("PASS: " + Assertions + " compiled shared resource-domain assertions; no live actions.");
                return 0;
            }
            if (args.Contains("--resource-regressions"))
            {
                Invoke(typeof(Program), "CheckLiveResourceRegressions");
                Console.WriteLine("PASS: " + Assertions + " compiled resource allocation regressions; no live actions.");
                return 0;
            }
            if (args.Contains("--adventure-weight"))
            {
                Invoke(typeof(Program), "CheckAdventureWeightRegressions");
                Console.WriteLine("PASS: " + Assertions + " adventure-weight assertions against compiled backend. No live game actions.");
                return 0;
            }
            if (args.Contains("--automatic-reconciliation"))
            {
                Invoke(typeof(Program), "CheckAutomaticReconciliationRegressions");
                Console.WriteLine("PASS: " + Assertions + " automatic reconciliation assertions against compiled backend. No live game actions.");
                return 0;
            }
            if (args.Contains("--preset-interruption"))
            {
                Invoke(typeof(Program), "CheckPresetInterruptionRegressions");
                Console.WriteLine("PASS: " + Assertions + " preset interruption assertions against compiled backend. No live game actions.");
                return 0;
            }
            if (args.Contains("--auto-boost") || args.Contains("--auto-boost-native"))
            {
                Invoke(typeof(Program), args.Contains("--auto-boost-native") ? "CheckNativeAutoBoostConsumption" : "CheckAutoBoostRegressions");
                Console.WriteLine("PASS: " + Assertions + " auto-boost assertions against compiled backend/native inventory. No live game actions.");
                return 0;
            }
            CheckCaptureAndPreview();
            CheckOptionalGearAvailability();
            Invoke(typeof(Program), "CheckPhaseSupport");
            CheckProjectedStats();
            CheckReservedBasicPool();
            Invoke(typeof(Program), "CheckNativeCombatProgression");
            CheckOsBases();
            CheckAllocation();
            CheckNguRemainders();
            CheckTokensAndRun();
            CheckAugmentGate();
            CheckFreshOsPin();
            CheckEpochAndOwnership();
            CheckImplicitAtPolicy();
            CheckExternalEdits();
            CheckFutureTargetEdits();
            CheckTerminationOwnership();
            CheckManualAdventureRestoration();
            CheckFailedGearAcquisition();
            CheckCompletePhaseMaps();
            CheckSparseAccessoryCapture();
            CheckNormalAtEntry();
            CheckPrimaryAtPolicy();
            CheckHeldRefresh();
            CheckUsedProfileDepletion();
            CheckRawBossBaseline();
            CheckRuntimeObservations();
            CheckPreviewResponseEnvelope();
            CheckNativeAutoNukePause();
            Invoke(typeof(Program), "CheckScheduledCombatPrep");
            Invoke(typeof(Program), "CheckAutomaticReconciliationRegressions");
            Invoke(typeof(Program), "CheckReviewRegressions");
            Invoke(typeof(Program), "CheckCleanupRecoveryRegressions");
            Invoke(typeof(Program), "CheckEquipmentReplanRegressions");
            Invoke(typeof(Program), "CheckAutomaticEntryRegressions");
            Invoke(typeof(Program), "CheckLiveResourceRegressions");
            Invoke(typeof(Program), "CheckAutoBoostRegressions");
            var beforeActivityTiming = Assertions;
            Invoke(typeof(Program), "CheckActivityTimingRegressions");
            Console.WriteLine("Activity timing regressions: " + (Assertions - beforeActivityTiming) + " assertions.");
            Invoke(typeof(Program), "CheckPresetInterruptionRegressions");
            var finalFailures = new List<Exception>();
            foreach (var check in new[] { "CheckAdventureWeightRegressions", "CheckSharedCombatSetup" })
            {
                try { Invoke(typeof(Program), check); }
                catch (Exception error) { finalFailures.Add(error); }
            }
            if (finalFailures.Count != 0) throw new AggregateException("Full combat-prep final integration groups failed; no full pass is claimed.", finalFailures);
            Console.WriteLine("PASS: " + Assertions + " assertions against compiled combat-prep capture/preview/execution and real game formulas.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAILED after " + Assertions + " native assertions (not a pass count)."); Console.Error.WriteLine(error); return 1; }
        finally { if (Temp != null && Directory.Exists(Temp)) Directory.Delete(Temp, true); }
    }

    private static Assembly LoadFixtureGame(string path)
    {
        using (var game = AssemblyDefinition.ReadAssembly(path))
        {
            var supportFixture = typeof(Program).GetMethod("ConfigureSupportFixture", All);
            supportFixture?.Invoke(null, new object[] { game });
            typeof(Program).GetMethod("ConfigureAutoBoostFixture", All)?.Invoke(null, new object[] { game });
            typeof(Program).GetMethod("ConfigureCombatSetupGameFixture", All)?.Invoke(null, new object[] { game });
            var nativeBossTimeInputs = 0;
            var factors = new HashSet<string> { "totalEnergyPowerBonus", "totalMagicPowerBonus", "totalEnergyCapBonus", "totalMagicCapBonus",
                "totalAugSpeedBonus", "totalWandoosSpeedBonus", "totalEnergyWandoosBonus", "totalMagicWandoosBonus", "wandoosBonus", "augmentBonus",
                "getBonus", "augDiscount", "potionModifier" };
            foreach (var name in new[] { "totalBothNGUSpeedBonus", "nguBonus", "totalEnergyNGUBonus", "totalMagicNGUBonus", "energyNGUBonus", "magicNGUBonus",
                "totalEnergyNGUSpeed", "totalMagicNGUSpeed", "statBonus", "totalStatBonus", "permStatBonus", "permStatBonus2", "difficultyModifier", "getTotalAttack", "getTotalDefense" }) factors.Add(name);
            foreach (var type in game.MainModule.Types)
            foreach (var method in type.Methods.Where(x => x.HasBody))
            {
                if (type.Name == "BossController" && method.Name == "Update")
                    foreach (var instruction in method.Body.Instructions)
                    {
                        var called = instruction.Operand as MethodReference;
                        if (called?.DeclaringType.FullName != "UnityEngine.Time" || called.Name != "get_deltaTime") continue;
                        instruction.Operand = game.MainModule.ImportReference(typeof(Program).GetMethod("NativeBossFrameDelta", All));
                        nativeBossTimeInputs++;
                    }
                if (type.Name == "BossController" && method.Name == "updateBossPortrait") Constant(method, 0);
                if (type.Name == "AdvancedTrainingController" && (method.Name == "updateBar" || method.Name == "updateText")) Constant(method, 0);
                if (type.Name == "Wandoos98Controller" && new[] { "advanceBootup", "advanceEnergyProgress", "advanceMagicProgress" }.Contains(method.Name))
                    foreach (var instruction in method.Body.Instructions)
                    {
                        var called = instruction.Operand as MethodReference;
                        if (called?.DeclaringType.FullName != "UnityEngine.UI.Slider" || called.Name != "set_value") continue;
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = game.MainModule.ImportReference(typeof(Program).GetMethod("NativeSliderValue", All));
                    }
                if (factors.Contains(method.Name) && (supportFixture == null || type.Name != "AllBeardsController" && type.Name != "AllGoldDiggerController")
                    && !(type.Name == "Wandoos98Controller" && method.Name == "wandoosBonus")
                    && (method.ReturnType.FullName == "System.Single" || method.ReturnType.FullName == "System.Double"))
                    Constant(method, method.Name == "potionModifier" ? 1.7 : 1);
                if ((method.Name == "completions" || method.Name == "evilCompletions" || method.Name == "sadisticCompletions") && type.Name.Contains("Challenge")) Constant(method, 0);
                if (type.Name == "AllNGUController" && (method.Name == "energySpeedDivider" || method.Name == "magicSpeedDivider")) Constant(method, 1000);
                if (type.Name == "ItopodPerkController" && method.Name == "totalOSLevelBonus") Constant(method, 0);
                if (supportFixture == null && type.Name == "Character" && (method.Name == "grossGoldPerSecond" || method.Name == "totalGPSDrain"))
                    Constant(method, method.Name == "grossGoldPerSecond" ? 100 : 10);
                if (type.Name == "InventoryController" && new[] { "accessorySpaces", "curSpaces", "weapon2Unlocked", "weapon2Factor" }.Contains(method.Name))
                {
                    if (method.Name == "weapon2Unlocked" || method.Name == "weapon2Factor")
                        Boundary(game.MainModule, method, method.Name == "weapon2Unlocked" ? "FixtureOffhandUnlocked" : "FixtureOffhandFactor", false);
                    else Constant(method, method.Name == "accessorySpaces" ? 2 : 10);
                }
                if (type.Name == "InventoryController" && (method.Name == "updateInventory" || method.Name == "updateBonuses"))
                {
                    if (method.Name == "updateBonuses") Boundary(game.MainModule, method, "RefreshBonuses", false);
                    else Constant(method, 0);
                }
                if (type.Name == "InventoryController" && new[] { "swapHead", "swapChest", "swapLegs", "swapBoots", "swapWeapon", "swapWeapon2", "swapAcc" }.Contains(method.Name)
                    && method.Parameters.Count == 0) Boundary(game.MainModule, method, "SwapGear", false);
                if (type.Name == "Wandoos98Controller" && method.Name == "refreshMenu") Constant(method, 0);
                if (type.Name == "Wandoos98Controller" && method.Name == "updateText") Constant(method, 0);
                if (type.Name == "AllYggdrasil" && (method.Name == "changePage" || method.Name == "refreshMenu")) Constant(method, 0);
                if (type.Name == "FruitController" && method.Name == "updateFruitDisplay") Constant(method, 0);
                if (type.Name == "HoverTooltip" && method.Name == "showTooltip") Constant(method, 0);
                if (type.Name == "Character" && method.Name == "removeAllRes3")
                    Boundary(game.MainModule, method, "RecallRes3", true);
                if (type.Name == "Character" && (method.Name == "removeMostEnergy" || method.Name == "removeMostMagic"))
                    Boundary(game.MainModule, method, method.Name == "removeMostEnergy" ? "RecallEnergy" : "RecallMagic", true);
            }
            if (nativeBossTimeInputs != 1) throw new Exception("Expected exactly one external deltaTime input in native BossController.Update.");
            typeof(Program).GetMethod("ConfigureLiveResourceFixture", All)?.Invoke(null, new object[] { game });
            typeof(Program).GetMethod("ConfigurePresetInterruptionFixture", All)?.Invoke(null, new object[] { game });
            typeof(Program).GetMethod("ConfigureAdventureWeightGameFixture", All)?.Invoke(null, new object[] { game });
            typeof(Program).GetMethod("ConfigureFiniteActionGameFixture", All)?.Invoke(null, new object[] { game });
            typeof(Program).GetMethod("ConfigureRepeatablePurchaseGameFixture", All)?.Invoke(null, new object[] { game });
            using (var bytes = new MemoryStream()) { game.Write(bytes); return Assembly.Load(bytes.ToArray()); }
        }
    }

    private static Assembly LoadFixturePlugin(string path)
    {
        using (var plugin = AssemblyDefinition.ReadAssembly(path))
        {
            ConfigureResourceBoundaryFixture(plugin);
            typeof(Program).GetMethod("ConfigureSettingsJsonFixture", All)?.Invoke(null, new object[] { plugin });
            typeof(Program).GetMethod("ConfigureAutomaticReconciliationFixture", All)?.Invoke(null, new object[] { plugin });
            typeof(Program).GetMethod("ConfigureAdventureWeightObservers", All)?.Invoke(null, new object[] { plugin });
            typeof(Program).GetMethod("ConfigureCombatSetupPluginFixture", All)?.Invoke(null, new object[] { plugin });
            typeof(Program).GetMethod("ConfigureResourceExecutionObservers", All)?.Invoke(null, new object[] { plugin });
            typeof(Program).GetMethod("ConfigureAutoBoostPluginFixture", All)?.Invoke(null, new object[] { plugin });
            // Time is an external boundary; the real lifecycle and timing accumulator stay intact.
            Boundary(plugin.MainModule, plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Main")
                .Methods.Single(x => x.Name == "get_CombatPrepClock"), "FixtureCombatPrepClock", false);
            // Only Unity shutdown leaves are unavailable outside the engine; Unload itself stays real.
            IEnumerable<TypeDefinition> Types(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Types));
            var shutdownLeaves = 0;
            foreach (var method in plugin.MainModule.Types.SelectMany(Types).SelectMany(x => x.Methods).Where(x => x.HasBody && x.Name.Contains("<Unload>")))
            foreach (var instruction in method.Body.Instructions)
            {
                var call = instruction.Operand as MethodReference;
                if (call?.DeclaringType.FullName != "UnityEngine.MonoBehaviour" || call.Name != "CancelInvoke" && call.Name != "StopAllCoroutines") continue;
                instruction.OpCode = OpCodes.Pop; instruction.Operand = null; shutdownLeaves++;
            }
            Equal(2, shutdownLeaves, "only two unavailable Unity shutdown leaves are substituted");
            typeof(Program).GetMethod("ConfigureFiniteActionPluginFixture", All)?.Invoke(null, new object[] { plugin });
            using (var bytes = new MemoryStream()) { plugin.Write(bytes); return Assembly.Load(bytes.ToArray()); }
        }
    }

    private static void ConfigureResourceBoundaryFixture(AssemblyDefinition plugin)
    {
        var module = plugin.MainModule;
        var writer = module.Types.Single(x => x.FullName == "NGUInjector.Managers.ResourceWriter");
        var set = writer.Methods.Single(x => x.Name == "SetAmount");
        var before = new VariableDefinition(module.TypeSystem.Int64); set.Body.Variables.Add(before); set.Body.InitLocals = true;
        var il = set.Body.GetILProcessor(); var first = set.Body.Instructions[0];
        foreach (var instruction in new[] {
            il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Ldarg_1), il.Create(OpCodes.Ldarg_2),
            il.Create(OpCodes.Call, writer.Methods.Single(x => x.Name == "GetAmount")), il.Create(OpCodes.Stloc, before),
            il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Ldarg_1), il.Create(OpCodes.Ldarg_2), il.Create(OpCodes.Ldloc, before), il.Create(OpCodes.Ldarg_3),
            il.Create(OpCodes.Call, module.ImportReference(typeof(Program).GetMethod("ObserveResourceModelWriteBefore", All))) }) il.InsertBefore(first, instruction);
        foreach (var ret in set.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
        {
            // Branches to an existing return must also pass through the observer.
            ret.OpCode = OpCodes.Nop; var cursor = ret;
            foreach (var instruction in new[] { il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Ldarg_1), il.Create(OpCodes.Ldarg_2),
                il.Create(OpCodes.Ldloc, before), il.Create(OpCodes.Ldarg_3),
                il.Create(OpCodes.Call, module.ImportReference(typeof(Program).GetMethod("ObserveResourceModelWriteAfter", All))), il.Create(OpCodes.Ret) })
            { il.InsertAfter(cursor, instruction); cursor = instruction; }
        }
        var reconcile = writer.Methods.Single(x => x.Name == "Reconcile"); il = reconcile.Body.GetILProcessor();
        var capture = reconcile.Body.Instructions.First(x => x.Operand is MethodReference method && method.Name == "CaptureAllocations");
        var index = reconcile.Body.Instructions.IndexOf(capture);
        Equal(true, reconcile.Body.ExceptionHandlers.Any(x => x.HandlerType == ExceptionHandlerType.Catch
            && reconcile.Body.Instructions.IndexOf(x.TryStart) <= index && reconcile.Body.Instructions.IndexOf(x.TryEnd) > index),
            "resource observation fault is inside the real unchanged-preflight rejection boundary");
        il.InsertBefore(capture, il.Create(OpCodes.Ldarg, reconcile.Parameters[1]));
        il.InsertBefore(capture, il.Create(OpCodes.Call, module.ImportReference(typeof(Program).GetMethod("ObserveDesiredResourcePreflight", All))));
        var refresh = writer.Methods.Single(x => x.Name == "Refresh"); il = refresh.Body.GetILProcessor(); first = refresh.Body.Instructions[0];
        foreach (var instruction in new[] { il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Ldarg_1), il.Create(OpCodes.Ldarg_2),
            il.Create(OpCodes.Call, module.ImportReference(typeof(Program).GetMethod("ObserveResourcePresentationStarting", All))) }) il.InsertBefore(first, instruction);
    }

    private static void ObserveResourceDiagnostic()
    {
        if (RequiredDiagnosticBeforeResourceMutation == null) return;
        ResourceDiagnosticChecks++;
        if (PreemptionLog == null || !Encoding.UTF8.GetString(PreemptionLog.ToArray()).Contains(RequiredDiagnosticBeforeResourceMutation)) ResourceMissingDiagnostics++;
    }

    public static void ObserveResourceModelWriteBefore(object c, int target, int index, long before, long desired)
    {
        if (desired != before) ObserveResourceDiagnostic();
        ResourceModelWriteBefore?.Invoke(c, target, index, before, desired);
    }

    public static void ObserveResourceModelWriteAfter(object c, int target, int index, long before, long desired)
    {
        if (desired != before) ResourceCommittedWrites++;
        ResourceModelWriteAfter?.Invoke(c, target, index, before, desired);
    }

    public static void ObserveDesiredResourcePreflight(bool energy)
    {
        if (!energy || !FailDesiredEnergyPreflight) return;
        FailDesiredEnergyPreflight = false; DesiredEnergyPreflightFailures++;
        throw new InvalidOperationException("fixture desired-resource observation failure before mutation");
    }

    public static void ObserveResourcePresentationStarting(object c, int target, int index) { ResourcePresentationStarting?.Invoke(c, target, index); }


    public static float NativeBossFrameDelta() { return NativeBossDeltaSeconds; }
    public static double FixtureCombatPrepClock()
    {
        CombatPrepClockRead?.Invoke();
        return ControlledCombatPrepClock ?? (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }
    public static void NativeSliderValue(object slider, float value) { } // rendering leaf only
    public static bool FixtureOffhandUnlocked() { return OffhandUnlocked; }
    public static float FixtureOffhandFactor() { return OffhandFactor; }
    public static int FixtureBeardSlots() { return BeardSlots; }
    public static int FixtureDiggerSlots() { return DiggerSlots; }

    private static void Constant(MethodDefinition method, double value)
    {
        method.Body = new Mono.Cecil.Cil.MethodBody(method);
        var il = method.Body.GetILProcessor();
        if (method.ReturnType.FullName == "System.Single") il.Emit(OpCodes.Ldc_R4, (float) value);
        else if (method.ReturnType.FullName == "System.Double") il.Emit(OpCodes.Ldc_R8, value);
        else if (method.ReturnType.FullName == "System.Int64") il.Emit(OpCodes.Ldc_I8, (long) value);
        else if (method.ReturnType.FullName != "System.Void") il.Emit(OpCodes.Ldc_I4, (int) value);
        il.Emit(OpCodes.Ret);
    }

    private static void Boundary(ModuleDefinition module, MethodDefinition method, string name, bool instance)
    {
        method.Body = new Mono.Cecil.Cil.MethodBody(method);
        var il = method.Body.GetILProcessor();
        if (instance) il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(Program).GetMethod(name, All)));
        il.Emit(OpCodes.Ret);
    }

    public static void RefreshBonuses()
    {
        Set(Controller, "bonuses", CallMain("CombatPrepGearBonuses", CallMain("CombatPrepEquipped")));
        if (FailRefreshAtSwap >= 0 && Swaps >= FailRefreshAtSwap)
        {
            FailRefreshAtSwap = -1;
            if (RefreshFailureInterference == "drag") Set(Controller, "midDrag", true);
            if (RefreshFailureInterference == "lock")
            {
                BeginFixtureClaim("Gear", "failed-swap-callback");
            }
            if (RefreshFailureInterference == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
            FailedApplicationMap = CallMain("CombatPrepEquipped");
            throw new InvalidOperationException("fixture controller refresh failure after owned swap");
        }
    }

    public static void SwapGear()
    {
        var boost = typeof(Program).GetMethod("TryNativeBoostFixtureSwap", All);
        if (boost != null && (bool)boost.Invoke(null, null)) return;
        typeof(Program).GetMethod("ObserveAdventureWeightGear", All)?.Invoke(null, null);
        if ((bool) Get(Controller, "midDrag")) throw new Exception("Attempted a gear swap while dragging.");
        var inventory = Get(Character, "inventory"); var slot = (int) Get(inventory, "item1");
        if (slot < 0) Invoke(inventory, new[] { "swapHead", "swapChest", "swapLegs", "swapBoots", "swapWeapon", "swapWeapon2" }[-slot - 1]);
        else Invoke(inventory, "swapAccWithItem", slot - 10000, Get(inventory, "item2"));
        Swaps++;
        NativeGearSwapped?.Invoke(Get(inventory, "head"));
        RefreshBonuses();
        typeof(Program).GetMethod("ApplyNativeCapLossOnSwap", All)?.Invoke(null, null);
    }

    internal static Action<object> NativeGearSwapped;

    private static object BeginFixtureClaim(string domains, string acquisition)
    {
        var domain = Enum.Parse(MainType.GetNestedType("ExecutionDomain", All), domains);
        var claim = CallMain("BeginExecutionClaim", "foreign-native-fixture", acquisition, domain, (Func<bool>)(() => true));
        var denied = CallMain("ResolveExecutionGrant", "combat-fixture-observer", acquisition, domain, (Func<bool>)(() => true), null, null);
        Equal(false, CallMain("ValidateExecutionGrant", denied, domain), "foreign admitted claim denies an unowned " + domains + " grant");
        return claim;
    }

    public static void RecallEnergy(object c)
    {
        if (FailEnergyRecall) { FailEnergyRecall = false; throw new InvalidOperationException("fixture resource recall failure"); }
        ObserveResourceDiagnostic();
        Recalls++;
        long total = (long) Get(c, "idleEnergy");
        var at = (long[]) Get(Get(c, "advancedTraining"), "energy");
        for (var i = 0; i < at.Length; i++) { total += at[i]; at[i] = 0; }
        foreach (var aug in (IEnumerable) Get(Get(c, "augments"), "augs"))
            foreach (var key in new[] { "augEnergy", "upgradeEnergy" }) { total += (long) Get(aug, key); Set(aug, key, 0L); }
        var w = Get(c, "wandoos98"); total += (long) Get(w, "wandoosEnergy"); Set(w, "wandoosEnergy", 0L);
        foreach (var lane in (IEnumerable)Get(Get(c, "NGU"), "skills"))
        { total += (long)Get(lane, "energy"); Set(lane, "energy", 0L); }
        var machine = Get(c, "machine");
        if (machine != null) { total += (long)Get(machine, "speedEnergy"); Set(machine, "speedEnergy", 0L); }
        foreach (var wish in (IEnumerable)Get(Get(c, "wishes"), "wishes"))
        { total += (long)Get(wish, "energy"); Set(wish, "energy", 0L); }
        Set(c, "idleEnergy", total);
    }

    public static void RecallMagic(object c)
    {
        Recalls++; MagicRecalls++;
        var w = Get(c, "wandoos98"); var magic = Get(c, "magic");
        var total = (long)Get(magic, "idleMagic") + (long)Get(w, "wandoosMagic"); Set(w, "wandoosMagic", 0L);
        foreach (var lane in (IEnumerable)Get(Get(c, "NGU"), "magicSkills"))
        { total += (long)Get(lane, "magic"); Set(lane, "magic", 0L); }
        var machine = Get(c, "machine");
        if (machine != null) { total += (long)Get(machine, "goldMultiMagic"); Set(machine, "goldMultiMagic", 0L); }
        var blood = Get(c, "bloodMagic");
        if (blood != null && Get(blood, "ritual") is IEnumerable rituals)
            foreach (var ritual in rituals) { total += (long)Get(ritual, "magic"); Set(ritual, "magic", 0L); }
        foreach (var wish in (IEnumerable)Get(Get(c, "wishes"), "wishes"))
        { total += (long)Get(wish, "magic"); Set(wish, "magic", 0L); }
        Set(magic, "idleMagic", total);
    }

    public static void RecallRes3(object c)
    {
        var res3 = Get(c, "res3");
        if (res3 == null) return;
        var total = (long)Get(res3, "idleRes3");
        var hacks = Get(c, "hacks");
        if (hacks != null && Get(hacks, "hacks") is IEnumerable lanes)
            foreach (var hack in lanes) { total += (long)Get(hack, "res3"); Set(hack, "res3", 0L); }
        foreach (var wish in (IEnumerable)Get(Get(c, "wishes"), "wishes"))
        { total += (long)Get(wish, "res3"); Set(wish, "res3", 0L); }
        Set(res3, "idleRes3", total);
    }

    private static void Setup()
    {
        ControlledCombatPrepClock = null; CombatPrepClockRead = null;
        FailRefreshAtSwap = -1; RefreshFailureInterference = null; FailedApplicationMap = null;
        OffhandUnlocked = false; OffhandFactor = 0;
        BeardSlots = 4; DiggerSlots = 1;
        FailEnergyRecall = false;
        FailDesiredEnergyPreflight = false; DesiredEnergyPreflightFailures = ResourceCommittedWrites = 0;
        NativeBossDeltaSeconds = .25f; RequiredDiagnosticBeforeResourceMutation = null; ResourceDiagnosticChecks = ResourceMissingDiagnostics = 0;
        MainInstance = Blank(MainType);
        // Blank bypasses Main's field initializers; runtime JSON requires these real defaults.
        Set(MainInstance, "_resourceExecutionOwner", ""); Set(MainInstance, "_resourceExecutionReason", "");
        Set(MainInstance, "_combatPrepEndedRuns", new HashSet<string>());
        Set(MainInstance, "_customIronPillPrepStartedAt", -1d);
        Set(MainType, "reference", MainInstance);
        Character = Blank(MainType.GetField("Character", All).FieldType);
        Controller = Child(Character, "inventoryController");
        Set(MainType, "Character", Character); Set(MainType, "Controller", Controller);
        Set(Controller, "character", Character);
        var settingsType = Plugin.GetType("NGUInjector.SavedSettings", true);
        Settings = Activator.CreateInstance(settingsType, All, null, new object[] { null }, null);
        Set(MainType, "Settings", Settings);
        Setting("ManageEnergy", true); Setting("ManageMagic", true); Setting("UseCustomRebirthPlan", false);
        Setting("AutoRebirth", true); Setting("CustomPlanFile", "fixture");
        Set(MainType, "_dir", Temp);
        var planDir = Path.Combine(Temp, "plans"); Directory.CreateDirectory(planDir);
        File.WriteAllText(Path.Combine(planDir, "fixture.json"), "{\"Version\":17,\"Name\":\"fixture\",\"StageSchedulerEnabled\":false,\"EnergyNguWeights\":[0,0,0,0,1,0,1,0,0],\"MagicNguWeights\":[1,0,0,0,0,0,0]}");
        var store = Blank(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStore", true)); Set(store, "_plansDir", planDir);
        Set(store, "PresetLibrary", Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.PresetLibraryStore", true), All, null, new object[] { Temp, planDir }, null));
        Set(MainType, "CustomPlanStore", store);
        var time = Child(Character, "rebirthTime"); Set(time, "totalseconds", 500d);
        Set(Child(Character, "rebirth"), "character", Character);
        Set(Child(Character, "totalPlaytime"), "totalseconds", 1500d);
        Set(Child(Character, "stats"), "rebirthNumber", 7L);
        Child(Character, "settings"); Set(Get(Character, "settings"), "wandoos98On", true); Set(Character, "bossID", 100); Set(Character, "highestBoss", 100);
        Set(Character, "attackMulti", 1000d); Set(Character, "defenseMulti", 2000d); Set(Character, "attackBoost", 1f); Set(Character, "defenseBoost", 1f);
        Set(Character, "energyPower", 100f); Set(Character, "capEnergy", 10000L); Set(Character, "curEnergy", 10000L); Set(Character, "idleEnergy", 10000L);
        var magic = Child(Character, "magic"); Set(magic, "magicPower", 50f); Set(magic, "capMagic", 20000L); Set(magic, "curMagic", 20000L); Set(magic, "idleMagic", 20000L);
        Child(Character, "challenges");
        foreach (var field in Get(Character, "challenges").GetType().GetFields(All).Where(f => !f.IsStatic && f.FieldType.Name == "Challenge")) Child(Get(Character, "challenges"), field.Name);
        var arbitrary = Child(Character, "arbitrary"); Child(arbitrary, "energyPotion1Time"); Child(arbitrary, "magicPotion1Time"); Child(arbitrary, "nukeTimer");
        Child(Character, "allArbitrary");
        Child(Child(Character, "adventureController"), "itopod"); Child(Character, "beastQuestPerkController"); Child(Character, "wishesController");
        var wishes = Child(Character, "wishes"); ListField(wishes, "wishes", 200);
        Set(Child(Character, "bossController"), "character", Character); Child(Character, "hacksController"); Child(Character, "cardsController");
        var nguController = Child(Character, "NGUController"); Set(nguController, "character", Character);
        foreach (var name in new[] { "NGU", "NGUMagic" })
        {
            var lanes = ArrayField(nguController, name, name == "NGU" ? 9 : 7);
            for (var i = 0; i < lanes.Length; i++) { Set(lanes.GetValue(i), "character", Character); Set(lanes.GetValue(i), "id", i); }
        }
        var ngu = Child(Character, "NGU"); ListField(ngu, "skills", 9); ListField(ngu, "magicSkills", 7);
        Child(Character, "allBeards"); Child(Character, "allDiggers");
        Child(Character, "yggdrasil"); Child(Character, "yggdrasilController");
        var challenges = Child(Character, "allChallenges");
        foreach (var field in challenges.GetType().GetFields(All).Where(f => !f.IsStatic && f.FieldType.Name.EndsWith("Controller"))) Child(challenges, field.Name);
        Set(Get(challenges, "noAugsChallenge"), "maxCompletions", 10); Set(Get(challenges, "laserSwordChallenge"), "maxCompletions", 10);
        var beards = Child(Character, "beards"); ListField(beards, "beards", 7); ListField(beards, "activeBeards", 0);
        var diggers = Child(Character, "diggers"); ListField(diggers, "diggers", 12); ListField(diggers, "activeDiggers", 0);
        var inventory = Child(Character, "inventory"); Child(inventory, "itemList");
        ListField(inventory, "macguffins", 0);
        Set(inventory, "macguffinBonuses", Enumerable.Repeat(1f, 30).ToList());
        var names = new string[1000]; for (var i = 0; i < names.Length; i++) names[i] = "Fixture " + i;
        Set(Child(Controller, "itemInfo"), "itemName", names);
        var id = 40;
        foreach (var slot in new[] { "head", "chest", "legs", "boots", "weapon", "weapon2" })
        {
            var item = Child(inventory, slot); Set(item, "id", slot == "weapon2" ? 0 : id++);
            SetEnum(item, "type", slot == "head" ? "Head" : slot == "chest" ? "Chest" : slot == "legs" ? "Legs" : slot == "boots" ? "Boots" : "Weapon");
            Set(item, "bossRequired", 1);
            Set(item, "removable", true);
        }
        var accs = ListField(inventory, "accs", 2);
        foreach (var item in accs) { Set(item, "id", id++); Set(item, "bossRequired", 1); Set(item, "removable", true); SetEnum(item, "type", "Accessory"); }
        ListField(inventory, "inventory", 10);
        ListField(inventory, "loadouts", 0);
        RefreshBonuses();
        var training = Child(Character, "training"); Set(training, "_attackEnergy", new long[6]); Set(training, "_defenseEnergy", new long[6]);
        Set(training, "_attackTraining", new long[] { 0, 0, 0, 0, 25000, 0 });
        Set(training, "_defenseTraining", new long[] { 0, 0, 0, 0, 25000, 0 });
        var at = Child(Character, "advancedTraining"); Set(at, "level", new long[5]); Set(at, "energy", new long[5]); Set(at, "levelTarget", new long[5]); Set(at, "barProgress", new float[5]);
        var allAt = Child(Character, "advancedTrainingController"); Set(allAt, "character", Character);
        var atNames = new[] { "defense", "attack", "block", "wandoosEnergy", "wandoosMagic" };
        for (var i = 0; i < atNames.Length; i++)
        {
            var lane = Child(allAt, atNames[i]); Set(lane, "character", Character); Set(lane, "id", i);
            Set(lane, "baseTime", i == 4 ? 4f : 2f); Set(lane, "levelFactor", i == 4 ? .2f : .1f);
        }
        var augments = Child(Character, "augments"); var augs = ArrayField(augments, "augs", 1);
        var allAugs = Child(Character, "augmentsController"); Set(allAugs, "character", Character);
        var controllers = ArrayField(allAugs, "augments", 1);
        foreach (var name in new[] { "normalAugSpeedDividers", "normalUpgradeSpeedDividers", "evilAugSpeedDividers", "evilUpgradeSpeedDividers", "sadisticAugSpeedDividers", "sadisticUpgradeSpeedDividers" })
            Set(allAugs, name, new List<float> { 1 });
        var augController = controllers.GetValue(0); Set(augController, "character", Character); Set(augController, "allAugsController", allAugs);
        Set(augController, "baseBoost", 2L); Set(augController, "baseAugmentCost", 1f); Set(augController, "baseUpgradeCost", 1f); Set(augController, "augName", "Fixture augment");
        Set(Character, "realGold", 1e12);
        var w = Child(Character, "wandoos98"); Set(w, "installed", true); Child(w, "bootupTime"); Set(Get(w, "bootupTime"), "totalseconds", 1e9);
        var wc = Child(Character, "wandoos98Controller"); Set(wc, "character", Character);
        var buttons = Child(Character, "buttons");
        foreach (var name in new[] { "wandoos", "augmentation", "advancedTraining", "ngu", "bloodMagic", "brokenTimeMachine", "yggdrasil" })
            Set(Child(buttons, name), "m_Interactable", name == "wandoos" || name == "augmentation" || name == "advancedTraining");
        typeof(Program).GetMethod("PrepareNativePresetFixture", All)?.Invoke(null, null);
        typeof(Program).GetMethod("PrepareSupportFixture", All)?.Invoke(null, null);
        typeof(Program).GetMethod("PrepareLiveResourceFixture", All)?.Invoke(null, null);
        // Main.Start normally creates this service; lifecycle retirement uses its
        // real grant-checked native route boundary even without ordinary combat ticks.
        Set(MainInstance, "_combManager", Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.CombatManager", true), true));
    }

    private static void CheckCaptureAndPreview()
    {
        var before = Resources(); var recalls = Recalls; var swaps = Swaps;
        var capture = CallMain("CaptureCombatPrep", .03, .07, null);
        var s = Get(capture, "Snapshot");
        var augController = ((Array) Get(Get(Character, "augmentsController"), "augments")).GetValue(0);
        Equal(Invoke(augController, "getUpgradeProgressPerTick", 1L), Invoke(augController, "getUpgradeProgressPerTick", 100L), "native upgrade overload demonstrably ignores sample amount");
        var aug = ((Array) Get(Get(Character, "augments"), "augs")).GetValue(0);
        foreach (var mode in new[] { "normal", "evil", "sadistic" })
        foreach (var amount in new[] { 1L, 127L, 10000L })
        {
            SetEnum(Get(Character, "settings"), "rebirthDifficulty", mode);
            Set(aug, "upgradeEnergy", amount);
            Equal(Invoke(augController, "getUpgradeProgressPerTick"), (float) (double) CallMain("CombatPrepAugProgress", augController, true, amount), "verified-IL read-only helper matches actual " + mode + " upgrade training getter at " + amount);
        }
        SetEnum(Get(Character, "settings"), "rebirthDifficulty", "normal");
        Set(aug, "upgradeEnergy", 0L);
        Equal(true, Get(s, "WandoosAvailable"), "explicit W availability");
        Equal(true, Get(s, "PhaseSpecificSupport"), "phase support included before planning");
        Near((double)Invoke(Character, "grossGoldPerSecond") - (double)Invoke(Character, "totalGPSDrain"), (double) Get(s, "NetGoldPerSecond"), "shared native net GPS");
        Near(2, (double) Get(Get(s, "AtEnergy"), "BaseTime"), "AT3 serialized coefficient");
        Near(4, (double) Get(Get(s, "AtMagic"), "BaseTime"), "AT4 distinct serialized coefficient");
        Equal(8, ((Array) Get(s, "Profiles")).Length, "four optimizer goals with two same-gear support variants");
        var boss = CallMain("CombatPrepBossStats", Get(capture, "OriginalGear"), false, null);
        Equal(Invoke(Character, "totalAttack"), Get(boss, "Item1"), "fixed-gear boss attack exactly matches actual current getter");
        Equal(Invoke(Character, "totalDefense"), Get(boss, "Item2"), "fixed-gear boss defense exactly matches actual current getter");
        Equal(before, Resources(), "capture does not allocate or equip"); Equal(recalls, Recalls, "capture never recalls resources");
        Equal(swaps, Swaps, "capture never performs even a temporary gear swap");
        var result = Invoke(Plugin.GetType("NGUInjector.Managers.CombatPrepPlanner", true), "Plan", s, 10d);
        Equal(true, Get(result, "Success"), "captured snapshot passes independent formula parity and planner validation");
        var preview = Preview(10);
        Equal(1, Json(preview, "schemaVersion").GetType().GetProperty("AsInt").GetValue(Json(preview, "schemaVersion")), "schema version");
        Equal(before, Resources(), "preview remains read only"); Equal(recalls, Recalls, "preview never recalls");
        Equal(swaps, Swaps, "preview never performs even a temporary gear swap");
        Equal(null, Get(MainInstance, "_protectedScheduler"), "preview does not initialize scheduler");
        Equal(null, Get(MainInstance, "_combatPrepRun"), "preview never starts execution");
        Equal(true, System.Text.Encoding.UTF8.GetByteCount(preview.ToString()) < (int)Get(MainType, "ExternalUiActionResultMaxBytes") - 4 * 1024, "bounded compact preview with action-envelope headroom");
        foreach (var text in new[] { "{\"durationSeconds\":0}", "{\"durationSeconds\":1.5}", "{\"durationSeconds\":86401}", "{\"backgroundEnergyPercent\":-1}", "{\"backgroundMagicPercent\":101}", "{\"durationSeconds\":null}", "{\"unknown\":3}" })
            Reject(() => { var args = new object[] { Parse(text), null }; Invoke(MainInstance, "PreviewCombatPrep", args); }, "invalid preview args " + text);
        Equal(true, Get(Settings, "AutoRebirth"), "preview leaves auto-rebirth setting enabled");
    }

    private static void CheckOptionalGearAvailability()
    {
        Setup();
        Set(Get(Get(Character, "inventory"), "head"), "id", 0);
        RefreshBonuses();
        var gear = CallMain("CombatPrepEquipped"); var support = CallMain("CombatPrepSupportKey");
        var resources = Resources(); var recalls = Recalls; var swaps = Swaps;
        var optimizer = Plugin.GetType("NGUInjector.Managers.GearOptimizer", true);
        var goalType = Plugin.GetType("NGUInjector.Managers.GearOptimizationGoal", true);
        var goals = new[] { "Augments", "AdvancedTraining", "Wandoos", "EnergyNgu" };
        foreach (var name in goals)
        {
            var result = Invoke(optimizer, "Optimize", Enum.Parse(goalType, name));
            Equal(false, Get(result, "Success"), "actual optional optimizer rejects missing head: " + name);
            Equal(true, ((string)Get(result, "Message")).StartsWith("Missing at least one required gear slot for optimization."), "optional failure is the actual required-slot result: " + name);
        }
        var preview = Preview(10);
        var cache = Get(MainInstance, "_combatPrepPreview"); var capture = Get(cache, "Capture"); var snapshot = Get(capture, "Snapshot");
        Equal(true, Get(Get(cache, "Plan"), "Success"), "normal preview plans successfully with only legal Current");
        Equal(1, ((IDictionary)Get(capture, "Gear")).Count, "no failed optional gear replaces Current");
        Equal(1, ((IDictionary)Get(capture, "Support")).Count, "Current retains its feasible physical support");
        Equal(0, ((Array)Get(snapshot, "Profiles")).Length, "no dedicated profiles are invented for unavailable optional gear");
        Equal("current", Get(Get(snapshot, "Current"), "Key"), "Current retains its original key");
        Equal("current", Get(snapshot, "ReferenceWandoosProfileKey"), "reference W profile falls back to Current");
        Equal(true, ((Array)Get(Get(cache, "Plan"), "Alternatives")).Cast<object>()
            .SelectMany(x => ((Array)Get(x, "Phases")).Cast<object>()).All(x => (string)Get(x, "GearProfileKey") == "current"), "all preview phases use Current, not substitute optional keys");
        var disclosure = "Optional gear profiles unavailable: " + string.Join(", ", goals) + ".";
        Equal(true, disclosure.Length < 256 && ((string)Get(snapshot, "CaptureAssumptions")).EndsWith(disclosure), "bounded capture assumption names every unavailable optional goal without raw errors");
        Equal(true, preview.ToString().Contains(disclosure), "normal preview serializes optional availability disclosure");
        var replay = CallMain("CaptureCombatPrep", .03, .03, capture);
        Equal(true, ((string)Get(Get(replay, "Snapshot"), "CaptureAssumptions")).EndsWith(disclosure), "fresh replay retains bounded optional availability disclosure");
        Equal(true, CallMain("CombatPrepGearMatches", gear), "Current-only preview preserves exact empty-head gear map");
        Equal(support, CallMain("CombatPrepSupportKey"), "Current-only preview preserves initial feasible beards, diggers and all levels");
        Equal(resources, Resources(), "Current-only preview leaves resources and settings unchanged");
        Equal(0, Recalls - recalls, "Current-only preview performs zero recalls");
        Equal(0, Swaps - swaps, "Current-only preview performs zero swaps");
        Equal(null, Get(MainInstance, "_combatPrepRun"), "Current-only preview never creates a run");

        Set(Character, "energyPower", float.NaN);
        Exception malformed = null;
        try { Preview(10); } catch (Exception error) { malformed = error; }
        Equal(true, malformed != null && malformed.ToString().Contains("Augment sample hit the game float clamp."), "malformed numeric getter still rejects preview despite optional gear failures");
        Equal(cache, Get(MainInstance, "_combatPrepPreview"), "malformed numeric capture never publishes a replacement preview");
        Equal(0, Recalls - recalls, "malformed numeric rejection performs zero recalls");
        Equal(0, Swaps - swaps, "malformed numeric rejection performs zero swaps");
        Equal(null, Get(MainInstance, "_combatPrepRun"), "malformed numeric rejection never creates a run");
        Setup();
    }

    private static void CheckProjectedStats()
    {
        var random = new Random(491);
        var dictionaryType = Get(Controller, "bonuses").GetType();
        var adapter = Plugin.GetType("NGUInjector.Managers.ResourceProjectionCapture", true);
        var types = (Array)Get(adapter, "SpecTypes");
        CheckRawNormalization(types);
        for (var i = 0; i < 80; i++)
        {
            var b = (IDictionary) Activator.CreateInstance(dictionaryType);
            foreach (var key in ((IDictionary) Get(Controller, "bonuses")).Keys) b.Add(key, (float) (random.NextDouble() * 4));
            Set(Controller, "bonuses", b);
            Set(Character, "energyPower", i % 4 == 0 ? float.MaxValue / 100 : (float) (random.NextDouble() * 1e8));
            Set(Get(Character, "magic"), "magicPower", i % 5 == 0 ? float.MaxValue / 100 : (float) (random.NextDouble() * 1e8));
            Set(Character, "capEnergy", i % 3 == 0 ? long.MaxValue / 10 : random.Next(1, int.MaxValue));
            Set(Get(Character, "magic"), "capMagic", i % 7 == 0 ? long.MaxValue / 10 : random.Next(1, int.MaxValue));
            Set(Get(Get(Character, "arbitrary"), "energyPotion1Time"), "totalseconds", i % 2 == 0 ? 20d : 0d);
            Set(Get(Get(Character, "arbitrary"), "magicPotion1Time"), "totalseconds", i % 3 == 0 ? 20d : 0d);
            Set(Get(Character, "arbitrary"), "energyPotion2InUse", i % 3 == 0); Set(Get(Character, "arbitrary"), "magicPotion2InUse", i % 5 == 0);
            var macguffins = (IList)Get(Get(Character, "inventory"), "macguffinBonuses");
            for (var j = 0; j < 4; j++) macguffins[j] = (float)(.5 + random.NextDouble() * 3);
            var bonuses = types.Cast<object>().Select(type => (float)b[type]).ToArray();
            foreach (var energy in new[] { true, false })
            foreach (var capacity in new[] { true, false })
            {
                var context = Invoke(adapter, "Create", Character, energy, capacity);
                var actual = (double)Invoke(context, "Project", bonuses);
                var native = Invoke(Character, capacity ? energy ? "totalCapEnergy" : "totalCapMagic" : energy ? "totalEnergyPower" : "totalMagicPower");
                Equal(Convert.ToDouble(native), actual, "shared projection exact native getter parity " + energy + "/" + capacity + "/" + i);
            }
        }
        foreach (var factor in new[] { 0f, .375f, 1f })
        {
            Setup(); OffhandUnlocked = factor > 0; OffhandFactor = factor;
            Set(Get(Get(Character, "inventory"), "weapon2"), "id", 49);
            var gear = (Array)CallMain("CombatPrepEquipped");
            var reversed = Array.CreateInstance(gear.GetType().GetElementType(), gear.Length);
            for (var i = 0; i < gear.Length; i++) reversed.SetValue(gear.GetValue(gear.Length - 1 - i), i);
            for (var spec = 0; spec < types.Length; spec++)
            {
                for (var i = 0; i < gear.Length; i++)
                {
                    var item = Get(gear.GetValue(i), "equipment");
                    SetEnum(item, "spec1Type", types.GetValue(spec).ToString()); Set(item, "spec1Cur", (i + 1) * 123.4567f);
                    SetEnum(item, "spec2Type", types.GetValue((spec + 1) % types.Length).ToString()); Set(item, "spec2Cur", (i + 1) * .0137f);
                    SetEnum(item, "spec3Type", types.GetValue((spec + 2) % types.Length).ToString()); Set(item, "spec3Cur", (i + 1) * 100003.7f);
                }
                RefreshBonuses();
                var expected = (IDictionary)Get(Controller, "bonuses");
                var projected = (float[])Invoke(adapter, "GetBonuses", gear);
                var reverseBonuses = (float[])Invoke(adapter, "GetBonuses", reversed);
                for (var i = 0; i < types.Length; i++)
                {
                    Equal(expected[types.GetValue(i)], projected[i], "shared raw-spec accumulation matches original Combat capture " + factor + "/" + spec + "/" + i);
                    Equal(Invoke(Controller, "getBonusFactor", Invoke(Controller, "specBonus", types.GetValue(i)), types.GetValue(i)), projected[i],
                        "shared resource bonuses match native mixed specs/offhand " + factor + "/" + spec + "/" + i);
                    Equal(projected[i], reverseBonuses[i], "shared resource bonuses use destination order, not input enumeration " + factor + "/" + spec + "/" + i);
                }
            }
        }
        Setup();
    }

    private static void CheckRawNormalization(Array types)
    {
        var projection = Plugin.GetType("NGUInjector.Managers.ResourceProjection", true);
        var values = new List<float> { 0f, -0f, float.Epsilon, -float.Epsilon, float.MaxValue, -float.MaxValue,
            float.PositiveInfinity, float.NegativeInfinity, float.NaN };
        foreach (var boundary in new[] { 1f, 100f, 1000f, 10000f, 100000f, 1000000f, 16777216f })
        {
            var bits = BitConverter.ToInt32(BitConverter.GetBytes(boundary), 0);
            foreach (var offset in new[] { -1, 0, 1 })
            {
                var value = BitConverter.ToSingle(BitConverter.GetBytes(bits + offset), 0);
                values.Add(value); values.Add(-value);
            }
        }
        foreach (var value in values)
        {
            var raw = Enumerable.Repeat(value, 14).ToArray();
            var bonuses = (float[])Invoke(projection, "NormalizeRaw", raw);
            Equal(false, ReferenceEquals(raw, bonuses), "native boundary normalization returns a defensive copy");
            for (var i = 0; i < types.Length; i++)
            {
                var native = (float)Invoke(Controller, "getBonusFactor", value, types.GetValue(i));
                Equal(BitConverter.ToInt32(BitConverter.GetBytes(native), 0), BitConverter.ToInt32(BitConverter.GetBytes(bonuses[i]), 0),
                    "all-spec raw normalization bit parity with native getBonusFactor " + value + "/" + types.GetValue(i));
                Equal(value, raw[i], "native boundary normalization leaves raw source unchanged");
            }
        }
    }

    private static void CheckAllocation()
    {
        var capture = CallMain("CaptureCombatPrep", .03, .07, null); var s = Get(capture, "Snapshot"); var p = Get(s, "Current");
        var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
        var phaseType = Plugin.GetType("NGUInjector.Managers.CombatPrepPhase", true);
        foreach (var weight in new[] { 0d, .25, .5, .75, 1d })
        {
            RecallEnergy(Character); RecallMagic(Character);
            Set(Get(Character, "wandoos98"), "wandoosEnergy", 17L); Set(Get(Character, "wandoos98"), "wandoosMagic", 29L);
            Set(Character, "idleEnergy", (long) Get(Character, "idleEnergy") - 17); Set(Get(Character, "magic"), "idleMagic", (long) Get(Get(Character, "magic"), "idleMagic") - 29);
            RecallEnergy(Character); RecallMagic(Character);
            var phase = Activator.CreateInstance(phaseType); Set(phase, "Kind", "AT"); Set(phase, "AtEnergyWeight", weight);
            ApplyResolvedCombatResources(phase, s, (long)Get(p, "EnergyTotal"), (long)Get(p, "MagicTotal"), true);
            var amounts = (long[]) Get(Get(Character, "advancedTraining"), "energy");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "AT zero W energy " + weight);
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "AT zero W magic " + weight);
            if (weight == 0) Equal(0L, amounts[3], "zero-weight AT3 excluded");
            if (weight == 1) Equal(0L, amounts[4], "zero-weight AT4 excluded");
            Equal(10000L, (long) Get(Character, "idleEnergy") + amounts.Sum(), "AT conserves energy " + weight);
        }
        RecallEnergy(Character); RecallMagic(Character);
        var aug = Activator.CreateInstance(phaseType); Set(aug, "Kind", "Augments"); Set(aug, "AugmentTierId", 0);
        ApplyResolvedCombatResources(aug, s, (long)Get(p, "EnergyTotal"), (long)Get(p, "MagicTotal"), true);
        var a = ((Array) Get(Get(Character, "augments"), "augs")).GetValue(0);
        Equal(Get(a, "augEnergy"), Get(a, "upgradeEnergy"), "selected augment pair shares equal caps");
        Equal(10000L, (long) Get(a, "augEnergy") + (long) Get(a, "upgradeEnergy") + (long) Get(Character, "idleEnergy"), "augment pair conservation");
        RecallEnergy(Character); RecallMagic(Character);
        var background = Activator.CreateInstance(phaseType); Set(background, "Kind", "Background");
        Set(background, "EnergyResourceFraction", .03); Set(background, "MagicResourceFraction", .07);
        ApplyResolvedCombatResources(background, s, (long)Get(p, "EnergyTotal"), (long)Get(p, "MagicTotal"), true);
        Equal(300L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "independent energy background cap");
        Equal(1400L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "independent magic background cap");
        RecallEnergy(Character); RecallMagic(Character);
    }

    private static void CheckOsBases()
    {
        var settings = Get(Character, "settings"); var w = Get(Character, "wandoos98"); var wc = Get(Character, "wandoos98Controller");
        foreach (var mode in new[] { "normal", "evil", "sadistic" })
        foreach (var os in new[] { 0, 1, 2 })
        {
            SetEnum(settings, "rebirthDifficulty", mode); Set(w, "os", Enum.ToObject(Field(w, "os").FieldType, os));
            var captured = CallMain("CaptureCombatPrepOs", os, mode);
            Equal((double) (float) Invoke(wc, "baseEnergyTime"), Get(captured, "EnergyBaseTime"), mode + " OS " + os + " exact energy base getter parity");
            Equal((double) (float) Invoke(wc, "baseMagicTime"), Get(captured, "MagicBaseTime"), mode + " OS " + os + " exact magic base getter parity");
        }
        SetEnum(settings, "rebirthDifficulty", "normal"); Set(w, "os", Enum.ToObject(Field(w, "os").FieldType, 0));
    }

    private static void CheckReservedBasicPool()
    {
        Setup(); var inv = Get(Character, "inventory"); var head = Get(inv, "head");
        SetEnum(head, "spec1Type", "EnergyCap"); Set(head, "spec1Cur", 90000f);
        var augmentHead = Blank(head.GetType()); Set(augmentHead, "id", 60); SetEnum(augmentHead, "type", "Head"); Set(augmentHead, "bossRequired", 1);
        SetEnum(augmentHead, "spec1Type", "Augs"); Set(augmentHead, "spec1Cur", 1e30f); ((IList) Get(inv, "inventory"))[0] = augmentHead;
        Set(Character, "capEnergy", 100L); Set(Character, "curEnergy", 1000L); Set(Character, "idleEnergy", 450L);
        ((long[]) Get(Get(Character, "training"), "_attackEnergy"))[0] = 550;
        RefreshBonuses();
        var capture = CallMain("CaptureCombatPrep", .03, .03, null); var snapshot = Get(capture, "Snapshot");
        Equal(false, ((Array) Get(snapshot, "Profiles")).Cast<object>().Any(x => (string) Get(x, "Goal") == "Augments"), "candidate below unrecallable basic allocation is excluded");
        Equal(false, ((IDictionary) Get(capture, "Gear")).Contains("augments-current"), "illegal candidate is removed from executable gear metadata");
        Equal(450L, Get(Get(snapshot, "Current"), "EnergyTotal"), "illegal unused candidate cannot reduce Current's usable pool");
        ((long[]) Get(Get(Character, "training"), "_attackEnergy"))[0] = 50;
        Set(Character, "idleEnergy", 950L);
        capture = CallMain("CaptureCombatPrep", .03, .03, null); snapshot = Get(capture, "Snapshot");
        var augment = ((Array) Get(snapshot, "Profiles")).Cast<object>().Single(x => (string) Get(x, "Key") == "augments-current");
        Equal(100L, Get(augment, "EnergyCap"), "legal candidate retains its actual full projected cap");
        Equal(50L, Get(augment, "EnergyTotal"), "candidate usable pool subtracts basic reservation");
        Equal(950L, Get(Get(snapshot, "Current"), "EnergyTotal"), "unused legal low cap cannot clip Current's usable pool");
        var projected = Project(capture, Phase("Wandoos", "current", 1));
        Equal(950L, Get(projected, "WandoosEnergyAllocation"), "Current-only policy keeps its full pool despite unused low-cap alternative");
        Setup();
    }

    private static void CheckRawBossBaseline()
    {
        Setup(); Set(Character, "attackMulti", 0d); Set(Character, "defenseMulti", 0d);
        ((IList) Get(Get(Character, "inventory"), "macguffinBonuses"))[13] = .01f;
        var capture = CallMain("CaptureCombatPrep", .03, .03, null); var snapshot = Get(capture, "Snapshot");
        Near(2, (double) Get(snapshot, "BaselineBossAttack"), "capture stores unclamped raw attack below the game's floor");
        Near(2, (double) Get(snapshot, "BaselineBossDefense"), "capture stores unclamped raw defense below the game's floor");
        Equal(100d, Invoke(Character, "totalAttack"), "actual game boss attack remains clamped");
        var baseline = Project(capture);
        Equal(100d, Get(baseline, "BossAttack"), "engine baseline applies boss floor after raw capture");
        var smallGain = Project(capture, Phase("Augments", "current", .02));
        Equal(true, (double) Get(smallGain, "Gain") > 1, "sub-floor boss regression actually gains augment multiplier");
        Equal(100d, Get(smallGain, "BossAttack"), "sub-floor raw attack gain cannot multiply the displayed floor");
        var largeGain = Project(capture, Phase("Augments", "current", 1));
        Near(Math.Max(100, (double) Get(snapshot, "BaselineBossAttack") * (double) Get(largeGain, "Gain")),
            (double) Get(largeGain, "BossAttack"), "large boss gain scales raw input, then clamps");
        var run = StartServerPolicy(capture, Phase("AT", "current", 1));
        Equal("100", JsonValue(Json(CallMain("GetCombatPrepRuntime"), "actual"), "bossAttack"), "runtime actual boss stats are still clamped");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
        Setup();
    }

    private static void CheckTokensAndRun()
    {
        var menuTargets = (long[]) Get(Get(Character, "advancedTraining"), "levelTarget"); menuTargets[3] = 17; menuTargets[4] = 19;
        var preview = Preview(10); var id = JsonValue(preview, "previewId");
        var candidate = FindCandidate(preview, "ATThenWandoos");
        Reject(() => CallMain("StartCombatPrep", Parse("{\"previewId\":\"wrong\",\"candidateId\":\"candidate-0\"}")), "foreign preview rejected");
        Reject(() => CallMain("StartCombatPrep", Parse("{\"previewId\":\"" + id + "\",\"candidateId\":\"" + candidate + "\",\"phases\":[]}")), "client phases rejected before execution");
        var cache = Get(MainInstance, "_combatPrepPreview"); var created = Get(cache, "Created"); Set(cache, "Created", (double) created - 121);
        Reject(() => Start(id, candidate), "expired preview rejected"); Set(cache, "Created", created);
        Setting("ManageMagic", false); var before = Recalls;
        Reject(() => Start(id, candidate), "unmanaged magic rejected before recall"); Equal(before, Recalls, "unmanaged start is inert"); Setting("ManageMagic", true);
        Set(MainInstance, "_customStageFightKey", "entry-preserved");
        Set(Character, "energyPower", 200f);
        Start(id, candidate);
        var run = Get(MainInstance, "_combatPrepRun"); Equal("running", Get(run, "Status"), "run starts");
        Reject(() => CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\",\"phase\":0}")), "cancel accepts only run ID");
        Equal(0L, menuTargets[3], "owned AT3 menu target temporarily unbounded"); Equal(0L, menuTargets[4], "owned AT4 menu target temporarily unbounded");
        Equal(200d, Get(Get(Get(Get(run, "Capture"), "Snapshot"), "Current"), "EnergyPower"), "selected server policy reprojects latest passive rate changes");
        Equal(true, Get(Settings, "AutoRebirth"), "overlay never saves auto-rebirth off");
        Equal(true, Get(MainType, "reference") == MainInstance, "same injector owner");
        Reject(() => Start(id, candidate), "active run rejects restart");
        Reject(() => CallMain("ApplyCustomFocusGoal", Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal"), "Wandoos")), "focus guard");
        Reject(() => CallMain("OverrideCustomStage", Parse("{}")), "manual stage guard precedes mutation");
        Equal(false, CallMain("ProtectedStageOwnershipAvailable", (object) null), "no scheduler applied credit during overlay");
        var w = Get(Character, "wandoos98"); Equal(0L, Get(w, "wandoosEnergy"), "AT starts without W energy"); Equal(0L, Get(w, "wandoosMagic"), "AT starts without W magic");
        var oldStart = (double) Get(run, "Started");
        Set(run, "PhaseDeadline", oldStart - 1); Set(run, "LastRefresh", -1d);
        CallMain("TickCombatPrep"); Equal(0, Get(run, "PhaseIndex"), "late actual AT gates W");
        var policy = Get(run, "Policy"); var phase = ((Array) Get(policy, "Phases")).GetValue(0);
        var levels = (long[]) Get(Get(Character, "advancedTraining"), "level"); levels[3] = (long) Get(phase, "AtEnergyTarget");
        Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
        if ((long) Get(phase, "AtMagicTarget") > 0) Equal(0, Get(run, "PhaseIndex"), "both AT targets must pass");
        levels[4] = (long) Get(phase, "AtMagicTarget"); Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
        Equal(1, Get(run, "PhaseIndex"), "W begins only after both actual targets"); Equal(oldStart, Get(run, "Started"), "setup/lateness never reset global start");
        Equal(10000L, Get(w, "wandoosEnergy"), "dedicated W phase uses the full usable energy pool below its one-tick cap");
        Equal(20000L, Get(w, "wandoosMagic"), "dedicated W phase uses the full usable magic pool below its one-tick cap");
        Equal(17L, menuTargets[3], "AT3 menu target restored after phase"); Equal(19L, menuTargets[4], "AT4 menu target restored after phase");
        var runId = (string) Get(run, "Id");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + runId + "\"}"));
        Equal("cancelled", Get(run, "Status"), "cancelled terminal state"); Equal("entry-preserved", Get(MainInstance, "_customStageFightKey"), "fight-on-entry receipt preserved");
        Equal(0L, Get(w, "wandoosEnergy"), "cancel clears W energy"); Equal(0L, Get(w, "wandoosMagic"), "cancel clears W magic");
        before = Recalls; CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + runId + "\"}")); Equal(before, Recalls, "cancel is idempotent");
        Reject(() => Start(id, candidate), "consumed preview cannot replay");
        var runtime = CallMain("GetCombatPrepRuntime");
        foreach (var key in new[] { "schemaVersion", "active", "status", "runId", "candidateLabel", "phase", "reason", "durationSeconds", "elapsedSeconds", "remainingSeconds", "phaseElapsedSeconds", "phaseRemainingSeconds", "atTargetsReached", "actual", "projected", "warnings" })
            Equal(true, Json(runtime, key) != null, "runtime contract " + key);
        Setup();
        preview = Preview(2); id = JsonValue(preview, "previewId"); candidate = FindCandidate(preview, "ATThenWandoos"); Start(id, candidate);
        run = Get(MainInstance, "_combatPrepRun"); Set(run, "Started", (double) Get(run, "Started") - 3); CallMain("TickCombatPrep");
        Equal("cancelled", Get(run, "Status"), "deadline cancels unfinished AT without W");
        Setup(); preview = Preview(2); Start(JsonValue(preview, "previewId"), FindCandidate(preview, "ATThenWandoos"));
        run = Get(MainInstance, "_combatPrepRun"); phase = ((Array) Get(Get(run, "Policy"), "Phases")).GetValue(0);
        levels = (long[]) Get(Get(Character, "advancedTraining"), "level"); levels[3] = (long) Get(phase, "AtEnergyTarget"); levels[4] = (long) Get(phase, "AtMagicTarget");
        Set(run, "Started", (double) Get(run, "Started") - 3); CallMain("TickCombatPrep");
        Equal("cancelled", Get(run, "Status"), "targets reached only at deadline cannot claim later W phase completed");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "deadline never starts late W energy");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "deadline never starts late W magic");
    }

    private static void CheckAugmentGate()
    {
        Setup();
        var a = ((Array) Get(Get(Character, "augments"), "augs")).GetValue(0); Set(a, "augmentTarget", 12L); Set(a, "upgradeTarget", 14L);
        var preview = Preview(10); Start(JsonValue(preview, "previewId"), FindCandidate(preview, "AugmentsThenWandoos"));
        var run = Get(MainInstance, "_combatPrepRun"); var phase = ((Array) Get(Get(run, "Policy"), "Phases")).GetValue(0);
        Equal(0L, Get(a, "augmentTarget"), "augment menu bound temporarily removed without altering saved plan");
        Set(run, "PhaseDeadline", (double) Get(run, "Started") - 1); Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
        Equal(0, Get(run, "PhaseIndex"), "late augments gate later W");
        Set(a, "augLevel", Get(phase, "AugmentTarget")); Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
        if ((long) Get(phase, "UpgradeTarget") > 0) Equal(0, Get(run, "PhaseIndex"), "upgrade target gates W independently");
        Set(a, "upgradeLevel", Get(phase, "UpgradeTarget")); Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
        Equal(1, Get(run, "PhaseIndex"), "W starts after both actual augment targets");
        Equal(12L, Get(a, "augmentTarget"), "augment menu bound restored"); Equal(14L, Get(a, "upgradeTarget"), "upgrade menu bound restored");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
    }

    private static void CheckFreshOsPin()
    {
        Setup(); var inv = Get(Character, "inventory"); var w = Get(Character, "wandoos98");
        Set(Get(inv, "itemList"), "jakeComplete", true); Set(w, "XLLevels", 1L);
        Set(Character, "capEnergy", 2000000000000L); Set(Character, "curEnergy", 2000000000000L); Set(Character, "idleEnergy", 2000000000000L);
        var magic = Get(Character, "magic"); Set(magic, "capMagic", 2000000000000L); Set(magic, "curMagic", 2000000000000L); Set(magic, "idleMagic", 2000000000000L);
        var preview = Preview(10); var id = JsonValue(preview, "previewId"); var candidate = FindCandidate(preview, "WandoosNoAT");
        var cache = Get(MainInstance, "_combatPrepPreview"); var alternatives = (Array) Get(Get(cache, "Plan"), "Alternatives");
        var index = int.Parse(candidate.Substring("candidate-".Length));
        Equal(1, Get(alternatives.GetValue(index), "OsId"), "large pool selects unlocked MEH without changing OS during preview");
        Equal(0, Convert.ToInt32(Get(w, "os")), "preview never changes OS");
        Set(w, "energyLevel", 1L); var before = Recalls;
        Reject(() => Start(id, candidate), "new dumps since preview prevent destructive OS switch"); Equal(before, Recalls, "failed OS switch never recalls resources");
        Set(w, "energyLevel", 0L); var bootup = Get(Get(w, "bootupTime"), "totalseconds");
        Start(id, candidate); var run = Get(MainInstance, "_combatPrepRun");
        Equal(1, Convert.ToInt32(Get(w, "os")), "fresh unlocked OS selected only at execution"); Equal(bootup, Get(Get(w, "bootupTime"), "totalseconds"), "fresh OS change preserves actual bootup");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
        Equal(1, Convert.ToInt32(Get(w, "os")), "OS is never restored on cancellation");
        Equal(false, File.Exists(Path.Combine((string)Get(MainType, "_dir"), "combat-prep-os.json")), "native OS choice creates no action-history sidecar");
        Equal(true, ReferenceEquals(Get(run, "Capture"), Get(MainInstance, "_combatPrepOsEpoch")), "same session retains only its acquired OS context");
        preview = Preview(10);
        foreach (var row in (IEnumerable) Get(Json(preview, "candidates"), "Children")) Equal("1", JsonValue(row, "os"), "same-session OS pin survives cancellation and zero dumps");
    }

    private static void CheckNguRemainders()
    {
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        foreach (var lane in (IEnumerable) Get(Get(Character, "NGU"), "skills")) Set(lane, "level", 100L);
        foreach (var lane in (IEnumerable) Get(Get(Character, "NGU"), "magicSkills")) Set(lane, "level", 100L);
        var allocator = Plugin.GetType("NGUInjector.Managers.BlackBarAllocator", true);
        Set(Get(MainType, "Settings"), "_energyNguWeights", new[] { 0, 0, 0, 0, 2, 0, 1, 0, 0 });
        Set(Get(MainType, "Settings"), "_magicNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0 });
        ApplyFallbackRequirements(900L, 700L, true);
        var skills = (IList) Get(Get(Character, "NGU"), "skills");
        Equal(600L, Get(skills[4], "energy"), "only the explicit remainder, weighted 2/3 to NGU4");
        Equal(300L, Get(skills[6], "energy"), "only the explicit remainder, weighted 1/3 to NGU6");
        Equal(9100L, Get(Character, "idleEnergy"), "NGU cannot consume parked energy outside conservative pool");
        Equal(700L, Get(((IList) Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "magic remainder respects independent budget");
        Equal(19300L, Get(Get(Character, "magic"), "idleMagic"), "NGU cannot consume parked magic");
        Setup();
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        Set(Get(MainType, "Settings"), "_energyNguWeights", new int[9]);
        Set(Get(MainType, "Settings"), "_magicNguWeights", new int[7]);
        ApplyFallbackRequirements(900L, 700L, true);
        Equal(10000L, Get(Character, "idleEnergy"), "all-zero global energy weights leave Combat Prep remainder parked");
        Equal(20000L, Get(Get(Character, "magic"), "idleMagic"), "all-zero global magic weights leave Combat Prep remainder parked");
        Setup();
    }

    private static void ApplyFallbackRequirements(long energy, long magic, bool allowTimeMachine)
    {
        var allocator = Plugin.GetType("NGUInjector.Managers.BlackBarAllocator", true);
        var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
        var desired = Invoke(writer, "EmptyDesiredMap", true, true, false);
        var plans = (IEnumerable)Invoke(allocator, "CalculateCombatPrepFallbacks", desired,
            Math.Min((long)Invoke(writer, "Reclaimable", true), Math.Max(0, energy)),
            Math.Min((long)Invoke(writer, "Reclaimable", false), Math.Max(0, magic)), allowTimeMachine);
        var intent = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceSetupIntent", true), true);
        var coordinator = MainInstance;
        Set(intent, "Owner", "fallback-check"); Set(intent, "Desired", desired);
        Set(intent, "Energy", Get(Get(MainType, "Settings"), "ManageEnergy"));
        Set(intent, "Magic", Get(Get(MainType, "Settings"), "ManageMagic"));
        Set(intent, "Authorized", (Func<bool>)(() => ReferenceEquals(Get(MainType, "reference"), coordinator)));
        var result = CallMain("ReconcileStageResourceSetup", intent);
        if (!(bool)Get(result, "Verified")) throw new InvalidOperationException("Shared fallback application failed: " + Get(result, "Reason"));
        foreach (var plan in plans) Invoke(allocator, "ObserveApplied", plan);
    }

    private static void CheckEpochAndOwnership()
    {
        Setup(); var preview = Preview(3); var id = JsonValue(preview, "previewId"); var candidate = FindCandidate(preview, "ATThenWandoos");
        var item = Get(Get(Character, "inventory"), "head"); Set(item, "level", 1f);
        Reject(() => Start(id, candidate), "material gear revision rejected"); Set(item, "level", 0f);
        Set(Get(Character, "stats"), "rebirthNumber", 8L); Reject(() => Start(id, candidate), "whole rebirth epoch mismatch rejected"); Set(Get(Character, "stats"), "rebirthNumber", 7L);
        var w = Get(Character, "wandoos98"); Set(w, "energyLevel", 2L); Set(w, "energyProgress", .25f);
        preview = Preview(3);
        foreach (var row in (IEnumerable) Get(Json(preview, "candidates"), "Children"))
            Equal("0", JsonValue(row, "os"), "existing progress pins current OS");
        id = JsonValue(preview, "previewId"); candidate = FindCandidate(preview, "ATThenWandoos"); Start(id, candidate);
        Equal(2L, Get(w, "energyLevel"), "AT start preserves W dumps"); Equal(.25f, Get(w, "energyProgress"), "AT start preserves W partial bar");
        var run = Get(MainInstance, "_combatPrepRun"); Set(Get(Character, "stats"), "rebirthNumber", 9L); var recalls = Recalls;
        CallMain("TickCombatPrep"); Equal("cancelled", Get(run, "Status"), "epoch change ends active overlay"); Equal(recalls, Recalls, "old run does not recall allocations from new epoch");
    }

    private static void CheckImplicitAtPolicy()
    {
        Setup();
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        var focus = Convert.ToInt32(Enum.Parse(Plugin.GetType("NGUInjector.CustomPlans.CustomStageFocus", true), "Wandoos"));
        Set(stage, "EnergyFocus", focus); Set(stage, "MagicFocus", focus);
        var allocator = Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true);
        foreach (var reserved in new[] { false, true })
        foreach (var energy in new[] { false, true })
        {
            var args = new object[] { stage, energy, 80, 100, false, reserved, false };
            var priorities = (string[]) Invoke(allocator, "EffectiveStagePriorities", args);
            Equal(true, args[4], "implicit W AT phase identified for either resource");
            Equal(false, priorities.Contains("WAN"), "implicit W AT excludes WAN, including Iron Pill overlay: " + energy + "/" + reserved);
        }
        Set(stage, "MagicFocus", Convert.ToInt32(Enum.Parse(Plugin.GetType("NGUInjector.CustomPlans.CustomStageFocus", true), "MagicNgu")));
        var otherLane = new object[] { stage, false, 80, 100, false, true, false };
        Equal(false, ((string[]) Invoke(allocator, "EffectiveStagePriorities", otherLane)).Contains("WAN"), "implicit energy AT also suppresses another magic focus's W fallback");
        Set(stage, "MagicFocus", focus); Set(stage, "EnergyPriorities", new[] { "AT-3", "AT-4" });
        var explicitMagic = new[] { "WAN", "NGU" }; Set(stage, "MagicPriorities", explicitMagic);
        var explicitArgs = new object[] { stage, false, 80, 100, false, false, false };
        Equal(false, ((string[]) Invoke(allocator, "EffectiveStagePriorities", explicitArgs)).Contains("WAN"), "explicit AT3/4 also prohibit magic WAN");
        Equal(true, ReferenceEquals(explicitMagic, Get(stage, "MagicPriorities")), "saved priority arrays are not mutated");
        foreach (var command in new[] { "previewCombatPrep", "startCombatPrep", "cancelCombatPrep" })
            Equal(true, ((string[]) Get(MainType, "ExternalUiCommands")).Contains(command), "advertised backend command " + command);
    }

    private static void CheckExternalEdits()
    {
        Setup(); var preview = Preview(10); Start(JsonValue(preview, "previewId"), FindCandidate(preview, "ATThenWandoos"));
        var run = Get(MainInstance, "_combatPrepRun");
        var inv = Get(Character, "inventory"); var replacement = Blank(Get(inv, "head").GetType());
        Set(replacement, "id", 40); Set(replacement, "bossRequired", 1); SetEnum(replacement, "type", "Head"); Set(inv, "head", replacement);
        Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
        Equal("cancelled", Get(run, "Status"), "external same-ID instance swap cancels run");
        Equal(true, ReferenceEquals(replacement, Get(inv, "head")), "cleanup never overwrites externally selected gear");
        Setup(); preview = Preview(10); Start(JsonValue(preview, "previewId"), FindCandidate(preview, "ATThenWandoos"));
        run = Get(MainInstance, "_combatPrepRun"); var id = (string) Get(run, "Id");
        Setting("AutoRebirth", false); Setting("CustomPlanFile", "latest-user-selection");
        var targets = (long[]) Get(Get(Character, "advancedTraining"), "levelTarget"); targets[4] = 99;
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + id + "\"}"));
        Equal(false, Get(Settings, "AutoRebirth"), "cleanup retains latest auto-rebirth edit"); Equal("latest-user-selection", Get(Settings, "CustomPlanFile"), "cleanup does not restore a stale selected plan");
        Equal(99L, targets[4], "cleanup preserves manual training target edits");
        var runtime = CallMain("GetCombatPrepRuntime"); var before = Recalls;
        Equal(true, ReferenceEquals(runtime, CallMain("GetCombatPrepRuntime")), "runtime getter is cheap cached state"); Equal(before, Recalls, "runtime publication does not allocate");
    }

    private static void CheckFutureTargetEdits()
    {
        foreach (var first in new[] { "Augments", "AT" })
        foreach (var edited in new[] { 1L, 99L, -1L })
        foreach (var editFirst in new[] { false, true })
        {
            Setup();
            var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget"); targets[3] = 8; targets[4] = 9;
            var aug = ((Array)Get(Get(Character, "augments"), "augs")).GetValue(0);
            Set(aug, "augmentTarget", 11L); Set(aug, "upgradeTarget", 12L);
            var capture = CallMain("CaptureCombatPrep", .03, .03, null);
            var second = first == "AT" ? "Augments" : "AT";
            var run = StartServerPolicy(capture, Phase(first, "current", 10), Phase(second, "current", 10));
            Equal(0, Get(run, "PhaseIndex"), "future-target fixture starts its actual earlier phase");
            if (second == "AT") targets[editFirst ? 3 : 4] = edited; else Set(aug, editFirst ? "augmentTarget" : "upgradeTarget", edited);
            // No refresh/deadline forcing: even edits during the earlier phase's held allocation cancel.
            CallMain("TickCombatPrep");
            Equal("cancelled", Get(run, "Status"), "future " + second + " target edit cancels during " + first + ": " + edited);
            Equal(0, Get(run, "PhaseIndex"), "edited future phase is never acquired");
            Equal(edited, second == "AT" ? targets[editFirst ? 3 : 4] : Get(aug, editFirst ? "augmentTarget" : "upgradeTarget"), "edited future field is never overwritten");
            Equal(second == "AT" ? editFirst ? 9L : 8L : editFirst ? 12L : 11L,
                second == "AT" ? targets[editFirst ? 4 : 3] : Get(aug, editFirst ? "upgradeTarget" : "augmentTarget"), "unowned companion future field is unchanged");
            Equal(first == "AT" ? 8L : 11L, first == "AT" ? targets[3] : Get(aug, "augmentTarget"), "only earlier owned targets are restored");
            Equal(0, ((ICollection)Get(run, "RestoreTargets")).Count, "owned target restoration actions are released");
        }
        // Unchanged nonzero future targets remain legal and active-owned zero menus do not cancel.
        foreach (var first in new[] { "Augments", "AT" })
        {
            Setup(); var targets = (long[])Get(Get(Character, "advancedTraining"), "levelTarget"); targets[3] = 8; targets[4] = 9;
            var aug = ((Array)Get(Get(Character, "augments"), "augs")).GetValue(0); Set(aug, "augmentTarget", 11L); Set(aug, "upgradeTarget", 12L);
            var capture = CallMain("CaptureCombatPrep", .03, .03, null);
            var run = StartServerPolicy(capture, Phase(first, "current", 10), Phase(first == "AT" ? "Augments" : "AT", "current", 10));
            if (first == "Augments") CompleteAugmentPhase(run);
            else
            {
                var phase = ((Array)Get(Get(run, "Policy"), "Phases")).GetValue(0);
                var levels = (long[])Get(Get(Character, "advancedTraining"), "level");
                levels[3] = (long)Get(phase, "AtEnergyTarget"); levels[4] = (long)Get(phase, "AtMagicTarget");
                Set(run, "PhaseDeadline", (double)Get(run, "Started") - 1); Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
            }
            Equal("running", Get(run, "Status"), "unchanged future target successfully acquires phase after " + first);
            Equal(1, Get(run, "PhaseIndex"), "both orders enter second phase with original expected targets");
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            Equal(8L, targets[3], "AT original bound restored after both phases");
            Equal(12L, Get(aug, "upgradeTarget"), "augment original bound restored after both phases");
        }
    }

    private static object AlternateAtRun()
    {
        Setup(); var inventory = Get(Character, "inventory"); var head = Blank(Get(inventory, "head").GetType());
        Set(head, "id", 60); SetEnum(head, "type", "Head"); Set(head, "bossRequired", 1); Set(head, "removable", true);
        SetEnum(head, "spec1Type", "AdvTraining"); Set(head, "spec1Cur", 1e30f); ((IList) Get(inventory, "inventory"))[0] = head;
        var capture = CallMain("CaptureCombatPrep", .03, .03, null);
        var run = StartServerPolicy(capture, Phase("AT", "at-current", 10));
        Equal(true, ReferenceEquals(head, Get(inventory, "head")), "fixture actually equips a different owned AT instance");
        return run;
    }

    private static void CheckTerminationOwnership()
    {
        foreach (var end in new[] { "cancel", "deadline", "unload" })
        foreach (var interference in new[] { "gear", "drag" })
        {
            var run = AlternateAtRun(); var inv = Get(Character, "inventory"); var retained = Get(inv, "head");
            if (interference == "gear")
            {
                var replacement = Blank(retained.GetType()); Set(replacement, "id", 60); SetEnum(replacement, "type", "Head"); Set(replacement, "bossRequired", 1);
                ((IList) Get(inv, "inventory"))[1] = retained; Set(inv, "head", replacement); retained = replacement;
            }
            else Set(Controller, "midDrag", true);
            var swaps = Swaps;
            if (end == "cancel") CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            else if (end == "deadline") { Set(run, "Started", (double) Get(run, "Started") - 11); CallMain("TickCombatPrep"); }
            else { Setting("ManualAdventureEnabled", true); Set(MainInstance, "_unloaded", true); CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true); }
            Equal(swaps, Swaps, end + " performs no restoration swaps after " + interference + " interference");
            Equal(true, ReferenceEquals(retained, Get(inv, "head")), end + " retains external gear/drag slot map");
            Equal(false, Get(MainInstance, "CombatPrepActive"), end + " still ends the overlay safely");
        }
        var owned = AlternateAtRun(); var original = ((Array) Get(Get(owned, "Capture"), "OriginalGear")).Cast<object>().Single(x => (int) Get(x, "slot") == -1);
        var path = Path.Combine(Temp, "plans", "fixture.json"); File.WriteAllText(path, File.ReadAllText(path).Replace("\"StageSchedulerEnabled\":false", "\"StageSchedulerEnabled\":true"));
        Setting("UseCustomRebirthPlan", true); Setting("ManualAdventureEnabled", true); Set(MainInstance, "_unloaded", true);
        var before = Swaps; CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true);
        Equal(before, Swaps, "unload never restores historical gear while another plan is configured");
        Equal(false, ReferenceEquals(Get(original, "equipment"), Get(Get(Character, "inventory"), "head")), "no explicit return baseline preserves actual phase setup on unload");
        Equal(true, Get(Settings, "UseCustomRebirthPlan"), "termination restoration does not overwrite selected plan settings");
    }

    private static void CheckManualAdventureRestoration()
    {
        foreach (var scheduledGear in new[] { false, true })
        foreach (var end in new[] { "cancel", "deadline", "complete" })
        {
            var run = AlternateAtRun(); var original = Get(Get(run, "Capture"), "OriginalGear");
            Setting("ManualAdventureEnabled", true);
            if (scheduledGear)
            {
                var path = Path.Combine(Temp, "plans", "fixture.json");
                File.WriteAllText(path, "{\"Version\":17,\"Name\":\"fixture\",\"StageSchedulerEnabled\":true,\"EnergyNguWeights\":[0,0,0,0,1,0,1,0,0],\"MagicNguWeights\":[1,0,0,0,0,0,0],\"Stages\":[{\"Name\":\"gear successor\",\"DurationSeconds\":7200,\"GearGoal\":0}]}");
                Setting("UseCustomRebirthPlan", true);
                var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
                Equal(true, ((Array)Get(plan, "Stages")).Cast<object>().Any(x => (int)Get(x, "GearGoal") >= 0), "manual-adventure regression has real configured stage gear");
            }
            var phaseSetup = CallMain("CombatPrepEquipped"); var swaps = Swaps;
            if (end == "cancel") CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            else if (end == "deadline") { Set(run, "Started", (double)Get(run, "Started") - 11); CallMain("TickCombatPrep"); }
            else CallMain("EndCombatPrep", "completed", "Completed fixture phase.", true);
            Equal(true, CallMain("CombatPrepGearMatches", phaseSetup), "Manual Adventure " + end + " retains actual setup until current successor applies, scheduled gear=" + scheduledGear);
            Equal(swaps, Swaps, "handoff has no historical gear detour");
            Equal(true, Get(Settings, "ManualAdventureEnabled"), "restoration does not disable manual adventure");
        }
    }

    private static void CheckFailedGearAcquisition()
    {
        foreach (var interference in new[] { "none", "drag", "lock", "epoch" })
        {
            Setup(); var inventory = Get(Character, "inventory");
            var originalHead = Get(inventory, "head"); var originalChest = Get(inventory, "chest");
            var items = (IList) Get(inventory, "inventory");
            for (var i = 0; i < 2; i++)
            {
                var item = Blank(originalHead.GetType()); Set(item, "id", 60 + i); SetEnum(item, "type", i == 0 ? "Head" : "Chest");
                Set(item, "bossRequired", 1); Set(item, "removable", true); SetEnum(item, "spec1Type", "AdvTraining"); Set(item, "spec1Cur", 1e30f);
                items[i] = item;
            }
            var desiredChest = items[1];
            var capture = CallMain("CaptureCombatPrep", .03, .03, null);
            Setting("UseCustomRebirthPlan", true); Setting("ManualAdventureEnabled", true);
            var before = Swaps; FailRefreshAtSwap = before + 1; RefreshFailureInterference = interference;
            Exception failure = null;
            try { StartServerPolicy(capture, Phase("AT", "at-current", 10)); } catch (Exception error) { failure = error; }
            Equal(interference == "none", failure == null, "only an owned known-partial phase remains resumable: " + interference + "; " + failure);
            Equal(true, FailedApplicationMap != null, "failure is injected after a real data swap: " + interference);
            var failedHead = ((Array) FailedApplicationMap).Cast<object>().Single(x => (int) Get(x, "slot") == -1);
            var failedChest = ((Array) FailedApplicationMap).Cast<object>().Single(x => (int) Get(x, "slot") == -2);
            Equal(true, ReferenceEquals(desiredChest, items[1]), "later desired chest remains in inventory at the first-swap failure");
            Equal(60, Get(failedHead, "id"), "one owned head swap happened before refresh failed");
            Equal(true, ReferenceEquals(originalChest, Get(failedChest, "equipment")), "failure occurred before the second desired swap");
            var run = Get(MainInstance, "_combatPrepRun");
            Equal(false, Get(run, "SetupVerified"), "partial or foreign acquisition cannot verify prerequisites: " + interference);
            Equal(-1, Get(run, "AppliedResourcePhase"), "partial or foreign acquisition never receives phase application credit: " + interference);
            Equal(true, ((string)Get(run, "Reason")).Contains("fixture controller refresh failure after owned swap"), "native failure is surfaced in current reconciliation status: " + interference);
            if (interference == "none")
            {
                Equal(before + 1, Swaps, "known partial makes no historical rollback swap");
                Equal(60, Get(Get(inventory, "head"), "id"), "committed head stays in place for shared continuation");
                Equal(true, ReferenceEquals(originalChest, Get(inventory, "chest")), "untouched original chest remains intact");
                Equal(true, CallMain("CombatPrepGearMatches", Get(run, "ExpectedGear")), "only shared typed progress advances the owned observation");
                var started = Get(run, "Started"); var deadline = Get(run, "PhaseDeadline");
                Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
                Equal(true, Get(run, "SetupVerified"), "same selected setup completes from known partial progress");
                Equal(before + 2, Swaps, "continuation applies only the unfinished chest without head detour");
                Equal(started, Get(run, "Started"), "partial continuation retains original start");
                Equal(deadline, Get(run, "PhaseDeadline"), "partial continuation retains original phase deadline");
            }
            else
            {
                Equal(before + 1, Swaps, "unsafe failure never attempts rollback swaps: " + interference);
                Equal(true, Get(run, "BlockedReason") != null, "unsafe acquisition cannot silently reacquire ownership: " + interference);
                Equal(true, CallMain("CombatPrepGearMatches", FailedApplicationMap), "interfered map is retained: " + interference);
                Equal(false, Get(MainInstance, "CombatPrepRunActive"), "foreign acquisition cancels its bounded preparation: " + interference);
                Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
                Equal(before + 1, Swaps, "later tick cannot retry the cancelled foreign acquisition: " + interference);
                Equal(-1, Get(run, "AppliedResourcePhase"), "cancelled foreign acquisition never funds a phase: " + interference);
            }
        }
    }

    private static void CheckCompletePhaseMaps()
    {
        Setup(); var inventory = Get(Character, "inventory"); var accessories = (IList) Get(inventory, "accs"); var item = accessories[1];
        ((IList) Get(inventory, "inventory"))[0] = item; accessories[1] = Blank(item.GetType()); RefreshBonuses();
        var capture = CallMain("CaptureCombatPrep", .03, .03, null);
        var run = StartServerPolicy(capture, Phase("Augments", "augments-current", 1), Phase("Wandoos", "current", 1));
        Equal(true, ReferenceEquals(item, accessories[1]), "first phase fills an originally empty accessory slot");
        CompleteAugmentPhase(run);
        Equal(1, Get(run, "PhaseIndex"), "second phase really enters Current gear");
        Equal(0, Get(accessories[1], "id"), "Current phase actively restores the selected empty slot");
        var wanted = ((IDictionary) Get(Get(run, "Capture"), "Gear"))["current"];
        Equal(true, CallMain("CombatPrepGearMatches", wanted), "complete selected slot map including zero entries matches actual gear");
        Equal(true, ReferenceEquals(wanted, Get(Get(run, "Setup"), "Gear")), "acquired desired map remains separate from actual application observations");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
    }

    private static void CheckSparseAccessoryCapture()
    {
        foreach (var occupied in new[] { 0, 1 })
        {
            Setup(); var inv = Get(Character, "inventory"); var accs = (IList)Get(inv, "accs");
            var sole = accs[1];
            accs[occupied] = sole; accs[1 - occupied] = Blank(sole.GetType()); RefreshBonuses();
            var before = Swaps;
            var optimizer = Plugin.GetType("NGUInjector.Managers.GearOptimizer", true);
            var goal = Enum.Parse(Plugin.GetType("NGUInjector.Managers.GearOptimizationGoal", true), "Augments");
            var score = Enum.Parse(Plugin.GetType("NGUInjector.Managers.FightScoreMode", true), "SqrtPowerToughness");
            var optimized = Invoke(optimizer, "Optimize", goal, score);
            Equal(true, Get(optimized, "Success"), "real optimizer accepts sole accessory in slot " + occupied);
            Equal(1, ((IEnumerable)Get(optimized, "Items")).Cast<object>().Count(x => ReferenceEquals(sole, Get(x, "equipment"))), "real optimizer returns sole accessory once without fake padding");
            var capture = CallMain("CaptureCombatPrep", .03, .03, null);
            foreach (DictionaryEntry pair in (IDictionary)Get(capture, "Gear"))
            {
                var map = ((Array)pair.Value).Cast<object>().ToArray();
                Equal(1, map.Count(x => ReferenceEquals(sole, Get(x, "equipment"))), "complete capture retains unique accessory instance for " + pair.Key);
                Equal(1, map.Count(x => (int)Get(x, "slot") >= 10000 && (int)Get(x, "id") == 0), "complete capture has explicit empty accessory for " + pair.Key);
            }
            Equal(before, Swaps, "sparse accessory capture remains read-only");
            var original = Get(capture, "OriginalGear");
            var run = StartServerPolicy(capture, Phase("Augments", "augments-current", 10));
            Equal(true, ReferenceEquals(sole, accs[0]), "actual phase application moves sole accessory to selected destination");
            Equal(0, Get(accs[1], "id"), "actual phase application leaves moved source empty");
            var applied = CallMain("CombatPrepEquipped"); var swaps = Swaps;
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            Equal(true, CallMain("CombatPrepGearMatches", applied), "sparse exact phase placement retained without explicit return baseline");
            Equal(swaps, Swaps, "sparse release performs no historical swap");
        }
    }

    private static void CheckUsedProfileDepletion()
    {
        Setup(); var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
        SetEnum(head, "spec1Type", "EnergyCap"); Set(head, "spec1Cur", 90000f);
        var lower = Blank(head.GetType()); Set(lower, "id", 60); SetEnum(lower, "type", "Head"); Set(lower, "bossRequired", 1); Set(lower, "removable", true);
        SetEnum(lower, "spec1Type", "Augs"); Set(lower, "spec1Cur", 1e30f); ((IList) Get(inventory, "inventory"))[0] = lower;
        Set(Character, "capEnergy", 100L); Set(Character, "curEnergy", 1000L); Set(Character, "idleEnergy", 950L);
        ((long[]) Get(Get(Character, "training"), "_attackEnergy"))[0] = 50; RefreshBonuses();
        var capture = CallMain("CaptureCombatPrep", .03, .03, null);
        var phases = new[] { Phase("Augments", "augments-current", 1), Phase("Wandoos", "current", 1) };
        var projected = Project(capture, phases);
        Equal(50L, Get(projected, "WandoosEnergyAllocation"), "engine carries depletion only through actually used lower-cap gear");
        typeof(Program).GetField("ModelNativeCapLossOnSwap", All)?.SetValue(null, true);
        var run = StartServerPolicy(capture, phases);
        Equal(50L, Get(run, "EnergyPool"), "execution enters lower-cap phase with its own usable pool");
        CompleteAugmentPhase(run);
        Equal(50L, Get(run, "EnergyPool"), "returning to Current cannot assume cap refill");
        Equal(50L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "execution W allocation matches projected carried pool");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
    }

    private static void CheckNormalAtEntry()
    {
        var allocator = Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true);
        foreach (var implicitAt in new[] { false, true })
        foreach (var managed in new[] { false, true })
        foreach (var emptyMagic in new[] { false, true })
        {
            Setup(); Setting("ManageMagic", managed);
            var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
            Set(stage, "EnergyFocus", implicitAt ? 4 : 0); Set(stage, "MagicFocus", 0);
            // Enum values are resolved explicitly; implicit W stages enter their first-half AT policy.
            if (implicitAt) Set(stage, "EnergyFocus", Convert.ToInt32(Enum.Parse(Plugin.GetType("NGUInjector.CustomPlans.CustomStageFocus", true), "Wandoos")));
            Set(stage, "EnergyPriorities", implicitAt ? new string[0] : new[] { "AT-3", "AT-4" });
            Set(stage, "MagicPriorities", emptyMagic ? new string[0] : new[] { "WAN" });
            var w = Get(Character, "wandoos98"); Set(w, "wandoosMagic", 123L); Set(Get(Character, "magic"), "idleMagic", 19877L);
            var oldMagicRecalls = MagicRecalls; var oldIdle = Get(Get(Character, "magic"), "idleMagic");
            var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
            var result = (string) Invoke(allocator, "ApplyStage", plan, stage, 80, 0, "at-entry", 100);
            var at = (long[]) Get(Get(Character, "advancedTraining"), "energy");
            if (managed)
            {
                Equal(0L, Get(w, "wandoosMagic"), "managed old WAN recalled even with empty magic policy");
                Equal(true, at[3] + at[4] > 0, "AT receives energy after magic W is removed");
            }
            else
            {
                Equal(true, result.Contains("unmanaged magic"), "unmanaged WAN produces an explicit AT entry blocker");
                Equal(123L, Get(w, "wandoosMagic"), "unmanaged W allocation is never recalled");
                Equal(oldIdle, Get(Get(Character, "magic"), "idleMagic"), "unmanaged magic counter is not changed");
                Equal(oldMagicRecalls, MagicRecalls, "no unmanaged magic recall controller call");
                Equal(0L, at.Sum(), "blocked AT never receives energy");
                Equal(false, Get(allocator, "LastStageApplicationSucceeded"), "blocked AT cannot receive applied-stage credit");
            }
            var priorities = (string[]) Invoke(allocator, "EffectiveStagePriorities", new object[] { stage, false, 80, 100, false, true, false });
            Equal(false, priorities.Contains("WAN"), "Iron Pill cannot reinsert WAN into explicit or implicit AT");
        }
        Setup(); var nonAt = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(nonAt, "EnergyPriorities", new[] { "NGU" }); Set(nonAt, "MagicPriorities", new[] { "WAN", "NGU" });
        var preserved = (string[]) Invoke(allocator, "EffectiveStagePriorities", new object[] { nonAt, false, 80, 100, false, false, false });
        Equal(true, preserved.SequenceEqual(new[] { "WAN", "NGU" }), "explicit non-AT user policy retains its meaning");

        Setup(); Set(nonAt, "MagicPriorities", new string[0]); Set(nonAt, "MagicFocus", 0);
        Set(Get(Character, "wandoos98"), "wandoosMagic", 123L); Set(Get(Character, "magic"), "idleMagic", 19877L);
        var nonAtPlan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
        Invoke(allocator, "ApplyStage", nonAtPlan, nonAt, 80, 0, "non-at", 100);
        Equal(123L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "empty non-AT magic policy preserves existing magic allocation");

        Setup(); Setting("ManageMagic", false);
        var stageAt = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stageAt, "EnergyPriorities", new[] { "AT-3", "AT-4" }); Set(stageAt, "MagicPriorities", new string[0]);
        Set(stageAt, "DurationSeconds", 100); Set(stageAt, "CompletionMode", 0); Set(stageAt, "GearGoal", 11);
        var stagedPlan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture"); Set(stagedPlan, "StageSchedulerEnabled", true);
        Set(stagedPlan, "StageLoopStartIndex", -1); Set(stagedPlan, "StageDurationSeconds", 1000); Set(stagedPlan, "TimeTriggerSeconds", 1000);
        var stages = Array.CreateInstance(stageAt.GetType(), 1); stages.SetValue(stageAt, 0); Set(stagedPlan, "Stages", stages); Setting("UseCustomRebirthPlan", true);
        Set(Get(Character, "wandoos98"), "wandoosMagic", 123L); Set(Get(Character, "magic"), "idleMagic", 19877L);
        var swaps = Swaps; var recalls = MagicRecalls;
        CallMain("RunCustomPlanAutomation", stagedPlan);
        Equal(swaps, Swaps, "normal stage entry blocks unmanaged WAN before gear setup");
        Equal(recalls, MagicRecalls, "normal stage gear preparation cannot recall unmanaged magic");
        Equal(123L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "normal stage entry preserves external unmanaged WAN");
        Equal(false, CallMain("ProtectedStageOwnershipAvailable", stagedPlan), "blocked normal stage cannot arm scheduler ownership");
        var oldAt = (long[]) Get(Get(Character, "advancedTraining"), "energy"); oldAt[3] = 20; oldAt[4] = 30; Set(Character, "idleEnergy", 9950L);
        Invoke(allocator, "ApplyStage", stagedPlan, stageAt, 80, 0, "blocked-old-at", 100);
        Equal(0L, oldAt.Sum(), "existing managed AT is released when external unmanaged WAN blocks its continuation");
        Equal(10000L, Get(Character, "idleEnergy"), "blocked old AT returns its energy without losing resources");
        Equal(123L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "releasing old managed AT never changes unmanaged magic WAN");
    }

    private static void CheckHeldRefresh()
    {
        Setup(); var capture = CallMain("CaptureCombatPrep", .03, .03, null);
        var run = StartServerPolicy(capture, Phase("AT", "current", 60));
        var at = (long[]) Get(Get(Character, "advancedTraining"), "energy"); var first = at[3]; var second = at[4];
        var levels = (long[]) Get(Get(Character, "advancedTraining"), "level"); levels[3] = 1000; levels[4] = 1000;
        Set(run, "LastRefresh", Get(MainType, "CombatPrepClock")); var before = Recalls;
        CallMain("TickCombatPrep");
        Equal(first, at[3], "AT3 allocation stays held when actual levels change inside the refresh interval");
        Equal(second, at[4], "AT4 allocation stays held inside the refresh interval"); Equal(before, Recalls, "no intra-interval recall/reallocation");
        Set(run, "LastRefresh", (double) Get(run, "LastRefresh") - .51); CallMain("TickCombatPrep");
        Equal(true, at[3] > first && at[4] > second, "next half-second refresh uses fresh actual rates/levels");
        Equal(.5d, Get(Get(Get(run, "Capture"), "Snapshot"), "AllocationRefreshSeconds"), "snapshot and executor share the engine's half-second cadence");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
    }

    private static void CheckPrimaryAtPolicy()
    {
        var allocator = Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true);
        foreach (var energyPriorities in new[] { new[] { "AT" }, new[] { "AT-0", "AT-1" }, new[] { "AT-1", "AT-0" }, new[] { "AT-2" } })
        foreach (var managed in new[] { true, false })
        {
            Setup(); Setting("ManageMagic", managed);
            var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
            Set(stage, "EnergyPriorities", energyPriorities); Set(stage, "MagicPriorities", new[] { "WAN" });
            Set(stage, "MagicFocus", Convert.ToInt32(Enum.Parse(Plugin.GetType("NGUInjector.CustomPlans.CustomStageFocus", true), "Wandoos")));
            foreach (var reserve in new[] { false, true })
            {
                var args = new object[] { stage, false, 0, 0, false, reserve, false };
                Equal(false, ((string[]) Invoke(allocator, "EffectiveStagePriorities", args)).Contains("WAN"), "primary " + energyPriorities[0] + " suppresses magic WAN including Iron Pill");
                Equal(true, args[4], "primary generic/stat AT uses the shared AT predicate");
            }
            var w = Get(Character, "wandoos98"); Set(w, "wandoosMagic", 123L); Set(Get(Character, "magic"), "idleMagic", 19877L);
            var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture"); var before = MagicRecalls;
            var message = (string) Invoke(allocator, "ApplyStage", plan, stage, 0, 0, "primary-at", 0);
            var allocated = ((long[]) Get(Get(Character, "advancedTraining"), "energy")).Sum();
            if (managed)
            {
                Equal(0L, Get(w, "wandoosMagic"), "primary stat/generic AT recalls managed magic W before allocating");
                Equal(true, allocated > 0, "primary stat/generic AT actually gets energy");
            }
            else
            {
                Equal(true, message.Contains("unmanaged magic"), "primary stat/generic AT is blocked by unmanaged W");
                Equal(123L, Get(w, "wandoosMagic"), "primary stat/generic AT preserves unmanaged magic W");
                Equal(before, MagicRecalls, "primary stat/generic AT never recalls unmanaged magic");
                Equal(0L, allocated, "blocked primary stat/generic AT cannot train");
            }
        }
        Setup(); var fallback = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        var priorities = new[] { "NGU", "WAN", "TM", "AT" };
        Set(fallback, "EnergyPriorities", priorities); Set(fallback, "MagicPriorities", new[] { "WAN", "NGU" });
        Set(fallback, "EnergyFocus", Convert.ToInt32(Enum.Parse(Plugin.GetType("NGUInjector.CustomPlans.CustomStageFocus", true), "AdvancedTraining")));
        Set(fallback, "MagicFocus", Convert.ToInt32(Enum.Parse(Plugin.GetType("NGUInjector.CustomPlans.CustomStageFocus", true), "Wandoos")));
        var energyArgs = new object[] { fallback, true, 80, 100, false, false, false };
        Equal(true, ((string[]) Invoke(allocator, "EffectiveStagePriorities", energyArgs)).SequenceEqual(priorities), "NGU/WAN/TM with later generic AT fallback retains the exact energy policy");
        Equal(false, energyArgs[4], "a later generic AT fallback is not primary AT even with an AT focus label");
        var magicArgs = new object[] { fallback, false, 80, 100, false, true, false };
        Equal(true, ((string[]) Invoke(allocator, "EffectiveStagePriorities", magicArgs)).Contains("WAN"), "NGU's later AT fallback preserves magic WAN including Iron Pill");
        var fallbackPlan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
        Invoke(allocator, "ApplyStage", fallbackPlan, fallback, 80, 0, "ngu-at-fallback", 100);
        Equal(true, (long) Get(Get(Character, "wandoos98"), "wandoosMagic") > 0, "non-primary AT fallback still actually allocates magic W");
        Equal(0L, ((long[]) Get(Get(Character, "advancedTraining"), "energy")).Sum(), "higher-priority W consumes energy before the generic AT fallback");
    }

    private static void CheckRuntimeObservations()
    {
        Setup(); var at = Get(Character, "advancedTraining"); var w = Get(Character, "wandoos98");
        var levels = (long[]) Get(at, "level"); var allocations = (long[]) Get(at, "energy");
        levels[3] = 9007199254740993L; levels[4] = long.MaxValue - 5;
        allocations[3] = 9007199254740995L; allocations[4] = 9007199254740997L;
        Set(w, "energyLevel", 9007199254740999L); Set(w, "magicLevel", 9007199254741001L);
        Set(w, "wandoosEnergy", 12345678901234567L); Set(w, "wandoosMagic", 0L); Set(Character, "realGold", 12345.5d);
        var recalls = Recalls; var swaps = Swaps; var runtime = CallMain("GetCombatPrepRuntime"); var actual = Json(runtime, "actual");
        Equal(true, Get(Json(actual, "available"), "AsBool"), "idle game observations are available without a prep run");
        var expected = new Dictionary<string, long> {
            { "energyAtLevel", levels[3] }, { "magicAtLevel", levels[4] }, { "energyAtAllocation", allocations[3] }, { "magicAtAllocation", allocations[4] },
            { "wandoosEnergyLevel", 9007199254740999L }, { "wandoosMagicLevel", 9007199254741001L },
            { "wandoosEnergyAllocation", 12345678901234567L }, { "wandoosMagicAllocation", 0L }
        };
        foreach (var pair in expected)
        {
            Equal(true, Get(Json(actual, pair.Key), "IsString"), pair.Key + " is an exact decimal string");
            Equal(pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), JsonValue(actual, pair.Key), pair.Key + " preserves values beyond JS safe integers");
        }
        Equal(12345.5d, Get(Json(actual, "gold"), "AsDouble"), "actual gold is current");
        Equal(50d, Get(Json(actual, "energyWandoosLevelsPerSecond"), "AsDouble"), "live W rate caps at 50 levels/s");
        Equal(0d, Get(Json(actual, "magicWandoosLevelsPerSecond"), "AsDouble"), "zero reported magic allocation always reports zero rate");
        Equal(-1, Get(Json(actual, "augmentIndex"), "AsInt"), "idle has no selected prep augment pair");
        levels[3] = 17; Set(MainInstance, "_combatPrepLastActualRefresh", Get(MainType, "CombatPrepClock"));
        Equal(expected["energyAtLevel"].ToString(), JsonValue(Json(CallMain("GetCombatPrepRuntime"), "actual"), "energyAtLevel"), "idle reads reuse the observation cache within a half second");
        Set(MainInstance, "_combatPrepLastActualRefresh", -1d);
        Equal("17", JsonValue(Json(CallMain("GetCombatPrepRuntime"), "actual"), "energyAtLevel"), "idle cache refresh observes current game levels");
        Equal(recalls, Recalls, "observations never recall resources"); Equal(swaps, Swaps, "observations never swap gear");
        foreach (var pair in new[] { Tuple.Create(0L, double.PositiveInfinity, 0d), Tuple.Create(1L, double.NaN, 0d),
            Tuple.Create(1L, 0d, 0d), Tuple.Create(1L, .25, 12.5), Tuple.Create(1L, .4, 50d / 3), Tuple.Create(1L, 2d, 50d), Tuple.Create(1L, double.PositiveInfinity, 50d) })
            Equal(pair.Item3, CallMain("CombatPrepActualDumpRate", pair.Item1, pair.Item2), "finite ceil-to-tick W rate for allocation/q " + pair.Item1 + "/" + pair.Item2);

        Setup(); w = Get(Character, "wandoos98"); Set(w, "wandoosEnergy", 1000000000L); Set(w, "wandoosMagic", 100000000L);
        actual = Json(CallMain("GetCombatPrepRuntime"), "actual"); var wc = Get(Character, "wandoos98Controller");
        var eq = (double) (float) Invoke(wc, "energyProgressToAdd", 1000000000L); var mq = (double) (float) Invoke(wc, "magicProgressToAdd", 100000000L);
        Equal(50 / Math.Max(1, Math.Ceiling(1 / eq)), Get(Json(actual, "energyWandoosLevelsPerSecond"), "AsDouble"), "energy live rate uses the real rounded amount getter q");
        Equal(50 / Math.Max(1, Math.Ceiling(1 / mq)), Get(Json(actual, "magicWandoosLevelsPerSecond"), "AsDouble"), "magic live rate uses its own actual getter q");
        Set(w, "disabled", true); Set(MainInstance, "_combatPrepLastActualRefresh", -1d);
        actual = Json(CallMain("GetCombatPrepRuntime"), "actual");
        Equal(0d, Get(Json(actual, "energyWandoosLevelsPerSecond"), "AsDouble"), "disabled W reports zero actual training rate");

        Setup(); var capture = CallMain("CaptureCombatPrep", .03, .03, null); var run = StartServerPolicy(capture, Phase("AT", "current", 60));
        levels = (long[]) Get(Get(Character, "advancedTraining"), "level"); levels[3] = 111; levels[4] = 222;
        Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep"); actual = Json(CallMain("GetCombatPrepRuntime"), "actual");
        Equal("111", JsonValue(actual, "energyAtLevel"), "tick-end actual AT level is observed, not the forecast target");
        Equal(((long[]) Get(Get(Character, "advancedTraining"), "energy"))[4].ToString(), JsonValue(actual, "magicAtAllocation"), "magic-AT allocation reports energy lane four");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}")); runtime = CallMain("GetCombatPrepRuntime"); actual = Json(runtime, "actual");
        Equal("0", JsonValue(actual, "energyAtAllocation"), "end snapshot shows cleared AT energy");
        Equal("0", JsonValue(actual, "magicAtAllocation"), "end snapshot shows cleared magic-AT energy");
        var timing = new[] { "elapsedSeconds", "remainingSeconds", "phaseElapsedSeconds", "phaseRemainingSeconds", "atTargetsReached" }.ToDictionary(k => k, k => Json(runtime, k).ToString());
        levels[3] = 333; Set(Character, "realGold", 4321.25d); Set(MainInstance, "_combatPrepLastActualRefresh", -1d);
        runtime = CallMain("GetCombatPrepRuntime"); actual = Json(runtime, "actual");
        Equal("333", JsonValue(actual, "energyAtLevel"), "closed run still exposes live available observations");
        Equal(4321.25d, Get(Json(actual, "gold"), "AsDouble"), "closed-run gold is observed rather than copied from capture");
        foreach (var pair in timing) Equal(pair.Value, Json(runtime, pair.Key).ToString(), "idle actual refresh preserves terminal " + pair.Key);

        Setup(); capture = CallMain("CaptureCombatPrep", .03, .03, null); run = StartServerPolicy(capture, Phase("Augments", "current", 60));
        actual = Json(CallMain("GetCombatPrepRuntime"), "actual");
        Equal(0, Get(Json(actual, "augmentIndex"), "AsInt"), "augment observation identifies the selected prep pair");
        var aug = ((Array) Get(Get(Character, "augments"), "augs")).GetValue(0);
        Equal(Get(aug, "augEnergy").ToString(), JsonValue(actual, "augmentEnergyAllocation"), "augment energy observes the selected lane");
        Equal(Get(aug, "upgradeEnergy").ToString(), JsonValue(actual, "upgradeEnergyAllocation"), "upgrade energy observes the selected lane");
        Set(aug, "augLevel", 9007199254740993L); Set(aug, "upgradeLevel", 9007199254740995L);
        Set(aug, "augEnergy", 9007199254740997L); Set(aug, "upgradeEnergy", 9007199254740999L); CallMain("CacheCombatPrepRuntime");
        actual = Json(CallMain("GetCombatPrepRuntime"), "actual");
        foreach (var pair in new Dictionary<string, string> { { "augmentLevel", "9007199254740993" }, { "upgradeLevel", "9007199254740995" },
            { "augmentEnergyAllocation", "9007199254740997" }, { "upgradeEnergyAllocation", "9007199254740999" } })
        {
            Equal(true, Get(Json(actual, pair.Key), "IsString"), pair.Key + " is a string"); Equal(pair.Value, JsonValue(actual, pair.Key), pair.Key + " preserves exact large values");
        }
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
        Setup(); Set(MainType, "Character", null); actual = Json(CallMain("GetCombatPrepRuntime"), "actual");
        Equal(false, Get(Json(actual, "available"), "AsBool"), "missing game data reports unavailable observations");
        Equal("0", JsonValue(actual, "energyAtAllocation"), "unavailable observations keep typed decimal-string defaults");
        Setup();
    }

    private static void CheckPreviewResponseEnvelope()
    {
        Setup(); BeardSlots = 7; DiggerSlots = 12;
        var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        var active = (IList)Get(Get(Character, "diggers"), "activeDiggers");
        for (var i = 0; i < rows.Count; i++)
        {
            Set(rows[i], "maxLevel", 20L); Set(rows[i], "curLevel", 20L); Set(rows[i], "active", true); active.Add(i);
        }
        var names = (string[]) Get(Get(Controller, "itemInfo"), "itemName");
        var oldStatus = Get(MainType, "_customAutomationStatus"); var oldResults = Get(MainType, "_externalUiCommandResultsDir");
        var oldRevision = Get(MainType, "_externalUiRuntimeRevision");
        var before = Resources(); var support = CallMain("CombatPrepSupportKey"); var recalls = Recalls; var swaps = Swaps;
        try
        {
            var limit = (int)Get(MainType, "ExternalUiActionResultMaxBytes");
            Equal(256 * 1024, limit, "backend shares native action-result limit rather than an obsolete feature cap");
            Set(MainType, "_externalUiRuntimeRevision", Get(MainType, "ExternalUiMaxRuntimeRevision"));
            var args = new object[] { Parse("{\"durationSeconds\":900}"), null };
            var message = Invoke(MainInstance, "PreviewCombatPrep", args); var preview = Json(args[1], "combatPrepPreview");
            var cache = Get(MainInstance, "_combatPrepPreview"); var plan = Get(cache, "Plan");
            var alternatives = (Array)Get(plan, "Alternatives");
            Equal(12, alternatives.Length, "support-rich fixture actually produces twelve ranked alternatives");
            Equal(12, Get(Json(preview, "candidates"), "Count"), "normal support-rich preview preserves all twelve alternatives");
            var families = alternatives.Cast<object>().Select(x => (string)Get(x, "Family")).Distinct().ToArray();
            Equal(true, families.Contains("ATThenWandoos"), "full preview retains AT family");
            Equal(true, families.Contains("BackgroundOnly"), "full preview retains background family");
            Equal(true, families.Contains("AugmentsThenNGUBackground"), "full preview retains augment/background family");
            Equal(12, Get(Json(Json(Json(preview, "baseline"), "support"), "diggers"), "Count"), "large preview uses actual shared support rows, not artificial JSON padding");
            Equal(true, Encoding.UTF8.GetByteCount(args[1].ToString()) < limit - 4 * 1024, "compact preview reserves four KiB of action-result headroom");
            int bytes;
            var result = WriteCombatPrepFixtureResponse(args[1], message, out bytes);
            Equal(true, bytes > 64 * 1024 && bytes < limit, "all twelve support-rich alternatives survive above old 64 KiB cap and below native 256 KiB wire limit");
            Equal(12, Get(Json(Json(Json(result, "data"), "combatPrepPreview"), "candidates"), "Count"), "actual pretty command writer preserves the twelve support-rich rows");
            Console.WriteLine("Combat prep support-rich 12-alternative envelope: " + bytes + " UTF-8 bytes.");

            Set(MainType, "_customAutomationStatus", new string('\u754c', 8000));
            var trimmed = false;
            for (var padding = 100; padding <= 2000; padding += 50)
            {
                for (var i = 40; i < 100; i++) names[i] = "Fixture " + i + new string('g', padding);
                args = new object[] { Parse("{\"durationSeconds\":900}"), null };
                message = Invoke(MainInstance, "PreviewCombatPrep", args); preview = Json(args[1], "combatPrepPreview");
                cache = Get(MainInstance, "_combatPrepPreview");
                if ((int)Get(Json(preview, "candidates"), "Count") < ((Array)Get(Get(cache, "Plan"), "Alternatives")).Length)
                { trimmed = true; break; }
            }
            Equal(true, trimmed, "long names and multibyte runtime eventually require bounded response trimming");
            alternatives = (Array)Get(Get(cache, "Plan"), "Alternatives");
            var winners = alternatives.Cast<object>().Select((policy, index) => new { Family = (string)Get(policy, "Family"), Id = "candidate-" + index })
                .GroupBy(x => x.Family).Select(x => x.First()).ToArray();
            Equal(true, winners.Length < alternatives.Length, "trim fixture contains duplicate-family extras");
            Equal(true, Encoding.UTF8.GetByteCount(args[1].ToString()) < limit - 4 * 1024, "trimmed compact data stays below 252 KiB");
            result = WriteCombatPrepFixtureResponse(args[1], message, out bytes);
            Equal(true, bytes < limit, "complete pretty UTF-8 envelope including multibyte runtime stays below 256 KiB");
            var emitted = Json(Json(result, "data"), "combatPrepPreview");
            var ids = ((IEnumerable)Get(Json(emitted, "candidates"), "Children")).Cast<object>().Select(c => JsonValue(c, "id")).ToArray();
            foreach (var winner in winners)
                Equal(true, ids.Contains(winner.Id), "duplicate-family trimming preserves highest-ranked family representative: " + winner.Family);
            Equal(true, Get(Json(Json(Json(result, "data"), "runtime"), "combatPrep"), "IsObject"), "bounded response retains nested runtime support observations");
            Equal(true, ids.Contains(JsonValue(emitted, "recommendedCandidateId")), "response trimming retains recommendation");
            Equal(true, ids.Contains("candidate-" + Array.IndexOf(alternatives, Get(Get(cache, "Plan"), "Baseline"))), "response trimming retains baseline");

            for (var i = 40; i < 100; i++) names[i] = "Fixture " + i + new string('g', 5000);
            args = new object[] { Parse("{\"durationSeconds\":900}"), null };
            message = Invoke(MainInstance, "PreviewCombatPrep", args); preview = Json(args[1], "combatPrepPreview");
            cache = Get(MainInstance, "_combatPrepPreview"); alternatives = (Array)Get(Get(cache, "Plan"), "Alternatives");
            Equal(true, (int) Get(Json(preview, "candidates"), "Count") < ((Array) Get(Get(cache, "Plan"), "Alternatives")).Length, "large runtime and names force actual response-budget trimming");
            result = WriteCombatPrepFixtureResponse(args[1], message, out bytes);
            Equal(true, bytes < limit, "family-winner fallback still respects complete action-result wire bound");
            emitted = Json(Json(result, "data"), "combatPrepPreview");
            ids = ((IEnumerable)Get(Json(emitted, "candidates"), "Children")).Cast<object>().Select(c => JsonValue(c, "id")).ToArray();
            Equal(true, ids.Length < winners.Length, "extreme payload exercises family-winner fallback when all families cannot fit");
            Equal(true, ids.Contains(JsonValue(emitted, "recommendedCandidateId")), "family-winner fallback retains recommendation");
            Equal(true, ids.Contains("candidate-" + Array.IndexOf(alternatives, Get(Get(cache, "Plan"), "Baseline"))), "family-winner fallback retains baseline");

            Set(MainType, "_customAutomationStatus", new string('\u754c', limit / 3 + 1000));
            Exception oversized = null;
            try { Preview(900); } catch (Exception error) { oversized = error; }
            Equal(true, oversized != null && oversized.ToString().Contains(limit + "-byte action-result bound"), "unshrinkable runtime fails closed with actual byte limit");
            Equal(cache, Get(MainInstance, "_combatPrepPreview"), "impossible response does not publish a partial preview");
            Equal(before, Resources(), "all envelope sizing remains read-only for resources, gear and settings");
            Equal(support, CallMain("CombatPrepSupportKey"), "all envelope sizing preserves native support selection and levels");
            Equal(recalls, Recalls, "preview sizing never recalls resources"); Equal(swaps, Swaps, "preview sizing never swaps gear");
            Equal(null, Get(MainInstance, "_combatPrepRun"), "preview sizing never starts a run");
        }
        finally
        {
            Set(MainType, "_customAutomationStatus", oldStatus); Set(MainType, "_externalUiCommandResultsDir", oldResults);
            Set(MainType, "_externalUiRuntimeRevision", oldRevision);
        }
    }

    private static object WriteCombatPrepFixtureResponse(object data, object message, out int bytes)
    {
        var attached = Invoke(MainType, "AttachExternalUiRuntime", data);
        var results = Path.Combine(Temp, "results"); Set(MainType, "_externalUiCommandResultsDir", results);
        var id = new string('x', 128);
        Invoke(MainType, "WriteExternalUiCommandResult", id, "previewCombatPrep", true, message, attached);
        var path = Path.Combine(results, id + ".json");
        Equal(true, File.Exists(path), "actual command result writer emits the bounded preview response");
        bytes = File.ReadAllBytes(path).Length;
        return Parse(File.ReadAllText(path));
    }

    private static void CheckNativeAutoNukePause()
    {
        Setup(); var boss = Get(Character, "bossController"); var arbitrary = Get(Character, "arbitrary"); var gameSettings = Get(Character, "settings");
        var timer = Get(arbitrary, "nukeTimer"); Set(gameSettings, "autoNukeOn", true); Set(arbitrary, "boughtAutoNuke", true);
        Set(timer, "totalseconds", 59.75d); Invoke(boss, "Update");
        Equal(true, Get(boss, "isFighting"), "positive control: real native Update starts an automatic nuke at 60 seconds");
        Equal(true, Get(boss, "nukeBoss"), "positive control: real startNuke sets native nuke flag");
        Equal(0d, Get(timer, "totalseconds"), "native Update resets the elapsed timer after triggering");
        Invoke(boss, "nukeBosses");
        Equal(false, Get(boss, "isFighting"), "real failed native nuke can end as a brief pulse");
        Set(gameSettings, "autoNukeOn", false); Set(timer, "totalseconds", 60d); Invoke(boss, "Update");
        Equal(true, Get(boss, "nukeBoss"), "native threshold branch can fire even with autoNukeOn false, so changing preferences is not a safe pause");

        Setup(); boss = Get(Character, "bossController"); arbitrary = Get(Character, "arbitrary"); gameSettings = Get(Character, "settings"); timer = Get(arbitrary, "nukeTimer");
        Set(gameSettings, "autoNukeOn", true); Set(arbitrary, "boughtAutoNuke", true); Set(timer, "totalseconds", 59.75d);
        var capture = CallMain("CaptureCombatPrep", .03, .03, null); var run = StartServerPolicy(capture, Phase("AT", "current", 7200));
        var started = Get(run, "Started"); var duration = Get(run, "Duration");
        Equal(59.75d, Get(run, "OriginalNukeSeconds"), "acquisition preserves the original native timer value");
        Equal(0d, Get(timer, "totalseconds"), "start zeros the owned timer before native Update can trigger");
        Equal(true, Json(CallMain("GetCombatPrepRuntime"), "warnings").ToString().Contains("Native automatic nuke timer paused"), "runtime discloses native timer pause without a preference change");
        var update = boss.GetType().GetMethod("Update", All); var tick = MainType.GetMethod("TickCombatPrep", All);
        var fighting = Field(boss, "isFighting"); var nuking = Field(boss, "nukeBoss"); var seconds = Field(timer, "totalseconds");
        var frames = 7200 * 4;
        for (var i = 0; i < frames; i++)
        {
            update.Invoke(boss, null);
            if ((bool) fighting.GetValue(boss) || (bool) nuking.GetValue(boss)) throw new Exception("Native auto-nuke fired during held runtime frame " + i);
            tick.Invoke(MainInstance, null);
            if ((double) seconds.GetValue(timer) != 0) throw new Exception("Native timer was not reset before the allocation guard at frame " + i);
        }
        Equal(true, Get(MainInstance, "CombatPrepActive"), "two virtual game-hours of real native Update cannot start an automatic nuke during the run");
        Equal(true, Get(gameSettings, "autoNukeOn"), "saved native autoNukeOn preference remains enabled throughout");
        Equal(true, Get(arbitrary, "boughtAutoNuke"), "purchase permission is never changed");
        Equal(started, Get(run, "Started"), "timer pause never restarts the run clock"); Equal(duration, Get(run, "Duration"), "timer pause never extends the original budget");
        Set(run, "Started", (double) Get(run, "Started") - 7201); CallMain("TickCombatPrep");
        Equal("cancelled", Get(run, "Status"), "global deadline still cancels unfinished AT during native timer pause");
        Equal(59.75d, Get(timer, "totalseconds"), "deadline restores the original timer, not the paused duration");
        Invoke(boss, "Update"); Equal(true, Get(boss, "nukeBoss"), "native automatic nuke resumes normally only after the run ends");

        foreach (var termination in new[] { "cancel", "completed", "failed", "unload", "handoff" })
        {
            Setup(); boss = Get(Character, "bossController"); arbitrary = Get(Character, "arbitrary"); gameSettings = Get(Character, "settings"); timer = Get(arbitrary, "nukeTimer");
            Set(gameSettings, "autoNukeOn", true); Set(arbitrary, "boughtAutoNuke", true); Set(timer, "totalseconds", 12.5d);
            capture = CallMain("CaptureCombatPrep", .03, .03, null); run = StartServerPolicy(capture, Phase("AT", "current", 60));
            Invoke(boss, "Update");
            if (termination == "cancel") CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            else if (termination == "unload") CallMain("Unload");
            else
            {
                CallMain("EndCombatPrep", termination == "completed" ? "completed" : termination == "failed" ? "failed" : "cancelled", "fixture " + termination, termination != "handoff");
            }
            Equal(termination == "handoff" ? .25d : 12.5d, Get(timer, "totalseconds"), "timer restoration respects lifecycle ownership: " + termination);
            Equal(false, Get(run, "NukeTimerOwned"), "timer lease is released: " + termination);
            Equal(true, Get(gameSettings, "autoNukeOn"), "lifecycle never writes the native saved preference: " + termination);
        }

        foreach (var conflict in new[] { "fight", "nuke", "bossPush" })
        {
            Setup(); boss = Get(Character, "bossController"); arbitrary = Get(Character, "arbitrary"); gameSettings = Get(Character, "settings"); timer = Get(arbitrary, "nukeTimer");
            Set(gameSettings, "autoNukeOn", true); Set(arbitrary, "boughtAutoNuke", true); Set(timer, "totalseconds", 25d);
            capture = CallMain("CaptureCombatPrep", .03, .03, null); run = StartServerPolicy(capture, Phase("AT", "current", 60));
            Set(timer, "totalseconds", 7.5d);
            var predicate = "";
            if (conflict == "fight") { Set(boss, "isFighting", true); predicate = "bossController.isFighting=true"; }
            if (conflict == "nuke") { Invoke(boss, "startNuke"); predicate = "bossController.nukeBoss=true"; }
            if (conflict == "bossPush") { Set(MainInstance, "_bossPushRunning", true); predicate = "BossPushRunning=true"; }
            Set(run, "LastRefresh", Get(MainType, "CombatPrepClock"));
            var oldWriter = Get(MainType, "OutputWriter");
            using (var log = new MemoryStream())
            using (var writer = new StreamWriter(log, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
            {
                try
                {
                    PreemptionLog = log; RequiredDiagnosticBeforeResourceMutation = predicate; Set(MainType, "OutputWriter", writer);
                    CallMain("TickCombatPrep");
                    Equal(0, ResourceDiagnosticChecks, "native combat/equipment handoff preserves funded prerequisites without cleanup writes: " + conflict);
                    Equal(0, ResourceMissingDiagnostics, "exact native preemption predicate is logged BEFORE every resource cleanup write: " + conflict);
                    var text = Encoding.UTF8.GetString(log.ToArray());
                    foreach (var detail in new[] { predicate, "nativeNukeTimer=7.5", "autoNukeOn=true" })
                        Equal(true, text.Contains(detail), "preemption log retains " + detail + " for " + conflict);
                }
                finally { Set(MainType, "OutputWriter", oldWriter); PreemptionLog = null; RequiredDiagnosticBeforeResourceMutation = null; }
            }
            Equal("cancelled", Get(run, "Status"), "manual/native combat and equipment conflicts cancel before the half-second guard: " + conflict);
            Equal(true, ((string) Get(run, "Reason")).Contains(predicate), "runtime reason identifies the exact predicate: " + conflict);
            Equal(7.5d, Get(timer, "totalseconds"), "manual/conflicting timer value is not reset or restored over: " + conflict);
            if (conflict == "fight" || conflict == "nuke") Equal(true, Get(boss, "isFighting"), "manual fight flag is never cleared by the pause");
            if (conflict == "nuke") Equal(true, Get(boss, "nukeBoss"), "manual native startNuke flag is never cleared");
            Equal(true, Get(gameSettings, "autoNukeOn"), "manual preemption preserves the saved preference");
        }

        foreach (var domain in new[] { "Gear", "Support" })
        {
            Setup(); arbitrary = Get(Character, "arbitrary"); gameSettings = Get(Character, "settings"); timer = Get(arbitrary, "nukeTimer");
            Set(gameSettings, "autoNukeOn", true); Set(arbitrary, "boughtAutoNuke", true); Set(timer, "totalseconds", 25d);
            capture = CallMain("CaptureCombatPrep", .03, .03, null); run = StartServerPolicy(capture, Phase("AT", "current", 60));
            var claim = BeginFixtureClaim(domain, "nuke-pause-setup-interference"); var writes = ResourceCommittedWrites;
            Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
            var phase = ((Array)Get(Get(run, "Policy"), "Phases")).GetValue((int)Get(run, "PhaseIndex"));
            Equal(false, CallMain("CombatPrepRequirementsVerified", run, phase), domain + " claim denies current phase prerequisites");
            Equal(false, Get(run, "SetupVerified"), domain + " claim withholds dependent setup proof");
            Equal(-1, Get(run, "AppliedResourcePhase"), domain + " claim withholds resource credit");
            Equal(writes, ResourceCommittedWrites, domain + " claim preserves funded prerequisites");
            Equal(0d, Get(timer, "totalseconds"), "setup claim does not revoke the independent native timer pause");
            Equal(true, Get(gameSettings, "autoNukeOn"), "setup claim never changes the saved native preference");
            Equal(true, CallMain("EndExecutionClaim", claim, false), "setup claim releases without historical writes");
            Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
            Equal(true, Get(run, "SetupVerified"), "released " + domain + " claim permits actual phase verification");
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            Equal(25d, Get(timer, "totalseconds"), "owned timer restores only after authorized cancellation");
        }

        foreach (var lost in new[] { "reference", "value", "epoch" })
        {
            Setup(); arbitrary = Get(Character, "arbitrary"); gameSettings = Get(Character, "settings"); timer = Get(arbitrary, "nukeTimer");
            Set(gameSettings, "autoNukeOn", true); Set(arbitrary, "boughtAutoNuke", true); Set(timer, "totalseconds", 25d);
            capture = CallMain("CaptureCombatPrep", .03, .03, null); run = StartServerPolicy(capture, Phase("AT", "current", 60));
            if (lost == "reference") timer = Child(arbitrary, "nukeTimer");
            Set(timer, "totalseconds", 20d);
            if (lost == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
            CallMain("TickCombatPrep");
            Equal(false, Get(MainInstance, "CombatPrepActive"), "lost timer ownership/epoch ends the overlay: " + lost);
            Equal(20d, Get(timer, "totalseconds"), "foreign/new-epoch timer is not overwritten: " + lost);
            Equal(true, Get(gameSettings, "autoNukeOn"), "ownership loss never changes native saved preference");
        }
        Setup(); arbitrary = Get(Character, "arbitrary"); gameSettings = Get(Character, "settings"); timer = Get(arbitrary, "nukeTimer");
        Set(gameSettings, "autoNukeOn", true); Set(arbitrary, "boughtAutoNuke", true); Set(timer, "totalseconds", 18d);
        capture = CallMain("CaptureCombatPrep", .03, .03, null); run = StartServerPolicy(capture, Phase("AT", "current", 60));
        Set(gameSettings, "autoNukeOn", false); CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
        Equal(false, Get(gameSettings, "autoNukeOn"), "user preference edits during a run are not restored from an old snapshot");
        Equal(18d, Get(timer, "totalseconds"), "owned timer can restore without touching the edited preference");

        Setup(); arbitrary = Get(Character, "arbitrary"); Set(arbitrary, "nukeTimer", null);
        var beforeRecalls = Recalls; var beforeSwaps = Swaps; var preview = Preview(10);
        foreach (var candidate in (IEnumerable) Get(Json(preview, "candidates"), "Children"))
            if ((int) Get(Json(candidate, "phases"), "Count") > 0)
            {
                Equal(false, Get(Json(candidate, "available"), "AsBool"), "preview remains readable but unavailable timer blocks execution");
                Equal(true, JsonValue(candidate, "reason").Contains("Native automatic nuke timer pause"), "unsupported timer execution blocker is explicit in preview");
            }
        Reject(() => Start(JsonValue(preview, "previewId"), FindCandidate(preview, "ATThenWandoos")), "start fails closed without a supported native timer");
        Equal(beforeRecalls, Recalls, "unsupported timer start never recalls resources"); Equal(beforeSwaps, Swaps, "unsupported timer start never swaps gear");
        Setup();
    }

    private static object Phase(string kind, string key, double seconds)
    {
        var phase = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.CombatPrepPhase", true));
        Set(phase, "Kind", kind); Set(phase, "GearProfileKey", key); Set(phase, "DurationSeconds", seconds);
        if (kind == "Augments") Set(phase, "AugmentTierId", 0);
        return phase;
    }

    // Direct math/actuator checks have no phase controller. Resolution must be read-only;
    // its exact map is then executed by the SAME real shared resource authority.
    private static void ApplyResolvedCombatResources(object phase, object snapshot, long energy, long magic, bool allowTimeMachine)
    {
        var args = new object[] { phase, snapshot, energy, magic, allowTimeMachine, null };
        var writes = ResourceCommittedWrites;
        var desired = CallMain("ResolveCombatPrepResources", args);
        Equal(writes, ResourceCommittedWrites, "combat resource requirements are resolved without native model writes");
        var intent = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceSetupIntent", true), true);
        var epoch = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.LoadoutManager+SnapshotEpoch", true), true);
        var owner = MainInstance; var settings = Settings;
        Set(intent, "Owner", "combat:direct-native-fixture"); Set(intent, "Desired", desired);
        Set(intent, "Energy", true); Set(intent, "Magic", true);
        Set(intent, "Authorized", (Func<bool>)(() => (bool)Invoke(epoch, "Matches") && ReferenceEquals(owner, Get(MainType, "reference"))
            && ReferenceEquals(settings, Get(MainType, "Settings")) && (bool)Get(settings, "ManageEnergy") && (bool)Get(settings, "ManageMagic")));
        var result = Invoke(CallMain("get__resourceSetupExecutor"), "Reconcile", intent);
        if (!(bool)Get(result, "Verified")) throw (Exception)Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceMutationException", true), All, null, new[] { result }, null);
        foreach (var fallback in (IEnumerable)args[5]) Invoke(Plugin.GetType("NGUInjector.Managers.BlackBarAllocator", true), "ObserveApplied", fallback);
    }

    private static object Project(object capture, params object[] phases)
    {
        var array = Array.CreateInstance(Plugin.GetType("NGUInjector.Managers.CombatPrepPhase", true), phases.Length);
        for (var i = 0; i < phases.Length; i++) array.SetValue(phases[i], i);
        var snapshot = Get(capture, "Snapshot");
        return Invoke(Plugin.GetType("NGUInjector.Managers.CombatPrepPlanner", true), "Project", snapshot, Get(snapshot, "CurrentOsId"), array);
    }

    private static object StartServerPolicy(object capture, params object[] phases)
    {
        var policy = Project(capture, phases); var plan = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.CombatPrepPlanResult", true));
        var alternatives = Array.CreateInstance(policy.GetType(), 1); alternatives.SetValue(policy, 0);
        Set(plan, "Success", true); Set(plan, "Alternatives", alternatives); Set(plan, "DurationSeconds", phases.Sum(p => (double) Get(p, "DurationSeconds")));
        var cache = Activator.CreateInstance(MainType.GetNestedType("CombatPrepPreviewCache", BindingFlags.NonPublic), true);
        var id = Guid.NewGuid().ToString("N"); Set(cache, "Id", id); Set(cache, "Created", Get(MainType, "CombatPrepClock")); Set(cache, "Capture", capture); Set(cache, "Plan", plan);
        Set(MainInstance, "_combatPrepPreview", cache); Start(id, "candidate-0");
        return Get(MainInstance, "_combatPrepRun");
    }

    private static void CompleteAugmentPhase(object run)
    {
        var phase = ((Array) Get(Get(run, "Policy"), "Phases")).GetValue(0);
        var aug = ((Array) Get(Get(Character, "augments"), "augs")).GetValue((int) Get(phase, "AugmentTierId"));
        Set(aug, "augLevel", Get(phase, "AugmentTarget")); Set(aug, "upgradeLevel", Get(phase, "UpgradeTarget"));
        Set(run, "PhaseDeadline", (double) Get(run, "Started") - 1); Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
    }

    private static object Preview(double seconds)
    {
        var args = new object[] { Parse("{\"durationSeconds\":" + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"), null };
        Invoke(MainInstance, "PreviewCombatPrep", args); return Json(args[1], "combatPrepPreview");
    }
    private static string FindCandidate(object preview, string family)
    {
        var cache = Get(MainInstance, "_combatPrepPreview"); var alternatives = (Array) Get(Get(cache, "Plan"), "Alternatives");
        for (var i = 0; i < alternatives.Length; i++) if ((string) Get(alternatives.GetValue(i), "Family") == family) return "candidate-" + i;
        throw new Exception("Missing family: " + family);
    }
    private static void Start(string id, string candidate) { CallMain("StartCombatPrep", Parse("{\"previewId\":\"" + id + "\",\"candidateId\":\"" + candidate + "\"}")); }
    private static string Resources() { return Get(Character, "idleEnergy") + "/" + Get(Get(Character, "magic"), "idleMagic") + "/" + Get(Get(Character, "inventory"), "head").GetHashCode() + "/" + Get(Settings, "AutoRebirth"); }
    private static object Parse(string text) { return Invoke(Plugin.GetType("NGUInjector.JsonFiles", true), "ParseObject", text); }
    private static object Json(object node, string key) { return node.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(node, new object[] { key }); }
    private static string JsonValue(object node, string key) { return (string) Get(Json(node, key), "Value"); }
    private static void Setting(string name, object value) { Set(Settings, name, value); }
    private static object Blank(Type type)
    {
        var value = FormatterServices.GetUninitializedObject(type);
        for (var parent = type; parent != null; parent = parent.BaseType)
        {
            var pointer = parent.GetField("m_CachedPtr", All);
            if (pointer != null) { pointer.SetValue(value, new IntPtr(1)); break; }
        }
        return value;
    }
    private static object Child(object parent, string name) { var field = Field(parent, name); var value = Blank(field.FieldType); field.SetValue(parent, value); return value; }
    private static IList ListField(object parent, string name, int count)
    {
        var field = Field(parent, name); var list = (IList) Activator.CreateInstance(field.FieldType); var type = field.FieldType.GetGenericArguments()[0];
        for (var i = 0; i < count; i++) list.Add(type.IsValueType ? Activator.CreateInstance(type) : Blank(type));
        field.SetValue(parent, list); return list;
    }
    private static Array ArrayField(object parent, string name, int count)
    {
        var field = Field(parent, name); var array = Array.CreateInstance(field.FieldType.GetElementType(), count);
        for (var i = 0; i < count; i++) array.SetValue(Blank(field.FieldType.GetElementType()), i);
        field.SetValue(parent, array); return array;
    }
    private static FieldInfo Field(object owner, string name)
    {
        for (var type = owner as Type ?? owner.GetType(); type != null; type = type.BaseType)
        { var f = type.GetField(name, All); if (f != null) return f; }
        throw new Exception("Missing field " + (owner as Type ?? owner.GetType()).Name + "." + name);
    }
    private static object Get(object owner, string name)
    {
        var type = owner as Type ?? owner.GetType(); var p = type.GetProperty(name, All);
        return p == null ? Field(owner, name).GetValue(owner is Type ? null : owner) : p.GetValue(owner is Type ? null : owner);
    }
    private static void Set(object owner, string name, object value)
    {
        var type = owner as Type ?? owner.GetType(); var p = type.GetProperty(name, All);
        if (p != null) { p.SetValue(owner is Type ? null : owner, value); return; }
        var field = Field(owner, name);
        if (value != null && field.FieldType.IsPrimitive) value = Convert.ChangeType(value, field.FieldType);
        field.SetValue(owner is Type ? null : owner, value);
    }
    private static void SetEnum(object owner, string field, string value) { Set(owner, field, Enum.Parse(Field(owner, field).FieldType, value)); }
    private static object CallMain(string name, params object[] args) { return Invoke(MainInstance, name, args); }
    private static object Invoke(object owner, string name, params object[] args)
    {
        var type = owner as Type ?? owner.GetType();
        var method = type.GetMethods(All).SingleOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length)
            ?? type.GetMethods(All).Single(x => x.Name == name && x.GetParameters().Length > args.Length && x.GetParameters().Skip(args.Length).All(p => p.IsOptional));
        if (method.GetParameters().Length > args.Length) args = args.Concat(method.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray();
        try { return method.Invoke(owner is Type || method.IsStatic ? null : owner, args); }
        catch (TargetInvocationException error) { throw new Exception(type.Name + "." + name + ": " + error.InnerException, error.InnerException); }
    }
    private static void Equal(object expected, object actual, string message)
    { Assertions++; if (!Equals(expected, actual)) throw new Exception(message + ": expected " + expected + ", actual " + actual); }
    private static void Near(double expected, double actual, string message)
    { Assertions++; if (double.IsNaN(expected) || double.IsInfinity(expected) || double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(expected - actual) > 1e-6 * Math.Max(1, Math.Abs(expected))) throw new Exception(message + ": " + actual); }
    private static void Reject(Action action, string message)
    { Assertions++; try { action(); } catch { return; } throw new Exception(message + ": expected rejection"); }
}
