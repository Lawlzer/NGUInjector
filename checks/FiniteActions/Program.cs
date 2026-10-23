using System;
using System.IO;
using System.Text;
using NGUInjector;
using NGUInjector.Managers;

internal static class Program
{
    private static int checks;
    private sealed class Effect : Main.FiniteAction
    {
        internal int Writes;
        internal bool Ready = true, RenderThrows;
        internal Action BeforeEffect, AfterEffect;
        internal Action<int> Preflight;
        private int preflights;
        internal override bool Prerequisites() { Preflight?.Invoke(++preflights); return Ready; }
        internal override string Observe() => Writes.ToString();
        internal override void Invoke()
        {
            BeforeEffect?.Invoke(); Writes++; AfterEffect?.Invoke();
            if (RenderThrows) throw new InvalidOperationException("render failed");
        }
        internal override MutationDisposition Classify(string before, string after, Exception error) => int.Parse(after) == int.Parse(before) + 1
            ? error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed : MutationDisposition.UnknownOutcome;
    }
    private static void Equal<T>(T expected, T actual, string message)
    {
        checks++;
        if (!Equals(expected, actual)) throw new InvalidOperationException(message + ": expected " + expected + ", actual " + actual);
    }
    private static Effect Leaf(string opportunity = "1", string kind = "feed") => new Effect {
        Identity = new FiniteActionIdentity(kind, "owner", "acquisition", "entry", opportunity),
        Footprint = NGUInjector.Main.ExecutionDomain.FiniteAction | NGUInjector.Main.ExecutionDomain.Inventory };
    private static Main.ExecutionGrant Grant(Effect action) => new Main.ExecutionGrant {
        Owner = action.Identity.Owner, Acquisition = action.Identity.Acquisition, Domains = action.Footprint };
    private static string RecordPath(string directory, Effect effect) => Path.Combine(directory, "finite-actions-v1",
        JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes(effect.Identity.Key)) + ".json");
    private static string CaseDirectory(string root, string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }

    private static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "ngu-finite-action-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = CaseDirectory(root, "render"); var runtime = new NGUInjector.Main(path); var effect = Leaf(); effect.RenderThrows = true;
            effect.BeforeEffect = () => Equal("Invocation", JsonFiles.ParseObject(File.ReadAllText(RecordPath(path, effect)))["state"].Value, "invocation is durable before effect");
            var result = runtime.RunFiniteAction(effect, Grant(effect));
            Equal(MutationDisposition.ModelAppliedRenderFailed, result.Disposition, "render failure classifies committed model");
            Equal(true, result.Verified, "committed rendering failure is durable and verified");
            runtime.RunFiniteAction(effect, Grant(effect)); Equal(1, effect.Writes, "same action is not repeated after render failure");
            new NGUInjector.Main(path).RunFiniteAction(effect, Grant(effect)); Equal(1, effect.Writes, "committed action survives reload");

            path = CaseDirectory(root, "prerequisites"); runtime = new NGUInjector.Main(path); effect = Leaf(); effect.Ready = false;
            result = runtime.RunFiniteAction(effect, Grant(effect)); Equal(0, effect.Writes, "unverified setup withholds invocation");
            Equal(MutationDisposition.RejectedUnchanged, result.Disposition, "unverified setup is unchanged");
            effect.Ready = true; var revoked = Grant(effect); revoked.Current = false;
            runtime.RunFiniteAction(effect, revoked); Equal(0, effect.Writes, "revoked grant withholds invocation");

            path = CaseDirectory(root, "storage-unavailable"); File.WriteAllText(Path.Combine(path, "finite-actions-v1"), "not a directory");
            runtime = new NGUInjector.Main(path); effect = Leaf(); result = runtime.RunFiniteAction(effect, Grant(effect));
            Equal(0, effect.Writes, "prepared storage failure withholds effect"); Equal(false, result.Durable, "failed admission is not durable");

            path = CaseDirectory(root, "invocation-failure"); runtime = new NGUInjector.Main(path); effect = Leaf(); FileStream invocationHeld = null;
            effect.Preflight = count => { if (count == 3) invocationHeld = new FileStream(RecordPath(path, effect), FileMode.Open, FileAccess.Read, FileShare.Read); };
            result = runtime.RunFiniteAction(effect, Grant(effect));
            Equal(0, effect.Writes, "invocation persistence failure withholds native call");
            Equal(false, result.Durable, "failed invocation persistence cannot publish success");
            invocationHeld.Dispose(); effect.Preflight = null;
            result = runtime.RunFiniteAction(effect, Grant(effect));
            Equal(MutationDisposition.UnknownOutcome, result.Disposition, "ambiguous invocation write remains unresolved in-session");
            Equal(0, effect.Writes, "unresolved failed invocation cannot retry blindly");

            path = CaseDirectory(root, "result-failure"); runtime = new NGUInjector.Main(path); effect = Leaf(); FileStream held = null;
            effect.AfterEffect = () => held = new FileStream(RecordPath(path, effect), FileMode.Open, FileAccess.Read, FileShare.Read);
            result = runtime.RunFiniteAction(effect, Grant(effect));
            Equal(true, result.Committed, "result persistence failure retains model commitment");
            Equal(false, result.Durable, "result persistence failure does not publish durable success");
            runtime.RunFiniteAction(effect, Grant(effect)); Equal(1, effect.Writes, "dirty committed result retries storage, not effect");
            var dependent = Leaf("2", "successor"); runtime.RunFiniteAction(dependent, Grant(dependent));
            Equal(0, dependent.Writes, "dirty committed result withholds dependent irreversible effect");
            var independent = Leaf("2", "healthy-energy"); independent.Footprint = NGUInjector.Main.ExecutionDomain.FiniteAction | NGUInjector.Main.ExecutionDomain.Energy;
            runtime.RunFiniteAction(independent, Grant(independent)); Equal(1, independent.Writes, "dirty inventory result leaves independent Energy action healthy");
            held.Dispose(); effect.AfterEffect = null;
            var imported = new NGUInjector.Main(path); result = imported.RunFiniteAction(effect, Grant(effect));
            Equal(MutationDisposition.UnknownOutcome, result.Disposition, "imported invocation is unknown, never uninvoked");
            Equal(1, effect.Writes, "imported invocation cannot repeat");
            var later = Leaf("2"); result = imported.RunFiniteAction(later, Grant(later)); Equal(0, later.Writes, "unresolved action scope blocks replacement opportunity");
            var otherOwner = Leaf("3"); otherOwner.Identity = new FiniteActionIdentity("feed", "replacement-owner", "acquisition", "entry", "3");
            imported.RunFiniteAction(otherOwner, Grant(otherOwner)); Equal(0, otherOwner.Writes, "changing owner cannot evade unresolved native-effect scope");
            var changedAcquisition = Leaf("4"); changedAcquisition.Identity = new FiniteActionIdentity("feed", "replacement-owner", "new-guid", "new-process", "4");
            imported.RunFiniteAction(changedAcquisition, Grant(changedAcquisition)); Equal(0, changedAcquisition.Writes, "new acquisition cannot bypass same-epoch unresolved native footprint");
            var healthy = Leaf("1", "independent"); healthy.Footprint = NGUInjector.Main.ExecutionDomain.FiniteAction | NGUInjector.Main.ExecutionDomain.Energy;
            result = imported.RunFiniteAction(healthy, Grant(healthy));
            Equal(1, healthy.Writes, "unrelated healthy action continues"); Equal(true, result.Verified, "healthy independent result verified");

            path = CaseDirectory(root, "result-recovery"); runtime = new NGUInjector.Main(path); effect = Leaf(); held = null;
            effect.AfterEffect = () => held = new FileStream(RecordPath(path, effect), FileMode.Open, FileAccess.Read, FileShare.Read);
            runtime.RunFiniteAction(effect, Grant(effect)); held.Dispose(); effect.AfterEffect = null;
            dependent = Leaf("2", "successor"); result = runtime.RunFiniteAction(dependent, Grant(dependent));
            Equal(1, effect.Writes, "storage recovery never re-invokes committed predecessor");
            Equal(1, dependent.Writes, "storage recovery admits current dependent effect");
            Equal(true, result.Verified, "dependent effect obtains its own fresh authority");
            Equal("Committed", JsonFiles.ParseObject(File.ReadAllText(RecordPath(path, effect)))["state"].Value, "bounded flush persists known predecessor result");
            var history = runtime.RunFiniteAction(effect, Grant(effect));
            Equal(false, history.Verified, "result flush does not manufacture fresh predecessor authority");
            Equal(true, history.PreviouslyCommitted, "completion history is explicitly distinct from a fresh callback result");

            path = CaseDirectory(root, "prepared-import"); var ledger = new FiniteActionLedger(Path.Combine(path, "finite-actions-v1")); effect = Leaf();
            ledger.Save(new FiniteActionRecord { Identity = effect.Identity, State = FiniteActionState.Prepared, Before = "0" });
            runtime = new NGUInjector.Main(path); runtime.RunFiniteAction(effect, Grant(effect)); Equal(1, effect.Writes, "prepared-only evidence permits first invocation");

            path = CaseDirectory(root, "unknown-import"); ledger = new FiniteActionLedger(Path.Combine(path, "finite-actions-v1")); effect = Leaf();
            ledger.Save(new FiniteActionRecord { Identity = effect.Identity, State = FiniteActionState.Unknown, Before = "0" });
            runtime = new NGUInjector.Main(path); runtime.RunFiniteAction(effect, Grant(effect)); Equal(0, effect.Writes, "imported unknown never becomes prepared");

            path = CaseDirectory(root, "post-authority"); runtime = new NGUInjector.Main(path); effect = Leaf(); var lost = Grant(effect);
            effect.AfterEffect = () => lost.Current = false;
            result = runtime.RunFiniteAction(effect, lost);
            Equal(true, result.Committed, "post-callback authority loss does not forget proven model commitment");
            Equal(false, result.Verified, "post-callback authority loss withholds dependent credit");
            Equal(false, result.PreviouslyCommitted, "fresh callback conflict cannot masquerade as recovered completion history");
            runtime.RunFiniteAction(effect, Grant(effect)); Equal(1, effect.Writes, "post-authority conflict cannot replay commitment");
            foreach (var kind in new[] { "exp-purchase:0", "digger-purchase:0", "item-Merge:40" })
            {
                path = CaseDirectory(root, "persistent-" + kind.Replace(':', '-'));
                ledger = new FiniteActionLedger(Path.Combine(path, "finite-actions-v1"));
                var old = new FiniteActionIdentity(kind, "owner", "rebirth:1", "rebirth:1", "original-opportunity");
                ledger.Save(new FiniteActionRecord { Identity = old, State = FiniteActionState.Unknown, Epoch = "rebirth:1",
                    Domains = (int)NGUInjector.Main.ExecutionDomain.Energy, Before = "unknown persistent input" });
                NGUInjector.Main.Character.stats.rebirthNumber = 2;
                runtime = new NGUInjector.Main(path); effect = Leaf("replacement-opportunity", kind);
                effect.Identity = new FiniteActionIdentity(kind, "replacement-owner", "new-guid", "new-entry", "replacement-opportunity");
                effect.Footprint = NGUInjector.Main.ExecutionDomain.FiniteAction | NGUInjector.Main.ExecutionDomain.Energy;
                result = runtime.RunFiniteAction(effect, Grant(effect));
                Equal(0, effect.Writes, "rebirth/new process cannot discard unresolved persistent effect: " + kind);
                Equal(MutationDisposition.UnknownOutcome, result.Disposition, "persistent uncertainty remains explicitly unknown: " + kind);
                independent = Leaf("independent-opportunity", "healthy-magic");
                independent.Footprint = NGUInjector.Main.ExecutionDomain.FiniteAction | NGUInjector.Main.ExecutionDomain.Magic;
                Equal(true, runtime.RunFiniteAction(independent, Grant(independent)).Verified, "persistent uncertainty leaves independent Magic healthy: " + kind);
                NGUInjector.Main.Character.stats.rebirthNumber = 1;
            }
            checks += PreparationCommitmentChecks.Run(root);
            checks += DependencyFlushChecks.Run(root);
            checks += PreparationHandoffChecks.Run(root);
            Console.WriteLine("Finite-action source-unit checks PASS " + checks + ". Native setup/grant acceptance is NOT exercised.");
        }
        finally { Directory.Delete(root, true); }
    }
}
