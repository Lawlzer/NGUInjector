using System;
using System.Globalization;
using System.IO;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal abstract class FiniteAction
        {
            internal FiniteActionIdentity Identity;
            internal ExecutionDomain Footprint;
            internal ExecutionDomain DependencyDomains;
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
            internal bool Committed, Durable, AuthorityCurrent, PreviouslyCommitted;
            internal string Reason;
            internal Exception Error;
            internal bool Verified => Committed && Durable && AuthorityCurrent;
        }

        private FiniteActionLedger _finiteActionLedger;
        private bool _finiteActionInvoking;
        private FiniteActionLedger FiniteActions => _finiteActionLedger ?? (_finiteActionLedger = new FiniteActionLedger(
            string.IsNullOrEmpty(_dir) ? null : Path.Combine(_dir, "finite-actions-v1")));
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
            if (_finiteActionInvoking) { result.Reason = "A native finite action is already invoking."; return result; }
            FiniteActionRecord record;
            Func<bool, bool> nativeBoundary;
            try
            {
                var ledger = FiniteActions;
                var epoch = "rebirth:" + ActionNumber(Character.stats.rebirthNumber);
                var dependencies = (int)(action.DependencyDomains == ExecutionDomain.None ? action.Footprint : action.DependencyDomains)
                    & ~(int)ExecutionDomain.FiniteAction;
                record = ledger.Find(action.Identity);
                if (record?.State == FiniteActionState.Committed)
                {
                    if (record.Dirty) ledger.Save(record);
                    result.Committed = result.Durable = true;
                    result.PreviouslyCommitted = true;
                    // Durable history prevents a repeat; it is not fresh proof of
                    // this native branch or permission to credit another effect.
                    result.AuthorityCurrent = false;
                    result.Reason = "This action already committed; no new invocation was admitted.";
                    result.Disposition = MutationDisposition.AppliedVerified;
                    return result;
                }
                if (!ledger.FlushDependentResults(action.Identity, epoch, dependencies))
                { result.Reason = "A dependent committed action result is awaiting durable storage."; return result; }
                if (ledger.HasUnresolvedDependencies(action.Identity, epoch, dependencies))
                {
                    result.Disposition = MutationDisposition.UnknownOutcome;
                    result.Reason = "A dependent native action has unresolved invocation/result evidence.";
                    return result;
                }
                if (ledger.HasUnresolved(action.Identity))
                {
                    result.Disposition = MutationDisposition.UnknownOutcome;
                    result.Reason = "This action has unresolved invocation evidence; it will not be repeated.";
                    return result;
                }
                if (!VerifyExecutionPrerequisites(grant, action.Footprint, action.Prerequisites))
                { result.Reason = "Action setup or authority is not verified."; return result; }
                var before = action.Observe();
                nativeBoundary = CaptureFiniteActionBoundary(action);
                if (!VerifyExecutionPrerequisites(grant, action.Footprint, action.Prerequisites)) return result;
                record = new FiniteActionRecord { Identity = action.Identity, State = FiniteActionState.Prepared, Before = before,
                    Epoch = epoch, Domains = dependencies };
                ledger.Save(record);
                if (!VerifyExecutionPrerequisites(grant, action.Footprint, () => action.Prerequisites() && action.Observe() == before && FiniteActionBoundaryCurrent(nativeBoundary, false)))
                    return result;
                record.State = FiniteActionState.Invocation;
                // If this write throws, even an ambiguously completed file replacement is never treated as uninvoked.
                ledger.Save(record);
            }
            catch (Exception error)
            {
                result.Reason = "Finite-action storage/preflight failed: " + error.Message;
                return result;
            }

            Exception invocationError = null;
            _finiteActionInvoking = true;
            try
            {
                // No native callback is allowed between this check and the single leaf.
                if (!ValidateExecutionGrant(grant, action.Footprint) || !FiniteActionBoundaryCurrent(nativeBoundary, false))
                    throw new InvalidOperationException("Action authority changed after invocation persistence.");
                action.Invoke();
            }
            catch (Exception error) { invocationError = error; }
            finally { _finiteActionInvoking = false; }
            result.AuthorityCurrent = ValidateExecutionGrant(grant, action.Footprint) && FiniteActionBoundaryCurrent(nativeBoundary, true);
            try
            {
                record.After = action.Observe();
                result.Disposition = action.Classify(record.Before, record.After, invocationError);
            }
            catch (Exception error)
            {
                invocationError = invocationError ?? error;
                result.Disposition = MutationDisposition.UnknownOutcome;
            }
            result.Committed = result.Disposition == MutationDisposition.AppliedVerified
                || result.Disposition == MutationDisposition.ModelAppliedRenderFailed;
            result.AuthorityCurrent &= ValidateExecutionGrant(grant, action.Footprint) && FiniteActionBoundaryCurrent(nativeBoundary, true);
            record.State = result.Committed ? FiniteActionState.Committed
                : result.Disposition == MutationDisposition.RejectedUnchanged ? FiniteActionState.Rejected : FiniteActionState.Unknown;
            record.Disposition = result.Disposition.ToString();
            try { _finiteActionLedger.Save(record); result.Durable = true; }
            catch (Exception error) { result.Reason = "Action result persistence failed: " + error.Message; }
            // Invalidate only after the result is retained, including unknown/partial outcomes.
            InvalidateExecutionObservations(action.Footprint & ~ExecutionDomain.FiniteAction);
            if (result.Reason == null && invocationError != null) result.Reason = invocationError.GetBaseException().Message;
            result.Error = invocationError;
            return result;
        }

        internal static string ActionNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        internal static string ActionNumber(long value) => value.ToString(CultureInfo.InvariantCulture);
        internal static bool PreparationAcquisitionCanRetry(FiniteActionRecord record) => record.State == FiniteActionState.Prepared && record.After == "acquired";
        internal static bool PreparationContinuesRebirth(FiniteActionRecord record)
        {
            var original = JsonFiles.ParseObject(record.Before);
            if (!original["manualRebirth"].IsBoolean || !original["plan"].IsString || !original["opportunity"].IsString
                || original["opportunity"].Value != record.Identity.Opportunity)
                throw new InvalidOperationException("Original fight-preparation intent/opportunity requires review.");
            return original["manualRebirth"].AsBool || record.Identity.Opportunity == "automatic:" + original["plan"].Value;
        }
        internal static string PreparationFruitOpportunity(FiniteActionIdentity preparation, int fruitId) => preparation.Key + ":fruit:" + ActionNumber(fruitId);
        internal static bool PreparationRetirementDurable(FiniteActionRecord record) => record != null
            && record.Identity.Kind == "preparation-handoff" && record.State == FiniteActionState.Committed && !record.Dirty && record.After == "retired";
        internal static bool PreparationSupersessionBlocksImport(FiniteActionRecord handoff, FiniteActionIdentity original) => handoff.Identity.Kind == "preparation-handoff"
            && handoff.Identity.Entry == original.Key && (handoff.State == FiniteActionState.Invocation || handoff.State == FiniteActionState.Unknown
                || handoff.State == FiniteActionState.Committed);
        internal static bool KnownRebirthResultCanFinish(FiniteActionRecord record, long original, long observed) => record != null
            && record.Identity.Kind == "rebirth" && record.State == FiniteActionState.Committed && !record.Dirty
            && record.Identity.Acquisition == "rebirth:" + ActionNumber(original) && observed == original + 1;
        internal FiniteActionIdentity ActionIdentity(string kind, string owner, string opportunity, string entry = null)
        {
            var rebirth = ActionNumber(Character.stats.rebirthNumber);
            return new FiniteActionIdentity(kind, owner, "rebirth:" + rebirth,
                entry ?? "rebirth:" + rebirth, opportunity);
        }
    }
}
