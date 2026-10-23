using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static int Main(string[] args)
    {
        var root = args.Length >= 1 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Supply the production source directory.");
        var files = new[] { "Main.CombatRouting.cs", "Main.CustomAdventure.cs", "Main.ManualTitan.cs", "Main.MaxItopodPush.cs",
            "Main.TitanAutomation.cs", "Main.BossPush.cs", "Main.CombatPrep.cs", "Managers/CombatManager.cs", "Managers/CombatHelpers.cs",
            "Managers/GearOptimizer.RouteSelection.cs", "../checks/PresetIntegration/RouteExecutorCutoverChecks.cs" };
        var assertions = 0;
        foreach (var file in files)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, file)), path: file);
            var errors = tree.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0) throw new Exception(string.Join(Environment.NewLine, errors.Select(x => x.ToString())));
            assertions++;
            if (file == "Main.CombatPrep.cs") continue;
            var banned = new[] { "SaveCurrentLoadout", "AcquireLock", "RestoreGear", "ApplyExactLoadoutObserved", "OptimizeAndEquip", "OptimizeAndEquipForAutokill" };
            var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                var member = invocation.Expression as MemberAccessExpressionSyntax;
                if (member != null && banned.Contains(member.Name.Identifier.ValueText))
                    throw new Exception(file + " retains a removed route writer: " + member.Name);
                assertions++;
            }
            foreach (var assignment in tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var member = assignment.Left as MemberAccessExpressionSyntax;
                if (member != null && new[] { "isFighting", "nukeBoss" }.Contains(member.Name.Identifier.ValueText))
                    throw new Exception(file + " fabricates native boss retirement: " + member.Name);
                assertions++;
            }
        }
        Console.WriteLine("PASS: " + assertions + " source syntax/writer-inventory assertions. NOT native or type-checked acceptance.");
        if (args.Length == 3)
        {
            // Read-only semantic diagnostics: no production project build or emitted DLL.
            var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(x => x == "bin" || x == "obj"))
                .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path));
            var references = Directory.EnumerateFiles(args[2], "*.dll")
                .Where(path => !new[] { "System.EnterpriseServices.Wrapper.dll", "System.EnterpriseServices.Thunk.dll" }.Contains(Path.GetFileName(path))).Concat(new[] {
                "Assembly-CSharp.dll", "UnityEngine.dll", "UnityEngine.CoreModule.dll", "UnityEngine.IMGUIModule.dll",
                "UnityEngine.InputLegacyModule.dll", "UnityEngine.JSONSerializeModule.dll", "UnityEngine.UI.dll" }
                .Select(name => Path.Combine(args[1], name))).Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("RouteReadOnlyDiagnostics", sources, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var errors = compilation.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
            foreach (var error in errors) Console.WriteLine(error);
            Console.WriteLine("Read-only semantic errors: " + errors.Length + ". No production DLL emitted.");
            return errors.Length == 0 ? 0 : 1;
        }
        return 0;
    }
}
