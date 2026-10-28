using System;
using NGUInjector;
using NGUInjector.Managers;
using Runtime = NGUInjector.Main;

internal static class Program
{
    private static int assertions;

    private sealed class Effect : Runtime.FiniteAction
    {
        internal int Writes, Invocations, Observations, Preflights;
        internal bool Ready = true;
        internal Action BeforeEffect, AfterEffect, BeforeClassify;
        internal Action<int> Preflight, OnObserve;
        internal MutationDisposition? Classification;
        internal string ClassifiedBefore, ClassifiedAfter;
        internal Exception ClassifiedError;

        internal override bool Prerequisites() { Preflights++; Preflight?.Invoke(Preflights); return Ready; }
        internal override string Observe() { Observations++; OnObserve?.Invoke(Observations); return Writes.ToString(); }
        internal override void Invoke()
        {
            Invocations++; BeforeEffect?.Invoke(); Writes++; AfterEffect?.Invoke();
        }
        internal override MutationDisposition Classify(string before, string after, Exception error)
        {
            ClassifiedBefore = before; ClassifiedAfter = after; ClassifiedError = error;
            BeforeClassify?.Invoke();
            return Classification ?? (int.Parse(after) == int.Parse(before) + 1
                ? error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed
                : before == after && error == null ? MutationDisposition.RejectedUnchanged : MutationDisposition.UnknownOutcome);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!Equals(expected, actual)) throw new InvalidOperationException(message + ": expected " + expected + ", actual " + actual);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        assertions++;
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static Effect Leaf(string opportunity = "1", string kind = "feed") => new Effect {
        Identity = new FiniteActionIdentity(kind, "owner", "acquisition", "entry", opportunity),
        Footprint = Runtime.ExecutionDomain.FiniteAction | Runtime.ExecutionDomain.Inventory };
    private static Runtime.ExecutionGrant Grant(Effect effect) => new Runtime.ExecutionGrant {
        Owner = effect.Identity.Owner, Acquisition = effect.Identity.Acquisition, Domains = effect.Footprint };
    private static void Rejected(Runtime.FiniteActionResult result, Effect effect, Runtime runtime)
    {
        Equal(false, result.Invoked, "preflight rejection is not an invocation");
        Equal(0, effect.Invocations, "preflight rejection withholds the leaf");
        Equal(false, result.Committed, "preflight rejection grants no commitment");
        Equal(false, result.AuthorityCurrent, "preflight rejection grants no result authority");
        Equal(false, result.Verified, "preflight rejection grants no credit");
        Equal(MutationDisposition.RejectedUnchanged, result.Disposition, "preflight rejection stays unchanged");
        Equal(0, runtime.Invalidations, "uninvoked preflight does not invalidate observations");
        Equal(false, string.IsNullOrEmpty(result.Reason), "preflight rejection explains why");
    }

    private static void IncompleteIdentityIsRejected()
    {
        for (var field = 0; field < 5; field++)
        {
            var parts = new[] { "kind", "owner", "acquisition", "entry", "opportunity" };
            parts[field] = "";
            Throws<ArgumentException>(() => new FiniteActionIdentity(parts[0], parts[1], parts[2], parts[3], parts[4]));
            parts[field] = null;
            Throws<ArgumentException>(() => new FiniteActionIdentity(parts[0], parts[1], parts[2], parts[3], parts[4]));
        }
    }

    private static void SemanticIdentityRemainsUnambiguous()
    {
        var first = new FiniteActionIdentity("a:b", "c", "acquisition", "entry", "opportunity");
        var same = new FiniteActionIdentity("a:b", "c", "acquisition", "entry", "opportunity");
        var other = new FiniteActionIdentity("a", "b:c", "acquisition", "entry", "opportunity");
        Equal(first.Key, same.Key, "same semantic fields retain the same key");
        Equal(false, first.Key == other.Key, "punctuation cannot alias semantic identities");
        Equal(first.Scope, new FiniteActionIdentity("a:b", "c", "acquisition", "entry", "other").Scope, "opportunity is not part of owner scope");
        var runtime = new Runtime();
        var id = runtime.ActionIdentity("feed", "owner", "now");
        Equal("rebirth:1", id.Acquisition, "current rebirth still supplies acquisition identity");
        Equal("rebirth:1", id.Entry, "default entry retains semantic rebirth scope");
        Equal("explicit-entry", runtime.ActionIdentity("feed", "owner", "now", "explicit-entry").Entry, "explicit entry remains available");
    }

