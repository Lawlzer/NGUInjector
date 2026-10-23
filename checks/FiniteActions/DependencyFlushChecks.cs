using System;
using System.IO;
using System.Linq;
using NGUInjector;

internal static class DependencyFlushChecks
{
    internal static int Run(string root)
    {
        var count = 0;
        void Equal<T>(T expected, T actual, string message)
        {
            count++;
            if (!Equals(expected, actual)) throw new InvalidOperationException(message + ": expected " + expected + ", actual " + actual);
        }
        var path = Path.Combine(root, "bounded-result-flush"); var ledger = new FiniteActionLedger(path);
        for (var i = 0; i < 9; i++)
        {
            var record = new FiniteActionRecord { Identity = new FiniteActionIdentity("pending-result-" + i, "owner", "rebirth:1", "entry", "effect"),
                State = FiniteActionState.Invocation, Epoch = "rebirth:1", Domains = (int)NGUInjector.Main.ExecutionDomain.Inventory,
                Before = "before", After = "committed model" };
            ledger.Save(record);
            // Simulate already-classified results not yet stored after their native effect.
            record.State = FiniteActionState.Committed; record.Dirty = true;
        }
        var next = new FiniteActionIdentity("successor", "owner", "rebirth:1", "entry", "next");
        Equal(true, ledger.HasUnresolvedDependencies(next, "rebirth:1", (int)NGUInjector.Main.ExecutionDomain.Inventory), "dirty committed results block dependent admission");
        Equal(true, ledger.FlushDependentResults(next, "rebirth:1", (int)NGUInjector.Main.ExecutionDomain.Energy), "unrelated Energy needs no storage drain");
        Equal(9, ledger.Records.Count(x => x.Dirty), "healthy unrelated admission does not globally flush result files");
        Equal(false, ledger.FlushDependentResults(next, "rebirth:1", (int)NGUInjector.Main.ExecutionDomain.Inventory, 0), "zero flush budget cannot admit dependent work");
        Equal(9, ledger.Records.Count(x => x.Dirty), "zero budget produces no result writes");
        var imported = new FiniteActionLedger(path);
        Equal(9, imported.Records.Count(x => x.State == FiniteActionState.Unknown), "unfinished durable invocations import as unknown, not committed assumptions");
        for (var remaining = 9; remaining > 0; remaining -= 2)
        {
            var complete = ledger.FlushDependentResults(next, "rebirth:1", (int)NGUInjector.Main.ExecutionDomain.Inventory, 2);
            Equal(Math.Max(0, remaining - 2), ledger.Records.Count(x => x.Dirty), "one admission flushes at most its bounded result budget");
            Equal(remaining <= 2, complete, "dependent admission waits until every required result is durable");
        }
        Equal(false, ledger.HasUnresolvedDependencies(next, "rebirth:1", (int)NGUInjector.Main.ExecutionDomain.Inventory), "bounded successful storage recovery reopens dependent admission");
        Equal(9, new FiniteActionLedger(path).Records.Count(x => x.State == FiniteActionState.Committed), "storage recovery durably records every known effect without native replay");
        return count;
    }
}
