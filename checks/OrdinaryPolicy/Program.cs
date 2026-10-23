using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static Assembly Game;
    internal static string InjectorPath;
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 3 || args.Length > 4 || args.Length == 4 && args[3] != "--terminal-hold")
                throw new ArgumentException("Pass Managed directory, NGUInjector.dll and BlackBarChecks.exe; optionally --terminal-hold.");
            var managed = args[0];
            InjectorPath = Path.GetFullPath(args[1]);
            NativePolicyFixture.Checks = Assembly.LoadFrom(Path.GetFullPath(args[2]));
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
                var name = new AssemblyName(e.Name).Name;
                if (name == "Assembly-CSharp" && Game != null) return Game;
                var path = Path.Combine(managed, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            using (var game = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll")))
            {
                var bonuses = new[] {"totalEnergyPowerBonus", "totalMagicPowerBonus", "totalEnergyCapBonus", "totalMagicCapBonus",
                    "totalAugSpeedBonus", "getBonus", "augDiscount", "potionModifier", "totalAdvancedTrainingSpeedBonus"};
                foreach (var type in game.MainModule.Types)
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    double? value = null;
                    if (bonuses.Contains(method.Name)) value = 1;
                    if (new[] {"completions", "evilCompletions", "sadisticCompletions"}.Contains(method.Name)
                        && type.Name.Contains("Challenge")) value = 0;
                    if (type.Name == "Character" && new[] {"goldPerSecond", "grossGoldPerSecond"}.Contains(method.Name)) value = 1000000;
                    if (type.Name == "Character" && new[] {"totalWandoosEnergySpeed", "totalWandoosMagicSpeed"}.Contains(method.Name)) value = 1;
                    // Match BlackBar's empty-gear/locked-support observation inputs.
                    // Keep ordinary AUG prices, progress and allocation decisions native.
                    if (type.Name == "InventoryController" && new[] {"curSpaces", "accessorySpaces", "weapon2Unlocked", "weapon2Factor"}.Contains(method.Name)) value = 0;
                    if (type.Name == "Character" && method.Name == "machineBossMulti"
                        || type.Name == "TimeMachineController" && new[] {"barFillsPerSecond", "speedGoldMultiBonus", "goldMultiBonus"}.Contains(method.Name)
                        || type.Name == "AllBloodMagicController" && method.Name == "goldBonus"
                        || type.Name == "AllNGUController" && method.Name == "timeMachineBonus"
                        || type.Name == "TimeMachineChallengeController" && method.Name == "totalGPSbonus"
                        || type.Name == "AllGoldDiggerController" && method.Name == "totalLevelBonus") value = 1;
                    if (new[] {"AugmentController", "AdvancedTrainingController", "Wandoos98Controller"}.Contains(type.Name)
                        && new[] {"updateAugTexts", "updateUpgradeTexts", "updateText", "refresh", "refreshMenu"}.Contains(method.Name)
                        || type.Name.Contains("Aug") && method.Name == "updateMenu")
                    {
                        method.Body = new Mono.Cecil.Cil.MethodBody(method);
                        var il = method.Body.GetILProcessor();
                        il.Emit(OpCodes.Ldstr, type.Name + "." + method.Name);
                        il.Emit(OpCodes.Call, game.MainModule.ImportReference(typeof(PolicyChecks).GetMethod("Render")));
                        il.Emit(OpCodes.Ret);
                        continue;
                    }
                    if (!value.HasValue) continue;
                    method.Body = new Mono.Cecil.Cil.MethodBody(method);
                    var body = method.Body.GetILProcessor();
                    if (method.ReturnType.FullName == "System.Single") body.Emit(OpCodes.Ldc_R4, (float) value.Value);
                    else if (method.ReturnType.FullName == "System.Double") body.Emit(OpCodes.Ldc_R8, value.Value);
                    else if (method.ReturnType.FullName == "System.Int64") body.Emit(OpCodes.Ldc_I8, (long) value.Value);
                    else if (method.ReturnType.FullName != "System.Void") body.Emit(OpCodes.Ldc_I4, (int) value.Value);
                    body.Emit(OpCodes.Ret);
                }
                using (var bytes = new MemoryStream()) { game.Write(bytes); Game = Assembly.Load(bytes.ToArray()); }
            }
            NativePolicyFixture.Production = (Assembly) NativePolicyFixture.Call(
                NativePolicyFixture.Checks.GetType("ProtectedTimingIntegrationChecks", true), "LoadInjector", args[1]);
            var failures = new List<Exception>();
            if (!args.Contains("--terminal-hold"))
            {
                try { typeof(PolicyChecks).GetMethod("Run").Invoke(null, null); }
                catch (Exception error) { failures.Add(error); }
            }
            try { TerminalHoldIntegrationChecks.Run(); }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("OrdinaryPolicy failed; no full pass is claimed.", failures);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