    private static void InvalidLeafIsRejected()
    {
        var runtime = new Runtime(); var effect = Leaf();
        Throws<ArgumentException>(() => runtime.RunFiniteAction(null, Grant(effect)));
        effect.Identity = null;
        Throws<ArgumentException>(() => runtime.RunFiniteAction(effect, null));
        effect = Leaf(); effect.Footprint = Runtime.ExecutionDomain.Inventory;
        Throws<ArgumentException>(() => runtime.RunFiniteAction(effect, Grant(effect)));
        Equal(0, effect.Invocations, "malformed leaves never invoke");
        Equal(0, runtime.Invalidations, "malformed leaves have no native result");
    }

    private static void GrantIsRequired(string invalid)
    {
        var runtime = new Runtime(); var effect = Leaf(); var grant = Grant(effect);
        switch (invalid)
        {
            case "missing": grant = null; break;
            case "owner": grant.Owner = "other"; break;
            case "acquisition": grant.Acquisition = "other"; break;
            case "footprint": grant.Domains = Runtime.ExecutionDomain.FiniteAction; break;
            case "expired": grant.Current = false; break;
            case "throws": grant.Check = () => throw new InvalidOperationException("grant failed"); break;
            default: throw new ArgumentException(invalid);
        }
        Rejected(runtime.RunFiniteAction(effect, grant), effect, runtime);
        Equal(0, effect.Observations, "invalid grant cannot reach before-observation");
    }

    private static void PrerequisitesCanRetry()
    {
        var runtime = new Runtime(); var effect = Leaf(); effect.Ready = false;
        Rejected(runtime.RunFiniteAction(effect, Grant(effect)), effect, runtime);
        effect.Ready = true;
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "fresh prerequisites admit the next call");
        Equal(1, effect.Invocations, "next call invokes only once");
    }

