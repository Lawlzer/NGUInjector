using System;
using System.Globalization;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal abstract class FiniteAction
        {
            internal FiniteActionIdentity Identity;
            internal ExecutionDomain Footprint;
            internal bool ChangesEquipmentMaterial;
            internal Func<object, string> ExpectedEquipmentMaterial;
            internal abstract bool Prerequisites();
            internal abstract string Observe();
            internal abstract void Invoke();
            internal abstract MutationDisposition Classify(string before, string after, Exception error);
        }

        internal sealed class FiniteActionResult
        {
            internal MutationDisposition Disposition;
            internal bool Invoked, Committed, AuthorityCurrent;
            internal string Reason;
            internal Exception Error;
            internal bool Verified => Committed && AuthorityCurrent;
        }

        private bool _finiteActionInvoking;
        private static bool FiniteActionBoundaryCurrent(Func<bool, bool> observe, bool after)
        {
            try { return observe(after); }
            catch { return false; }
        }

        internal FiniteActionResult RunFiniteAction(FiniteAction action, ExecutionGrant grant)
        {
            var result = new FiniteActionResult { Disposition = MutationDisposition.RejectedUnchanged };
            if (action?.Identity == null || (action.Footprint & ExecutionDomain.FiniteAction) == 0)
                throw new ArgumentException("A finite leaf requires identity and an explicit finite-action footprint.");
            if (grant == null || grant.Owner != action.Identity.Owner || grant.Acquisition != action.Identity.Acquisition)
            { result.Reason = "Action identity does not match the current grant."; return result; }
            if (_finiteActionInvoking) { result.Reason = "A finite action is already running."; return result; }
            _finiteActionInvoking = true;
            try
            {
                string before;
                Func<bool, bool> nativeBoundary;
                try
                {
                    if (!VerifyExecutionPrerequisites(grant, action.Footprint, action.Prerequisites))
                    { result.Reason = "Action setup or authority is not verified."; return result; }
                    nativeBoundary = CaptureFiniteActionBoundary(action);
                    if (!FiniteActionBoundaryCurrent(nativeBoundary, false))
                    { result.Reason = "Action native boundary is not current."; return result; }
                    before = action.Observe();
                    if (!VerifyExecutionPrerequisites(grant, action.Footprint, () => action.Prerequisites()
                        && action.Observe() == before && FiniteActionBoundaryCurrent(nativeBoundary, false)))
                    { result.Reason = "Action setup, observation, or authority changed during preflight."; return result; }
                    // Check the native boundary before the last grant check: neither
                    // an observer nor a native callback may run between that check and Invoke.
                    if (!FiniteActionBoundaryCurrent(nativeBoundary, false) || !ValidateExecutionGrant(grant, action.Footprint))
                    { result.Reason = "Action authority or native boundary changed before invocation."; return result; }
                }
                catch (Exception error)
                {
                    result.Error = error;
                    result.Reason = "Finite-action preflight failed: " + error.GetBaseException().Message;
                    return result;
                }

                try
                {
                    result.Invoked = true;
                    action.Invoke();
                }
                catch (Exception error) { result.Error = error; }
                result.AuthorityCurrent = FiniteActionBoundaryCurrent(nativeBoundary, true) && ValidateExecutionGrant(grant, action.Footprint);
                try
                {
                    var after = action.Observe();
                    result.Disposition = action.Classify(before, after, result.Error);
                }
                catch (Exception error)
                {
                    result.Error = result.Error ?? error;
                    result.Disposition = MutationDisposition.UnknownOutcome;
                }
                result.Committed = result.Disposition == MutationDisposition.AppliedVerified
                    || result.Disposition == MutationDisposition.ModelAppliedRenderFailed;
                result.AuthorityCurrent &= FiniteActionBoundaryCurrent(nativeBoundary, true) && ValidateExecutionGrant(grant, action.Footprint);
                if (result.Error != null) result.Reason = result.Error.GetBaseException().Message;
                else if (!result.AuthorityCurrent) result.Reason = "Action authority or native boundary changed during invocation.";
                else if (!result.Committed) result.Reason = "Native action outcome was not verified.";
                return result;
            }
            finally
            {
                try
                {
                    // Even an ambiguous invocation makes the caller's observations stale.
                    if (result.Invoked) InvalidateExecutionObservations(action.Footprint & ~ExecutionDomain.FiniteAction);
                }
                finally { _finiteActionInvoking = false; }
            }
        }

        internal static string ActionNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        internal static string ActionNumber(long value) => value.ToString(CultureInfo.InvariantCulture);
        internal FiniteActionIdentity ActionIdentity(string kind, string owner, string opportunity, string entry = null)
        {
            var rebirth = ActionNumber(Character.stats.rebirthNumber);
            return new FiniteActionIdentity(kind, owner, "rebirth:" + rebirth,
                entry ?? "rebirth:" + rebirth, opportunity);
        }
    }
}
