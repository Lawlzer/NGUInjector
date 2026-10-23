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
}
