using System;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private sealed class SupportApplicationResult
        {
            internal MutationDisposition Disposition;
            internal string Reason;
            internal Exception Error;
            internal SupportMutation Mutation;
            internal bool Verified => Disposition == MutationDisposition.AppliedVerified
                || Disposition == MutationDisposition.ModelAppliedRenderFailed;
        }

        private sealed class SupportApplicationException : InvalidOperationException
        {
            internal readonly SupportApplicationResult Result;
            internal SupportApplicationException(SupportApplicationResult result) : base(result.Reason, result.Error) { Result = result; }
        }

        private SupportApplicationResult _presetSupportOutcome;
        private sealed class PresetSupportAcquisition
        {
            internal string Event, Id;
            internal readonly LoadoutManager.SnapshotEpoch Epoch = new LoadoutManager.SnapshotEpoch();
            internal PresetRotationAttempt Rotation;
        }

        private sealed class PresetRotationAttempt
        {
            internal PresetCandidate Execution;
            internal DateTime At;
            internal SupportMutation Mutation;
        }

        private sealed class PresetSupportContinuation
        {
            internal PresetSupportAcquisition Owner;
            internal SupportMutation Mutation;
            internal string Desired;
            internal double BudgetFraction;
            internal bool EnforceBudget;
        }

        private PresetSupportAcquisition _presetSupportAcquisition;
        private PresetSupportContinuation _presetSupportContinuation;
        private bool _presetSupportPresentationPending;

        private PresetRotationAttempt PendingPresetRotation(string eventKey, string acquisition)
        {
            var current = _presetSupportAcquisition;
            return current != null && current.Event == eventKey && current.Id == acquisition && current.Epoch.Matches()
                ? current.Rotation : null;
        }

        private void RequirePendingPresetRotationSupport(PresetRotationAttempt attempt)
        {
            if (attempt?.Mutation == null || SupportMutationStillOwned(attempt.Mutation)) return;
            _presetSupportOutcome = new SupportApplicationResult { Disposition = MutationDisposition.ForeignConflict,
                Mutation = attempt.Mutation, Reason = "Pending rotation support changed externally; newer support was preserved." };
            throw new SupportApplicationException(_presetSupportOutcome);
        }

        // Only accepted acquisition boundaries may retire old evidence. This never restores native state.
        private void BeginPresetSupportAcquisition(string eventKey, string acquisition)
        {
            var current = _presetSupportAcquisition;
            if (current != null && current.Event == eventKey && current.Id == acquisition && current.Epoch.Matches()) return;
            _presetSupportContinuation = null;
            _presetSupportPresentationPending = false;
            _presetSupportAcquisition = new PresetSupportAcquisition { Event = eventKey, Id = acquisition };
        }

        private void ApplyPresetDesiredSupport(SupportSelection selection, double budgetFraction, bool enforceBudget)
        {
            var result = new SupportApplicationResult();
            _presetSupportOutcome = result;
            var previous = _supportMutation;
            var acquisition = _presetSupportAcquisition;
            try
            {
                if (acquisition == null || !acquisition.Epoch.Matches())
                    throw new InvalidOperationException("A fresh authorized preset support acquisition is required.");
                var continuation = _presetSupportContinuation;
                if (continuation != null && (!ReferenceEquals(continuation.Owner, acquisition)
                    || continuation.Desired != selection.StableKey || continuation.BudgetFraction != budgetFraction
                    || continuation.EnforceBudget != enforceBudget))
                    throw new InvalidOperationException("A different support intent requires a fresh authorized acquisition.");
                if (continuation != null && !SupportMutationStillOwned(continuation.Mutation))
                {
                    result.Disposition = MutationDisposition.ForeignConflict;
                    throw new InvalidOperationException("Support continuation changed externally; newer support was preserved.");
                }
                // Rebind only the synchronous boundary guard, after checking the old
                // bounded evidence. This grants no ownership over changed support.
                if (continuation != null) continuation.Mutation.Authorized = _selectedSetupSupportAuthorized;
                PreflightExactSupport(selection, budgetFraction, enforceBudget);
                if (CaptureSupportState().Selection.StableKey != selection.StableKey)
                    result.Mutation = ApplyExactSupport(selection, budgetFraction, enforceBudget);
                else if (_presetSupportPresentationPending && _presetSupportContinuation != null)
                {
                    result.Mutation = _presetSupportContinuation.Mutation;
                    try
                    {
                        WriteSupport(result.Mutation, () => Character.allBeards.refreshMenu());
                        WriteSupport(result.Mutation, () => Character.allDiggers.refreshMenu());
                    }
                    finally { RestoreSupportPodDrafts(result.Mutation); }
                }
                if (!ReferenceEquals(acquisition, _presetSupportAcquisition) || !acquisition.Epoch.Matches())
                {
                    result.Disposition = MutationDisposition.ForeignConflict;
                    throw new InvalidOperationException("Preset support acquisition changed during application.");
                }
                if (acquisition.Rotation != null && result.Mutation != null) acquisition.Rotation.Mutation = result.Mutation;
                _presetSupportContinuation = null;
                _presetSupportPresentationPending = false;
                result.Disposition = MutationDisposition.AppliedVerified;
                return;
            }
            catch (Exception error)
            {
                result.Error = error; result.Reason = error.Message;
                if (_selectedSetupSupportAuthorized != null && !_selectedSetupSupportAuthorized())
                    result.Disposition = MutationDisposition.ForeignConflict;
                if (acquisition != null && !ReferenceEquals(acquisition, _presetSupportAcquisition))
                    result.Disposition = MutationDisposition.ForeignConflict;
                if (result.Disposition != MutationDisposition.ForeignConflict && ReferenceEquals(previous, _supportMutation) && result.Mutation == null)
                    result.Disposition = MutationDisposition.RejectedUnchanged;
                else if (result.Disposition != MutationDisposition.ForeignConflict)
                {
                    result.Mutation = _supportMutation;
                    if (!SupportMutationStillOwned(result.Mutation)) result.Disposition = MutationDisposition.ForeignConflict;
                    else
                    {
                        _presetSupportContinuation = new PresetSupportContinuation { Owner = acquisition, Mutation = result.Mutation,
                            Desired = selection.StableKey, BudgetFraction = budgetFraction, EnforceBudget = enforceBudget };
                        if (acquisition.Rotation != null) acquisition.Rotation.Mutation = result.Mutation;
                        result.Disposition = CaptureSupportState().Selection.StableKey == selection.StableKey
                            ? MutationDisposition.ModelAppliedRenderFailed : MutationDisposition.KnownPartial;
                        if (result.Verified)
                        {
                            try { PreflightExactSupport(selection, budgetFraction, enforceBudget); }
                            catch { result.Disposition = MutationDisposition.KnownPartial; }
                        }
                    }
                }
                if (ReferenceEquals(acquisition, _presetSupportAcquisition) && result.Disposition != MutationDisposition.RejectedUnchanged)
                    _presetSupportPresentationPending = result.Disposition == MutationDisposition.ModelAppliedRenderFailed;
                if (!result.Verified) throw new SupportApplicationException(result);
            }
        }
    }
}
