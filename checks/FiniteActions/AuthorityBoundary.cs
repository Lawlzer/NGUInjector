using System;

// Source-unit boundary ONLY. This does not validate native Main.Execution authority,
// setup claims, resource reservations, or game-thread admission.
namespace NGUInjector.Managers
{
    internal enum MutationDisposition { RejectedUnchanged, AppliedVerified, ModelAppliedRenderFailed, KnownPartial, ForeignConflict, UnknownOutcome }
}
namespace NGUInjector
{
    internal partial class Main
    {
        [Flags]
        internal enum ExecutionDomain { None = 0, Energy = 1, Magic = 2, Res3 = 4, Gear = 8, Support = 16, NativeSettings = 32, Route = 64, FiniteAction = 128, Inventory = 256 }
        internal sealed class ExecutionGrant
        {
            internal string Owner, Acquisition;
            internal bool Current = true;
            internal ExecutionDomain Domains;
            internal Func<bool> Check;
        }
        internal static readonly CharacterBoundary Character = new CharacterBoundary();
        internal sealed class CharacterBoundary { internal readonly StatsBoundary stats = new StatsBoundary(); }
        internal sealed class StatsBoundary { internal long rebirthNumber = 1; }
        internal Func<bool, bool> CheckBoundary = after => true;
        internal Action CaptureBoundary, OnInvalidate;
        internal int Invalidations;
        internal ExecutionDomain InvalidatedDomains;
        internal bool ValidateExecutionGrant(ExecutionGrant grant, ExecutionDomain footprint)
        {
            try { return grant != null && grant.Current && (grant.Domains & footprint) == footprint && (grant.Check == null || grant.Check()); }
            catch { return false; }
        }
        internal bool VerifyExecutionPrerequisites(ExecutionGrant grant, ExecutionDomain footprint, Func<bool> observe)
        {
            if (!ValidateExecutionGrant(grant, footprint) || observe == null) return false;
            try { return observe() && ValidateExecutionGrant(grant, footprint); }
            catch { return false; }
        }
        internal void InvalidateExecutionObservations(ExecutionDomain domains)
        {
            Invalidations++; InvalidatedDomains |= domains; OnInvalidate?.Invoke();
        }
        private Func<bool, bool> CaptureFiniteActionBoundary(FiniteAction action)
        {
            CaptureBoundary?.Invoke();
            return CheckBoundary;
        }
    }
}
