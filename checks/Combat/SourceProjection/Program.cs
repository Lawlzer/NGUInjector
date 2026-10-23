using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Extract exact decision bodies, not a replacement executor. All action choices
// terminate in record-only game move boundaries. No grant/route/setup code is emitted.
internal static class Program
{
    private static MethodDeclarationSyntax[] Methods(string root, string file) => CSharpSyntaxTree.ParseText(
        File.ReadAllText(Path.Combine(root, file))).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();

    private static int Main(string[] args)
    {
        var manager = Methods(args[0], "Managers/CombatManager.cs");
        var names = new[] { "DoCombat", "GetHPPercentage", "CombatBuffs", "CombatAttacks", "TryHandleWalderpSays",
            "TryWalderpAlternateAttack", "TryWalderpAttack", "IsWalderpFight", "WalderpSecondsUntilPrompt", "TryGetEnemyAIValue", "IsZoneUnlocked" };
        var decision = string.Join("\n", names.Select(name => manager.Single(x => x.Identifier.ValueText == name).ToFullString()));
        var helpers = Methods(args[0], "Managers/CombatHelpers.cs");
        var reads = string.Join("\n", helpers.Where(x => x.Identifier.ValueText != "Current" && !x.Identifier.ValueText.StartsWith("Cast")).Select(x => x.ToFullString()));
        // Keep the actual button/active-state predicates, but emit only the move
        // request. Authority and native side-effect acceptance belong to native tests.
        var casts = string.Join("\n", helpers.Where(x => x.Identifier.ValueText.StartsWith("Cast")).Select(method => {
            var condition = method.DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
            var terms = condition.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Where(x => x.Expression.ToString() == "Current").ToArray();
            var filtered = condition.ReplaceNodes(terms, (old, rewritten) => SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression));
            var test = filtered.ToString().Replace("true && ", "").Replace(" && true", "").Replace("!true || ", "").Replace(" || !true", "");
            if (method.Identifier.ValueText == "CastParalyze") test = "!(" + test + ")";
            var move = method.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(x => x.Expression.ToString().EndsWith(".doMove"));
            return "internal static bool " + method.Identifier + "() { if (!(" + test + ")) return false; " + move + "; return true; }";
        }));
        var wrappers = string.Join("\n", helpers.Where(x => x.Identifier.ValueText.StartsWith("Cast")).Select(x =>
            "private bool " + x.Identifier + "() => CombatHelpers." + x.Identifier + "();"));
        var rules = Methods(args[0], "Main.CustomAdventure.cs");
        var ruleReads = string.Join("\n", new[] { "FindMatchingAdventureRule", "ResolveConfiguredAdventureGoal", "GetAdventureRulesRuntime" }
            .Select(name => rules.Single(x => x.Identifier.ValueText == name).ToFullString()));
        var output = "using System; using System.Reflection; using NGUInjector.Managers; using SimpleJSON; using static NGUInjector.Managers.CombatHelpers;\n"
            + "namespace NGUInjector.Managers { internal sealed class CombatManager { private readonly Character _character = Main.Character; private readonly PlayerController _pc = Main.PlayerController; "
            + "internal void Decide(bool fast) => DoCombat(fast); private bool InvokeMove(Action move) { move(); return true; } "
            + wrappers + decision + " } internal static class CombatHelpers { " + reads + casts + " } }\n"
            + "namespace NGUInjector { internal partial class Main { private const int CustomAdventureModeFarmZone = 1, CustomAdventureModeManualZone = 4, CustomAdventureModeMaxItopodPush = 3; " + ruleReads + " } }";
        if (output.Contains("ExecutionGrant") || output.Contains("ExecutionDomain") || output.Contains("Current(grant)"))
            throw new Exception("Decision extraction must not emit executor authority.");
        if (!Directory.Exists(Path.GetDirectoryName(args[1]))) throw new DirectoryNotFoundException("Build intermediate parent is unavailable.");
        File.WriteAllText(args[1], output);
        return 0;
    }
}
