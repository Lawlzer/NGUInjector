using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SimpleJSON;

namespace NGUInjector
{
    internal sealed class FiniteActionIdentity
    {
        internal readonly string Kind, Owner, Acquisition, Entry, Opportunity, Key, Scope;
        internal FiniteActionIdentity(string kind, string owner, string acquisition, string entry, string opportunity)
        {
            var parts = new[] { kind, owner, acquisition, entry, opportunity };
            if (parts.Any(string.IsNullOrEmpty)) throw new ArgumentException("Finite actions require complete semantic identity.");
            Kind = kind; Owner = owner; Acquisition = acquisition; Entry = entry; Opportunity = opportunity;
            // Length prefixes avoid aliasing identities containing punctuation.
            Scope = string.Concat(parts.Take(4).Select(x => x.Length + ":" + x));
            Key = Scope + opportunity.Length + ":" + opportunity;
        }
    }

    internal enum FiniteActionState { Prepared, Invocation, Committed, Unknown, Rejected }

    internal sealed class FiniteActionRecord
    {
        internal FiniteActionIdentity Identity;
        internal FiniteActionState State;
        internal string Before, After, Disposition;
        internal string Epoch;
        internal int Domains;
        internal bool Dirty;
    }

    // Semantic commitments only. An imported invocation has no current native authority.
    internal sealed class FiniteActionLedger
    {
        private readonly string directory;
        private readonly Dictionary<string, FiniteActionRecord> records = new Dictionary<string, FiniteActionRecord>();
        internal FiniteActionLedger(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Finite-action storage is unavailable.");
            this.directory = directory;
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                var root = JsonFiles.ParseObject(File.ReadAllText(path));
                if (root["version"].AsInt != 1) throw new FormatException("Unsupported finite-action ledger version.");
                var id = new FiniteActionIdentity(root["kind"].Value, root["owner"].Value,
                    root["acquisition"].Value, root["entry"].Value, root["opportunity"].Value);
                FiniteActionState state;
                if (!Enum.TryParse(root["state"].Value, out state) || !Enum.IsDefined(typeof(FiniteActionState), state)
                    || Path.GetFileName(path) != FileName(id) || records.ContainsKey(id.Key))
                    throw new FormatException("Invalid finite-action record identity or state.");
                records.Add(id.Key, new FiniteActionRecord { Identity = id,
                    State = state == FiniteActionState.Invocation ? FiniteActionState.Unknown : state,
                    Before = root["before"].Value, After = root["after"].Value, Disposition = root["disposition"].Value,
                    Epoch = root["epoch"].Value, Domains = root["domains"].AsInt });
            }
        }

        internal FiniteActionRecord Find(FiniteActionIdentity id) => records.TryGetValue(id.Key, out var value) ? value : null;
        internal FiniteActionRecord[] Records => records.Values.ToArray();
        private static bool PersistentEffectKind(string kind) => kind.StartsWith("exp-purchase:", StringComparison.Ordinal)
            || kind.StartsWith("digger-purchase:", StringComparison.Ordinal) || kind.StartsWith("item-", StringComparison.Ordinal);
        internal bool HasUnresolved(FiniteActionIdentity id) => records.Values.Any(x => x.Identity.Kind == id.Kind
            && (PersistentEffectKind(id.Kind) || x.Identity.Acquisition == id.Acquisition && x.Identity.Entry == id.Entry)
            && (x.State == FiniteActionState.Invocation || x.State == FiniteActionState.Unknown
                || x.State == FiniteActionState.Committed && x.Dirty));

        private static bool DependsOn(FiniteActionRecord record, FiniteActionIdentity id, string epoch, int domains)
        {
            var same = record.Identity.Kind == id.Kind;
            var footprint = record.Domains == 0 ? same || id.Kind == "rebirth" : (record.Domains & domains) != 0;
            return footprint && (record.State == FiniteActionState.Committed && record.Dirty
                || record.Epoch == epoch || string.IsNullOrEmpty(record.Epoch) && same
                || same && (id.Kind == "rebirth" || PersistentEffectKind(id.Kind)));
        }

        internal bool FlushDependentResults(FiniteActionIdentity id, string epoch, int domains, int budget = 8)
        {
            foreach (var record in records.Values.Where(x => x.State == FiniteActionState.Committed && x.Dirty
                && DependsOn(x, id, epoch, domains)).ToArray())
            {
                if (budget-- <= 0) return false;
                try { Save(record); }
                catch { return false; }
            }
            return true;
        }

        internal bool HasUnresolvedDependencies(FiniteActionIdentity id, string epoch, int domains) => records.Values.Any(x =>
            (x.State == FiniteActionState.Invocation || x.State == FiniteActionState.Unknown || x.State == FiniteActionState.Committed && x.Dirty)
            && DependsOn(x, id, epoch, domains));

        internal void Save(FiniteActionRecord record)
        {
            // Retain the in-memory state even if persistence fails after native commitment.
            records[record.Identity.Key] = record;
            record.Dirty = true;
            var id = record.Identity;
            var root = new JSONObject(); root["version"] = 1;
            root["kind"] = id.Kind; root["owner"] = id.Owner; root["acquisition"] = id.Acquisition;
            root["entry"] = id.Entry; root["opportunity"] = id.Opportunity; root["state"] = record.State.ToString();
            root["before"] = record.Before ?? ""; root["after"] = record.After ?? "";
            root["disposition"] = record.Disposition ?? "";
            root["epoch"] = record.Epoch ?? ""; root["domains"] = record.Domains;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileName(id));
            // Do not append a GUID to the 64-character record name: a valid
            // runtime path can otherwise exceed desktop CLR's MAX_PATH here.
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes(root.ToString());
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                record.Dirty = false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { /* A cleanup failure must not replace invocation/result evidence. */ }
            }
        }

        private static string FileName(FiniteActionIdentity id) => JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes(id.Key)) + ".json";
    }
}