    private static void PrerequisiteExceptionsReject()
    {
        var runtime = new Runtime(); var effect = Leaf();
        effect.Preflight = _ => throw new InvalidOperationException("prerequisite failed");
        Rejected(runtime.RunFiniteAction(effect, Grant(effect)), effect, runtime);
        effect.Preflight = null;
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "preflight exception leaves no retry latch");
    }

    private static void CaptureFailureCanRetry()
    {
        var runtime = new Runtime(); var effect = Leaf(); var error = new InvalidOperationException("capture failed");
        runtime.CaptureBoundary = () => throw error;
        var result = runtime.RunFiniteAction(effect, Grant(effect));
        Rejected(result, effect, runtime);
        Equal(error, result.Error, "capture error is retained without claiming invocation");
        Equal(0, effect.Observations, "native boundary is captured before before-observation");
        runtime.CaptureBoundary = null;
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "capture failure leaves no retry latch");
    }

    private static void BoundaryMustBeCurrent(bool throws)
    {
        var runtime = new Runtime(); var effect = Leaf();
        runtime.CheckBoundary = _ => throws ? throw new InvalidOperationException("boundary failed") : false;
        Rejected(runtime.RunFiniteAction(effect, Grant(effect)), effect, runtime);
        Equal(0, effect.Observations, "invalid native boundary withholds before-observation");
        runtime.CheckBoundary = _ => true;
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "fresh native boundary can retry");
    }

    private static void BeforeObservationFailureCanRetry()
    {
        var runtime = new Runtime(); var effect = Leaf(); var error = new InvalidOperationException("observe failed");
        effect.OnObserve = _ => throw error;
        var result = runtime.RunFiniteAction(effect, Grant(effect));
        Rejected(result, effect, runtime);
        Equal(error, result.Error, "before-observation error is retained");
        effect.OnObserve = null;
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "observation exception leaves no retry latch");
    }

    private static void ObservationIsRechecked()
    {
        var runtime = new Runtime(); var effect = Leaf();
        effect.OnObserve = count => { if (count == 2) effect.Writes++; };
        Rejected(runtime.RunFiniteAction(effect, Grant(effect)), effect, runtime);
        Equal(1, effect.Writes, "foreign preflight change is not overwritten");
        effect.OnObserve = null;
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "next call replans its before-observation");
    }

    private static void PrerequisitesAreRechecked()
    {
        var runtime = new Runtime(); var effect = Leaf();
        effect.Preflight = count => { if (count == 2) effect.Ready = false; };
        Rejected(runtime.RunFiniteAction(effect, Grant(effect)), effect, runtime);
        Equal(2, effect.Preflights, "prerequisites are checked after before-observation");
    }

    private static void BoundaryChangesDuringObservationReject()
    {
        var runtime = new Runtime(); var effect = Leaf(); var current = true;
        runtime.CheckBoundary = _ => current;
        effect.OnObserve = _ => current = false;
        Rejected(runtime.RunFiniteAction(effect, Grant(effect)), effect, runtime);
    }

    private static void GrantChangesDuringObservationReject()
    {
        var runtime = new Runtime(); var effect = Leaf(); var grant = Grant(effect);
        effect.OnObserve = _ => grant.Current = false;
        Rejected(runtime.RunFiniteAction(effect, grant), effect, runtime);
    }

    private static void FinalBoundaryIsRechecked()
    {
        var runtime = new Runtime(); var effect = Leaf(); var checks = 0;
        runtime.CheckBoundary = _ => ++checks < 3;
        Rejected(runtime.RunFiniteAction(effect, Grant(effect)), effect, runtime);
        Equal(2, effect.Observations, "final boundary rejection follows stable before-observations");
    }

    private static void FinalGrantCheckFollowsNativeBoundary()
    {
        var runtime = new Runtime(); var effect = Leaf(); var grant = Grant(effect); var checks = 0;
        runtime.CheckBoundary = _ => { if (++checks == 3) grant.Current = false; return true; };
        Rejected(runtime.RunFiniteAction(effect, grant), effect, runtime);
        Equal(3, checks, "last native boundary runs before last grant check");
    }

    private static void SuccessUsesFreshEvidence()
    {
        var runtime = new Runtime(); var effect = Leaf();
        var result = runtime.RunFiniteAction(effect, Grant(effect));
        Equal(true, result.Invoked, "success reports the admitted invocation");
        Equal(1, effect.Invocations, "one call admits exactly one leaf");
        Equal(1, effect.Writes, "one call produces one effect");
        Equal("0", effect.ClassifiedBefore, "classifier receives fresh before evidence");
        Equal("1", effect.ClassifiedAfter, "classifier receives fresh after evidence");
        Equal(null, effect.ClassifiedError, "successful callback has no fabricated error");
        Equal(MutationDisposition.AppliedVerified, result.Disposition, "fresh effect classifies applied");
        Equal(true, result.Committed, "fresh effect commits");
        Equal(true, result.AuthorityCurrent, "fresh effect retains result authority");
        Equal(true, result.Verified, "fresh commitment with authority is verified");
        Equal(1, runtime.Invalidations, "invocation invalidates observations exactly once");
        Equal(Runtime.ExecutionDomain.Inventory, runtime.InvalidatedDomains, "only native footprint domains are invalidated");
    }

    private static void SuccessDoesNotDeduplicateFutureCalls()
    {
        var runtime = new Runtime(); var effect = Leaf();
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "first current effect verifies");
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "same semantic identity can recur from fresh state");
        Equal(2, effect.Writes, "successful history does not suppress current native recurrence");
        Equal("1", effect.ClassifiedBefore, "second call observes current state, not prior before evidence");
        Equal(2, runtime.Invalidations, "each invocation invalidates its own observations");
    }

    private static void RenderFailureCanCommit()
    {
        var runtime = new Runtime(); var effect = Leaf(); var error = new InvalidOperationException("render failed");
        effect.AfterEffect = () => throw error;
        var result = runtime.RunFiniteAction(effect, Grant(effect));
        Equal(true, result.Invoked, "render failure occurred inside the admitted leaf");
        Equal(MutationDisposition.ModelAppliedRenderFailed, result.Disposition, "model evidence survives render failure");
        Equal(true, result.Verified, "proven model effect retains current authority");
        Equal(error, result.Error, "callback error is retained");
        Equal(error, effect.ClassifiedError, "classifier receives callback error");
        Equal("render failed", result.Reason, "callback error explains result");
        Equal(1, effect.Invocations, "render failure never invokes a second leaf");
        Equal(1, runtime.Invalidations, "render failure invalidates observations");
    }

    private static void InvocationFailureCanRetry()
    {
        var runtime = new Runtime(); var effect = Leaf(); var error = new InvalidOperationException("leaf failed");
        effect.BeforeEffect = () => throw error;
        var result = runtime.RunFiniteAction(effect, Grant(effect));
        Equal(true, result.Invoked, "callback throwing before model writes still counts as invoked");
        Equal(1, effect.Invocations, "failing leaf was entered exactly once");
        Equal(0, effect.Writes, "callback failed before model change");
        Equal(MutationDisposition.UnknownOutcome, result.Disposition, "failed native callback keeps ambiguous evidence");
        Equal(false, result.Committed, "ambiguous callback has no proven commitment");
        Equal(false, result.Verified, "ambiguous callback withholds caller continuation");
        Equal(error, result.Error, "original callback error is retained");
        Equal(1, runtime.Invalidations, "ambiguous invocation invalidates observations");
        effect.BeforeEffect = null;
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "fresh next call is not banned by callback uncertainty");
        Equal(2, effect.Invocations, "next call admits one new invocation");
    }

    private static void UnverifiedResultDoesNotLatch(MutationDisposition disposition)
    {
        var runtime = new Runtime(); var effect = Leaf(); effect.Classification = disposition;
        var result = runtime.RunFiniteAction(effect, Grant(effect));
        Equal(true, result.Invoked, "unverified disposition follows an invocation");
        Equal(disposition, result.Disposition, "low-level disposition is preserved");
        Equal(false, result.Committed, "unverified disposition is not a proven commitment");
        Equal(false, result.Verified, "unverified result stops its caller's batch");
        Equal(1, runtime.Invalidations, "unverified invocation invalidates observations");
        effect.Classification = null;
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "fresh state permits retry of the same identity");
        Equal(2, effect.Writes, "runner has no per-action outcome latch");
        var sameKind = Leaf("next");
        Equal(true, runtime.RunFiniteAction(sameKind, Grant(sameKind)).Verified, "same action type has no session ban");
        var sameDomain = Leaf("next", "other-kind");
        Equal(true, runtime.RunFiniteAction(sameDomain, Grant(sameDomain)).Verified, "shared native domains have no UNKNOWN dependency latch");
    }

    private static void AfterObservationFailureCanRetry()
    {
        var runtime = new Runtime(); var effect = Leaf(); var error = new InvalidOperationException("after failed");
        effect.OnObserve = _ => { if (effect.Invocations > 0) throw error; };
        var result = runtime.RunFiniteAction(effect, Grant(effect));
        Equal(true, result.Invoked, "failed after-observation does not erase invocation");
        Equal(1, effect.Writes, "effect happened before failed observation");
        Equal(MutationDisposition.UnknownOutcome, result.Disposition, "failed after-observation is ambiguous");
        Equal(false, result.Verified, "missing after evidence withholds caller continuation");
        Equal(error, result.Error, "after-observation failure is retained");
        Equal(1, runtime.Invalidations, "failed after-observation invalidates observations");
        effect.OnObserve = null;
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "fresh observation can retry after ambiguity");
    }

    private static void ClassificationFailureCanRetry()
    {
        var runtime = new Runtime(); var effect = Leaf(); var error = new InvalidOperationException("classify failed");
        effect.BeforeClassify = () => throw error;
        var result = runtime.RunFiniteAction(effect, Grant(effect));
        Equal(MutationDisposition.UnknownOutcome, result.Disposition, "classification failure stays ambiguous");
        Equal(true, result.Invoked, "classification failure does not erase invocation");
        Equal(false, result.Verified, "classification failure withholds caller continuation");
        Equal(error, result.Error, "classification error is retained");
        Equal(1, runtime.Invalidations, "classification failure invalidates observations");
        effect.BeforeClassify = null;
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "classification failure leaves no permanent UNKNOWN latch");
    }

    private static void InvocationErrorTakesPrecedence()
    {
        var runtime = new Runtime(); var effect = Leaf(); var error = new InvalidOperationException("original leaf error");
        effect.AfterEffect = () => throw error;
        effect.BeforeClassify = () => throw new InvalidOperationException("secondary classifier error");
        var result = runtime.RunFiniteAction(effect, Grant(effect));
        Equal(error, result.Error, "classification failure cannot hide original callback error");
        Equal("original leaf error", result.Reason, "reason retains original callback failure");
        Equal(MutationDisposition.UnknownOutcome, result.Disposition, "failed classification cannot claim commitment");
    }

    private static void AuthorityLossWithholdsCredit(string phase)
    {
        var runtime = new Runtime(); var effect = Leaf(); var grant = Grant(effect);
        switch (phase)
        {
            case "invoke": effect.AfterEffect = () => grant.Current = false; break;
            case "observe": effect.OnObserve = _ => { if (effect.Invocations > 0) grant.Current = false; }; break;
            case "classify": effect.BeforeClassify = () => grant.Current = false; break;
            case "boundary": runtime.CheckBoundary = after => { if (after) grant.Current = false; return true; }; break;
            default: throw new ArgumentException(phase);
        }
        var result = runtime.RunFiniteAction(effect, grant);
        Equal(true, result.Invoked, "authority loss follows one admitted callback");
        Equal(true, result.Committed, "authority loss does not erase proven native commitment");
        Equal(false, result.AuthorityCurrent, "post-invocation authority is revalidated");
        Equal(false, result.Verified, "lost authority withholds dependent credit");
        Equal(1, runtime.Invalidations, "authority loss invalidates observations");
        Equal(true, runtime.RunFiniteAction(effect, Grant(effect)).Verified, "fresh authority can recur after a conflicted result");
        Equal(2, effect.Invocations, "no historical commitment latch replaces current authority");
    }

    private static void NativeBoundaryLossWithholdsCredit(string phase)
    {
        var runtime = new Runtime(); var effect = Leaf(); var current = true; var afterChecks = 0;
        runtime.CheckBoundary = after => phase == "transient" && after ? ++afterChecks > 1 : current;
        if (phase == "invoke") effect.AfterEffect = () => current = false;
        else if (phase == "classify") effect.BeforeClassify = () => current = false;
        var result = runtime.RunFiniteAction(effect, Grant(effect));
        Equal(true, result.Committed, "foreign native boundary does not erase proven leaf commitment");
        Equal(false, result.AuthorityCurrent, "foreign boundary withholds result authority");
        Equal(false, result.Verified, "foreign boundary cannot credit dependent work");
        Equal(1, runtime.Invalidations, "foreign boundary invalidates observations");
        if (phase == "transient") Equal(2, afterChecks, "later boundary recovery does not erase earlier conflict");
    }

    private static void OwnInvalidationFollowsResultValidation()
    {
        var runtime = new Runtime(); var effect = Leaf(); var grant = Grant(effect);
        runtime.OnInvalidate = () => grant.Current = false;
        var result = runtime.RunFiniteAction(effect, grant);
        Equal(false, grant.Current, "own invalidation expires old observations");
        Equal(true, result.Verified, "result is validated before its own observation invalidation");
    }

    private static void ReentryIsRejected(string phase)
    {
        var runtime = new Runtime(); var effect = Leaf(); var child = Leaf("nested", "other-kind"); var grant = Grant(effect);
        Runtime.FiniteActionResult nested = null;
        void Reenter() { if (nested == null) nested = runtime.RunFiniteAction(child, Grant(child)); }
        switch (phase)
        {
            case "grant": grant.Check = () => { Reenter(); return true; }; break;
            case "prerequisite": effect.Preflight = _ => Reenter(); break;
            case "capture": runtime.CaptureBoundary = Reenter; break;
            case "before-observe": effect.OnObserve = count => { if (count == 1) Reenter(); }; break;
            case "invoke": effect.BeforeEffect = Reenter; break;
            case "after-observe": effect.OnObserve = count => { if (count == 3) Reenter(); }; break;
            case "classify": effect.BeforeClassify = Reenter; break;
            case "after-boundary": runtime.CheckBoundary = after => { if (after) Reenter(); return true; }; break;
            case "invalidate": runtime.OnInvalidate = Reenter; break;
            default: throw new ArgumentException(phase);
        }
        Equal(true, runtime.RunFiniteAction(effect, grant).Verified, "outer call verifies during " + phase);
        Equal(false, nested.Invoked, "reentry cannot invoke during " + phase);
        Equal(false, nested.Verified, "reentry cannot acquire result credit during " + phase);
        Equal(MutationDisposition.RejectedUnchanged, nested.Disposition, "reentry remains uninvoked during " + phase);
        Equal(0, child.Invocations, "no nested leaf enters during " + phase);
        Equal(1, effect.Invocations, "outer call invokes once during " + phase);
        Equal(1, runtime.Invalidations, "reentry does not create another invalidation during " + phase);
        Equal(true, runtime.RunFiniteAction(child, Grant(child)).Verified, "guard releases after " + phase);
        Equal(1, child.Invocations, "next top-level call invokes once after " + phase);
    }

    private static void Main()
    {
        var cases = new Action[] {
            IncompleteIdentityIsRejected, SemanticIdentityRemainsUnambiguous, InvalidLeafIsRejected,
            () => GrantIsRequired("missing"), () => GrantIsRequired("owner"), () => GrantIsRequired("acquisition"),
            () => GrantIsRequired("footprint"), () => GrantIsRequired("expired"), () => GrantIsRequired("throws"),
            PrerequisitesCanRetry, PrerequisiteExceptionsReject, CaptureFailureCanRetry,
            () => BoundaryMustBeCurrent(false), () => BoundaryMustBeCurrent(true), BeforeObservationFailureCanRetry,
            ObservationIsRechecked, PrerequisitesAreRechecked, BoundaryChangesDuringObservationReject,
            GrantChangesDuringObservationReject, FinalBoundaryIsRechecked, FinalGrantCheckFollowsNativeBoundary,
            SuccessUsesFreshEvidence, SuccessDoesNotDeduplicateFutureCalls, RenderFailureCanCommit, InvocationFailureCanRetry,
            () => UnverifiedResultDoesNotLatch(MutationDisposition.RejectedUnchanged),
            () => UnverifiedResultDoesNotLatch(MutationDisposition.KnownPartial),
            () => UnverifiedResultDoesNotLatch(MutationDisposition.ForeignConflict),
            () => UnverifiedResultDoesNotLatch(MutationDisposition.UnknownOutcome),
            AfterObservationFailureCanRetry, ClassificationFailureCanRetry, InvocationErrorTakesPrecedence,
            () => AuthorityLossWithholdsCredit("invoke"), () => AuthorityLossWithholdsCredit("observe"),
            () => AuthorityLossWithholdsCredit("classify"), () => AuthorityLossWithholdsCredit("boundary"),
            () => NativeBoundaryLossWithholdsCredit("invoke"), () => NativeBoundaryLossWithholdsCredit("classify"),
            () => NativeBoundaryLossWithholdsCredit("transient"), OwnInvalidationFollowsResultValidation,
            () => ReentryIsRejected("grant"), () => ReentryIsRejected("prerequisite"), () => ReentryIsRejected("capture"),
            () => ReentryIsRejected("before-observe"), () => ReentryIsRejected("invoke"), () => ReentryIsRejected("after-observe"),
            () => ReentryIsRejected("classify"), () => ReentryIsRejected("after-boundary"), () => ReentryIsRejected("invalidate") };
        foreach (var test in cases) test();
        Console.WriteLine("Finite-action source-unit checks PASS " + cases.Length + " cases, " + assertions
            + " assertions. Native setup/grant acceptance is NOT exercised.");
    }
}
