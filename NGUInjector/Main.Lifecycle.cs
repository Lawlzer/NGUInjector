using System;
using System.IO;
using System.Linq;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private volatile bool _unloaded;
        private bool _started;
        private string _startupError;
        private FileStream _injectorLease;
        private bool _saveTestLeaseHandover, _saveTestSuperseded;
        internal bool SaveTestOwnerStopped => _unloaded;

        private void QuiesceExecution(string reason)
        {
            SuspendProtectedStageProgress();
            SaveProtectedStageProgress(true);
            SetExecutionLifecycle(ExecutionLifecycle.Quiescing);
            _executionRebindReview = null;
            SetCustomAutomationStatus(reason);
            if (!ServiceLifecycleRetirement())
                throw new InvalidOperationException("Quiesced: native route/settings retirement is unresolved. Prerequisites and semantic evidence retained; retry after native activity stops.");
        }

        private void FailStartup(Exception error)
        {
            _startupError = error.Message;
            _started = false;
            if (SaveTestActive || SaveTestInternalRestart)
            {
                Loader.SaveTestStartupFailed(this, error);
                return;
            }
            if (ReferenceEquals(reference, this))
                LogError("Start", error);
            else
                UnityEngine.Debug.LogError("NGUInjector startup failed: " + error.Message);
            Unload();
            if (reference == null)
                Loader.Unload();
            else
                enabled = false;
        }

        internal void Unload()
        {
            if (_unloaded)
                return;
            if (ReferenceEquals(reference, this) && !_saveTestSuperseded)
            {
                QuiesceExecution("Injector quiescing.");
            }
            SetExecutionLifecycle(ExecutionLifecycle.Unloaded);
            _unloaded = true;
            _started = false;
            Exception cleanupFailure = null;
            void Cleanup(Action action)
            {
                try { action(); }
                catch (Exception e) { cleanupFailure = cleanupFailure ?? e; LogError("Unload", e); }
            }

            try
            {
                if (ReferenceEquals(reference, this))
                {
                    if (!_saveTestSuperseded) Cleanup(() => SuspendProtectedStageProgress());
                }
                Cleanup(() => CancelInvoke());
                Cleanup(() => StopAllCoroutines());
                if (ReferenceEquals(reference, this))
                {
                    if (!_saveTestSuperseded) Cleanup(() => SaveProtectedStageProgress(true));
                    if (_injectorLease != null && ExternalUiEnabled)
                        Cleanup(() => WriteExternalUiRuntimeSnapshot(0));
                    foreach (var resource in new IDisposable[] {
                        ConfigWatcher, AllocationWatcher, ZoneWatcher, settingsForm,
                        LootWriter, CombatWriter, AllocationWriter, PitSpinWriter, OutputWriter
                    })
                        Cleanup(() => resource?.Dispose());

                    ConfigWatcher = AllocationWatcher = ZoneWatcher = null;
                    settingsForm = null;
                    LootWriter = CombatWriter = AllocationWriter = PitSpinWriter = OutputWriter = null;
                    Settings = null;
                    _presetResourcePresentationCharacter = null;
                    _presetResourcePresentationPending = false;
                    _presetResourceOutcome = null;
                    CustomPlanStore = null;
                    Character = null;
                    Controller = null;
                    PlayerController = null;
                    WishManager = null;
                    _profile = null;
                    _protectedScheduler = null;
                    reference = null;
                }
            }
            finally
            {
                // Keep the lease through cancellation, the final snapshot, and every resource disposal.
                try
                {
                    if (_saveTestLeaseHandover && _injectorLease != null) Loader.HoldSaveTestLease(this, _injectorLease);
                    else _injectorLease?.Dispose();
                }
                finally { _injectorLease = null; }
            }
            if (_saveTestLeaseHandover && !_saveTestSuperseded && cleanupFailure != null)
                throw new InvalidOperationException("Original-owner cleanup failed; fixture preparation was not admitted.", cleanupFailure);
        }

        internal void UnloadForSaveTest(bool supersedeFixture)
        {
            _saveTestLeaseHandover = true;
            _saveTestSuperseded = supersedeFixture;
            Unload();
        }
    }
}
