using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NGUInjector
{
    public class Loader
    {
        private static GameObject _load;
        private static SaveTestHost _saveTestHost;

        internal static void RetainSaveTestRecovery(Main adapter)
        {
            if (_saveTestHost == null)
            {
                var host = new GameObject("NGUInjector.SaveTestRecovery");
                _saveTestHost = host.AddComponent<SaveTestHost>();
                Object.DontDestroyOnLoad(host);
            }
            _saveTestHost.Adapter = adapter;
        }

        internal static bool IsSaveTestAdapter(Main adapter) => _saveTestHost != null
            && ReferenceEquals(_saveTestHost.Adapter, adapter);

        internal static void HoldSaveTestLease(Main adapter, FileStream lease)
        {
            RetainSaveTestRecovery(adapter);
            if (_saveTestHost.Lease != null && !ReferenceEquals(_saveTestHost.Lease, lease))
                throw new InvalidOperationException("Recovery host already holds a different injector lease.");
            _saveTestHost.Lease = lease;
        }

        internal static FileStream AdoptSaveTestLease()
        {
            if (_saveTestHost == null) return null;
            var lease = _saveTestHost.Lease;
            _saveTestHost.Lease = null;
            return lease;
        }

        internal static void ReplaceSaveTestMain(Main owner, bool supersedeFixture)
        {
            RetainSaveTestRecovery(owner);
            var load = _load;
            // Transfer the lease only after callbacks, writers and watchers have stopped.
            try { owner.UnloadForSaveTest(supersedeFixture); }
            finally
            {
                // Strict quiesce refusal retains the original object. A later cleanup
                // failure must not leave a stopped object blocking explicit recovery Init.
                if (owner.SaveTestOwnerStopped)
                {
                    _load = null;
                    if (load != null)
                    {
                        load.SetActive(false);
                        Object.Destroy(load);
                    }
                }
            }
        }

        internal static void SaveTestStartupFailed(Main owner, Exception error)
        {
            owner.RecordSaveTestFailure(error);
            ReplaceSaveTestMain(owner, true);
        }

        internal static void SaveTestMainReady(Main owner)
        {
            if (_saveTestHost != null) _saveTestHost.Adapter = owner;
        }
        public static void Init()
        {
            SaveSafetyDependencies.Register();
            TraceStartup("Loader.Init entered.");
            try
            {
                if (_load != null || Main.reference != null)
                {
                    TraceStartup("Loader.Init skipped: an existing injector object or reference is present.");
                    return;
                }
                _load = new GameObject();
                TraceStartup("Injector GameObject created.");
                var component = _load.AddComponent<Main>();
                TraceStartup("Main component added: " + (component != null));
                Object.DontDestroyOnLoad(_load);
                TraceStartup("Injector GameObject retained across scenes.");
            }
            catch (Exception e)
            {
                TraceStartup("Loader.Init failed: " + e);
                if (Main.SaveTestActive && Main.reference != null)
                    SaveTestStartupFailed(Main.reference, e);
                else if (Main.SaveTestActive && _load != null)
                {
                    _load.SetActive(false);
                    Object.Destroy(_load);
                    _load = null;
                }
                throw;
            }
        }

        internal static void TraceStartup(string message)
        {
            try
            {
                var directory = Path.Combine(Environment.ExpandEnvironmentVariables("%userprofile%/Desktop"), "NGUInjector");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "loader-startup.log"), DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
            }
            catch
            {
                // A diagnostic failure must not prevent injection or cleanup.
            }
        }

        public static void Unload()
        {
            if (Main.SaveTestActive)
                throw new InvalidOperationException("Restore the protected original before unloading a save-test session.");
            _Unload();
        }

        private static void _Unload()
        {
            var load = _load;
            var instance = Main.reference;
            if (instance == null && load != null)
                instance = load.GetComponent<Main>();
            // A pending gear recovery can refuse unload; retain its component and timers.
            instance?.Unload();
            _load = null;
            if (load != null)
            {
                load.SetActive(false);
                Object.Destroy(load);
            }
        }
    }

    // The sole recovery pump delegates to the existing claim/result transport, never a second executor.
    internal sealed class SaveTestHost : MonoBehaviour
    {
        internal Main Adapter;
        internal FileStream Lease;
        private void Update()
        {
            if (!Main.SaveTestActive || ReferenceEquals(Adapter, null)) return;
            try { Adapter.PumpSaveTestRecovery(); }
            catch (Exception error) { Loader.TraceStartup("Save-test recovery pump failed: " + error); }
        }
    }
}
