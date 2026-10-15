using System;
using System.IO;
using System.Linq;
using NGUInjector.AllocationProfiles;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal static string GetProfilesDir()
        {
            return _profilesDir;
        }

        internal static string GetCustomPlansDir()
        {
            return _customPlansDir;
        }

        internal static string GetBaseDir()
        {
            return _dir;
        }

        internal static void UpdateForm(SavedSettings newSettings)
        {
            settingsForm?.UpdateFromSettings(newSettings);
        }

        internal static void LoadAllocation()
        {
            _profile = new CustomAllocation(_profilesDir, Settings.AllocationFile);
            try
            {
                _profile.ReloadAllocation();
            }
            catch (Exception e)
            {
                LogError("LoadAllocation", e);
            }
        }

        private static void LoadAllocationProfiles()
        {
            var files = Directory.GetFiles(_profilesDir);
            settingsForm?.UpdateProfileList(files.Select(Path.GetFileNameWithoutExtension).ToArray(), Settings.AllocationFile);
        }

        private readonly object _watcherReloadLock = new object();
        private readonly DateTime[] _watcherReloadDue = new DateTime[4];
        private readonly int[] _watcherReloadAttempts = new int[4];

        private void QueueWatcherReload(int kind)
        {
            lock (_watcherReloadLock)
            {
                if (_unloaded)
                    return;
                _watcherReloadDue[kind] = DateTime.UtcNow.AddMilliseconds(250);
                _watcherReloadAttempts[kind] = 0;
            }
        }

        private void PumpWatcherReloads()
        {
            for (var kind = 0; kind < _watcherReloadDue.Length; kind++)
            {
                int attempt;
                lock (_watcherReloadLock)
                {
                    if (_unloaded || _watcherReloadDue[kind] == DateTime.MinValue || DateTime.UtcNow < _watcherReloadDue[kind])
                        continue;
                    _watcherReloadDue[kind] = DateTime.MinValue;
                    attempt = ++_watcherReloadAttempts[kind];
                }
                try
                {
                    switch (kind)
                    {
                        case 0:
                            if (Settings.HasSettingsFileChanged())
                                ReloadSettingsForExternalUi();
                            break;
                        case 1:
                            var path = Path.Combine(_profilesDir, Settings.AllocationFile + ".json");
                            JsonFiles.ParseObject(File.ReadAllText(path));
                            LoadAllocation();
                            break;
                        case 2:
                            LoadAllocationProfiles();
                            break;
                        case 3:
                            JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "zoneOverride.json")));
                            ZoneStatHelper.CreateOverrides(_dir);
                            break;
                    }
                }
                catch (Exception e)
                {
                    if (attempt >= 5)
                        LogError("File watcher reload " + kind, e);
                    else
                    {
                        lock (_watcherReloadLock)
                        {
                            if (!_unloaded && _watcherReloadDue[kind] == DateTime.MinValue)
                                _watcherReloadDue[kind] = DateTime.UtcNow.AddMilliseconds(500);
                        }
                    }
                }
            }
        }
    }
}
