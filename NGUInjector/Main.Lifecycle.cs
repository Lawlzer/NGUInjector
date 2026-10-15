using System;
using System.IO;

namespace NGUInjector
{
    internal partial class Main
    {
        private volatile bool _unloaded;
        private bool _started;
        private string _startupError;
        private FileStream _injectorLease;

        private void FailStartup(Exception error)
        {
            _startupError = error.Message;
            _started = false;
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
            _unloaded = true;
            _started = false;
            void Cleanup(Action action)
            {
                try { action(); }
                catch (Exception e) { LogError("Unload", e); }
            }

            try
            {
                Cleanup(() => CancelInvoke());
                Cleanup(() => StopAllCoroutines());
                if (ReferenceEquals(reference, this))
                {
                    if (_injectorLease != null && ExternalUiEnabled)
                        WriteExternalUiRuntimeSnapshot(0);
                    foreach (var resource in new IDisposable[] {
                        ConfigWatcher, AllocationWatcher, ZoneWatcher, settingsForm,
                        LootWriter, CombatWriter, AllocationWriter, PitSpinWriter, OutputWriter
                    })
                        Cleanup(() => resource?.Dispose());

                    ConfigWatcher = AllocationWatcher = ZoneWatcher = null;
                    settingsForm = null;
                    LootWriter = CombatWriter = AllocationWriter = PitSpinWriter = OutputWriter = null;
                    Settings = null;
                    CustomPlanStore = null;
                    Character = null;
                    Controller = null;
                    PlayerController = null;
                    WishManager = null;
                    _profile = null;
                    reference = null;
                }
            }
            finally
            {
                // Keep the lease through cancellation, the final snapshot, and every resource disposal.
                try { _injectorLease?.Dispose(); }
                finally { _injectorLease = null; }
            }
        }
    }
}
