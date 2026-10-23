using System;
using System.Collections.Generic;
using System.Linq;

internal static class AutoExpProgram
{
    internal static readonly List<(string Name, Action Body)> Tests = new();
    private static int assertions;

    private static int Main(string[] args)
    {
#if NATIVE
        using var boundary = NativeBoundary.Initialize(args[0], args[1]);
        args = args.Skip(2).ToArray();
        IntegrationChecks.Register();
#else
        PlannerChecks.Register();
#endif
        var selected = Tests.Where(t => args.Length == 0 || t.Name.IndexOf(args[0], StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        if (selected.Length == 0)
        {
            Console.Error.WriteLine("No matching checks.");
            return 2;
        }
        var failures = 0;
        foreach (var test in selected)
        {
            try
            {
                test.Body();
                Console.WriteLine("PASS " + test.Name);
            }
            catch (Exception e)
            {
                failures++;
                Console.Error.WriteLine("FAIL " + test.Name + ": " + e);
            }
        }
        Console.WriteLine($"{selected.Length - failures}/{selected.Length} checks passed; {failures} failed; {assertions} assertions.");
        return failures == 0 ? 0 : 1;
    }

    internal static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Equal<T>(T expected, T actual, string message)
    {
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
    }

    internal static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        Check(expected.SequenceEqual(actual), $"{message}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }
}
