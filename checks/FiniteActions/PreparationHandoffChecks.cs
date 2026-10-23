using System;
using System.IO;
using System.Text;
using NGUInjector;

internal static class PreparationHandoffChecks
{
    internal static int Run(string root)
    {
        var checks = 0;
        void Equal<T>(T expected, T actual, string label)
        {
            checks++;
            if (!Equals(expected, actual)) throw new InvalidOperationException(label + ": expected " + expected + ", actual " + actual);
        }
        var original = new FiniteActionIdentity("fight-preparation", "fight-preparation", "rebirth:7", "rebirth:7", "original-rebirth-request");
        var successor = new FiniteActionIdentity("fight-preparation", "fight-preparation", "rebirth:7", "rebirth:7", "explicit-manual-request");
        var id = new FiniteActionIdentity("preparation-handoff", successor.Owner, successor.Acquisition, original.Key, successor.Key);
        var record = new FiniteActionRecord { Identity = id, State = FiniteActionState.Prepared, After = "selected",
            Before = "original scope and selected successor evidence", Epoch = "rebirth:7", Domains = 376 };
        var directory = Path.Combine(root, "preparation-handoff"); var ledger = new FiniteActionLedger(directory);
        ledger.Save(record);
        Equal(false, NGUInjector.Main.PreparationSupersessionBlocksImport(record, original), "selection alone does not retire the previous intent");
        Equal(false, NGUInjector.Main.PreparationRetirementDurable(record), "preflight selection grants no retirement completion");
        record.State = FiniteActionState.Invocation; record.After = "retirement-authorized"; ledger.Save(record);
        Equal(true, NGUInjector.Main.PreparationSupersessionBlocksImport(record, original), "durable handoff admission prevents old intent resurrection");
        Equal(false, NGUInjector.Main.PreparationRetirementDurable(record), "authorized retirement is not observed retirement");
        var imported = new FiniteActionLedger(directory).Find(id);
        Equal(FiniteActionState.Unknown, imported.State, "interrupted native retirement imports as unknown, never selected again");
        Equal(true, NGUInjector.Main.PreparationSupersessionBlocksImport(imported, original), "unknown handoff cannot resume the old rebirth request");
        Equal(false, NGUInjector.Main.PreparationRetirementDurable(imported), "unknown retirement cannot start a replacement process");
        record.State = FiniteActionState.Committed; record.After = "retired"; record.Dirty = true;
        Equal(false, NGUInjector.Main.PreparationRetirementDurable(record), "known native retirement waits for durable result before replacement");
        var file = Path.Combine(directory, JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes(id.Key)) + ".json");
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { ledger.Save(record); throw new InvalidOperationException("locked retirement result unexpectedly saved"); }
            catch (IOException) { }
            Equal(true, record.Dirty, "failed retirement result storage retains dirty known progress");
            Equal(false, NGUInjector.Main.PreparationRetirementDurable(record), "storage fault never admits replacement work");
        }
        ledger.Save(record);
        Equal(true, NGUInjector.Main.PreparationRetirementDurable(record), "exact retired result opens replacement only after storage recovery");
        Equal(original.Key, record.Identity.Entry, "handoff retains exact predecessor identity and scope");
        Equal(successor.Key, record.Identity.Opportunity, "successor is the explicit request, not a retry-generated GUID");
        Equal("original scope and selected successor evidence", record.Before, "storage retries preserve original acquired handoff evidence");
        Equal(true, NGUInjector.Main.PreparationSupersessionBlocksImport(new FiniteActionLedger(directory).Find(id), original), "committed tombstone remains effective after reload");
        var unrelated = new FiniteActionIdentity("fight-preparation", "fight-preparation", "rebirth:7", "rebirth:7", "other-request");
        Equal(false, NGUInjector.Main.PreparationSupersessionBlocksImport(record, unrelated), "tombstone does not withdraw unrelated preparation authority");
        record.State = FiniteActionState.Rejected;
        Equal(false, NGUInjector.Main.PreparationSupersessionBlocksImport(record, original), "rejected admission cannot retire old intent");
        return checks;
    }
}
