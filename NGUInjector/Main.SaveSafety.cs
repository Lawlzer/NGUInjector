using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private readonly Dictionary<string, SaveIdentity> _knownSaves = new Dictionary<string, SaveIdentity>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _saveScanErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _saveScanStamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _saveScanPending = new Queue<string>();
        private DateTime _saveScanDue;
        private bool _saveScanComplete, _saveRecoveryChecked;
        private string _saveRecoveryIssue;
        private SaveIdentity _saveHighWater;
        private readonly HashSet<string> _saveHighWaterHashes = new HashSet<string>(StringComparer.Ordinal);
        private bool _saveHighWaterDirty;
        private string SaveSafetyDirectory => Path.Combine(_dir, "save-safety");
        private string SaveRecoveryJournal => Path.Combine(SaveSafetyDirectory, "recovery.json");

        private static string SaveNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static void WriteSaveSafetyJson(string path, JSONObject value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(value.ToString(2));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".previous-" + Guid.NewGuid().ToString("N"));
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        // Establish the interlock before startup schedules any ordinary work. Merely
        // showing a recovery banner after reinjection would permit unsafe execution.
        private void InitializeSaveRecovery()
        {
            if (_saveRecoveryChecked) return;
            _saveRecoveryChecked = true;
            try
            {
                if (File.Exists(SaveRecoveryJournal))
                {
                    var journal = JsonFiles.ParseObject(File.ReadAllText(SaveRecoveryJournal));
                    if (journal["phase"].Value != "loaded")
                        _saveRecoveryIssue = "An interrupted save load needs review. Protected backup: " + journal["backup"]["path"].Value;
                }
                var highWaterPath = Path.Combine(SaveSafetyDirectory, "high-water.json");
                if (File.Exists(highWaterPath))
                {
                    var saved = JsonFiles.ParseObject(File.ReadAllText(highWaterPath));
                    var total = double.Parse(saved["totalPlaytimeSeconds"].Value, CultureInfo.InvariantCulture);
                    var elapsed = double.Parse(saved["rebirthSeconds"].Value, CultureInfo.InvariantCulture);
                    var rebirth = long.Parse(saved["rebirth"].Value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(total) || double.IsInfinity(total) || total < 0 || double.IsNaN(elapsed)
                        || double.IsInfinity(elapsed) || elapsed < 0 || elapsed > total || rebirth < 0
                        || saved["sha256"].Value.Length != 64 || string.IsNullOrWhiteSpace(saved["path"].Value))
                        throw new InvalidDataException("Invalid saved playtime high-water mark.");
                    _saveHighWater = new SaveIdentity(saved["path"].Value, saved["sha256"].Value, total,
                        rebirth, elapsed, saved["version"].AsInt, 0, saved["playerName"].Value);
                    _saveHighWaterHashes.Add(_saveHighWater.Sha256);
                    if (saved["competingHashes"].IsArray)
                        foreach (var hash in saved["competingHashes"].AsArray.Children)
                        {
                            if (!hash.IsString || hash.Value.Length != 64 || _saveHighWaterHashes.Count >= 4096)
                                throw new InvalidDataException("Invalid saved playtime branch evidence.");
                            _saveHighWaterHashes.Add(hash.Value);
                        }
                }
            }
            catch (Exception error) { _saveRecoveryIssue = "Save-safety recovery evidence cannot be read: " + error.Message; }
            if (_saveRecoveryIssue != null) SetExecutionLifecycle(ExecutionLifecycle.LoadingSave);
        }

        private void ObserveSaveHighWater(SaveIdentity identity)
        {
            // Observation cannot be forgotten merely because its publication fails.
            if (_saveHighWater == null || identity.TotalPlaytimeSeconds > _saveHighWater.TotalPlaytimeSeconds)
            {
                _saveHighWater = identity;
                _saveHighWaterHashes.Clear();
                _saveHighWaterHashes.Add(identity.Sha256);
                _saveHighWaterDirty = true;
            }
            else if (identity.TotalPlaytimeSeconds == _saveHighWater.TotalPlaytimeSeconds && _saveHighWaterHashes.Add(identity.Sha256))
                _saveHighWaterDirty = true;
            PersistSaveHighWater();
        }

        private void PersistSaveHighWater()
        {
            if (!_saveHighWaterDirty) return;
            var record = SaveIdentityJson(_saveHighWater); var hashes = new JSONArray();
            foreach (var hash in _saveHighWaterHashes.OrderBy(x => x, StringComparer.Ordinal)) hashes.Add(hash);
            record["competingHashes"] = hashes;
            try { WriteSaveSafetyJson(Path.Combine(SaveSafetyDirectory, "high-water.json"), record); }
            catch (Exception error)
            {
                _saveScanComplete = false;
                _saveScanErrors["high-water"] = "Playtime evidence is not durable: " + error.Message;
                throw;
            }
            _saveHighWaterDirty = false;
            _saveScanErrors.Remove("high-water");
        }

        private SaveIdentity LongestKnownSave(out bool ambiguous)
        {
            var longest = SaveSafetyCatalog.SelectLatest(_knownSaves.Values, null, out ambiguous);
            if (_saveHighWater == null) return longest;
            ambiguous |= _saveHighWaterHashes.Count > 1;
            if (longest == null || _saveHighWater.TotalPlaytimeSeconds > longest.TotalPlaytimeSeconds) return _saveHighWater;
            if (_saveHighWater.TotalPlaytimeSeconds == longest.TotalPlaytimeSeconds && _saveHighWater.Sha256 != longest.Sha256) ambiguous = true;
            return longest;
        }

        // Discovery is bounded; decoding one file per frame avoids a synchronous scan
        // of years of backups on the Unity game thread. A command can request a full rescan.
        private void PumpSaveSafety()
        {
            if (Character?.stats == null || string.IsNullOrEmpty(_dir) || _knownSaves == null) return;
            try
            {
                InitializeSaveRecovery();
                if (_saveScanPending.Count == 0 && DateTime.UtcNow >= _saveScanDue) BeginSaveScan(false);
                if (_saveScanPending.Count != 0)
                {
                    var path = _saveScanPending.Dequeue();
                    try
                    {
                        var before = new FileInfo(path);
                        var stamp = before.Length + ":" + before.LastWriteTimeUtc.Ticks;
                        var identity = SaveSafetyCatalog.Read(path, Character.getVersion());
                        _knownSaves[path] = identity;
                        ObserveSaveHighWater(identity);
                        var after = new FileInfo(path);
                        if (stamp != after.Length + ":" + after.LastWriteTimeUtc.Ticks)
                            throw new IOException("Save changed during inspection; it must be inspected again.");
                        _saveScanStamps[path] = stamp;
                        _saveScanErrors.Remove(path);
                    }
                    catch (Exception error)
                    {
                        // Never forget a previously decoded high-water mark on a failed read.
                        _saveScanErrors[path] = error.Message;
                        _saveScanStamps.Remove(path);
                    }
                    if (_saveScanPending.Count == 0) CompleteSaveScan();
                }
            }
            catch (Exception error)
            {
                _saveScanComplete = false;
                _saveScanErrors["scan"] = error.Message;
                _saveScanDue = DateTime.UtcNow.AddSeconds(60);
            }
        }

        private void BeginSaveScan(bool force)
        {
            _saveScanPending.Clear();
            _saveScanComplete = false;
            _saveScanErrors.Clear();
            _saveScanDue = DateTime.UtcNow.AddSeconds(60);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in new[] { UnityEngine.Application.persistentDataPath, Path.Combine(SaveSafetyDirectory, "backups") })
            {
                if (!Directory.Exists(directory))
                {
                    if (directory == UnityEngine.Application.persistentDataPath) _saveScanErrors[directory] = "Native save directory is unavailable.";
                    continue;
                }
                try
                {
                    foreach (var path in Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly))
                    {
                        if (paths.Count >= 4096) throw new InvalidOperationException("Save discovery exceeds the 4,096-file safety limit.");
                        paths.Add(Path.GetFullPath(path));
                    }
                }
                catch (Exception error) { _saveScanErrors[directory] = error.Message; }
            }
            var quick = Path.Combine(_dir, "NGUSave.txt");
            if (File.Exists(quick))
            {
                if (paths.Count >= 4096) _saveScanErrors[quick] = "Save discovery exceeds the 4,096-file safety limit.";
                else paths.Add(quick);
            }
            // Previously observed files stay in the catalog even if removed. Their
            // missing bytes must not silently lower the best-known progression.
            foreach (var missing in _knownSaves.Keys.Where(x => !paths.Contains(x)))
                _saveScanErrors[missing] = "A previously known save disappeared; its playtime high-water mark is retained.";
            foreach (var path in paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var info = new FileInfo(path);
                    var stamp = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
                    if (force || !_knownSaves.ContainsKey(path) || !_saveScanStamps.TryGetValue(path, out var previous) || previous != stamp)
                        _saveScanPending.Enqueue(path);
                }
                catch (Exception error) { _saveScanErrors[path] = error.Message; }
            }
            if (_saveScanPending.Count == 0) CompleteSaveScan();
        }

        private void CompleteSaveScan()
        {
            try { PersistSaveHighWater(); }
            catch (Exception error) { _saveScanErrors["high-water"] = "Playtime evidence is not durable: " + error.Message; }
            if (_saveHighWater != null && (!_knownSaves.TryGetValue(_saveHighWater.Path, out var current)
                || current.TotalPlaytimeSeconds < _saveHighWater.TotalPlaytimeSeconds))
                _saveScanErrors[_saveHighWater.Path] = "The previously longest save is missing or was replaced by an earlier save. Its high-water mark is retained.";
            _saveScanComplete = _saveScanErrors.Count == 0;
        }

        private SaveIdentity CurrentSaveIdentity()
        {
            var total = Character.totalPlaytime.totalseconds;
            var elapsed = Character.rebirthTime.totalseconds;
            if (double.IsNaN(total) || double.IsInfinity(total) || total < 0
                || double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0 || elapsed > total
                || Character.stats.rebirthNumber < 0)
                throw new InvalidOperationException("The current game's playtime or rebirth clock is invalid.");
            return new SaveIdentity("", "", total, Character.stats.rebirthNumber, elapsed, Character.getVersion(), Character.lastTime, Character.playerName);
        }

        private static JSONObject SaveIdentityJson(SaveIdentity identity)
        {
            var result = new JSONObject();
            if (identity == null) return result;
            result["path"] = identity.Path ?? "";
            result["sha256"] = identity.Sha256 ?? "";
            result["totalPlaytimeSeconds"] = SaveNumber(identity.TotalPlaytimeSeconds);
            result["rebirthSeconds"] = SaveNumber(identity.RebirthSeconds);
            result["rebirth"] = identity.Rebirth.ToString(CultureInfo.InvariantCulture);
            result["version"] = identity.Version;
            result["playerName"] = identity.PlayerName ?? "";
            return result;
        }

        private JSONObject GetSaveSafetyRuntime()
        {
            var result = new JSONObject();
            result["version"] = 1; result["testActive"] = false;
            if (_knownSaves == null || _saveScanPending == null || _saveScanErrors == null || _saveHighWaterHashes == null)
            {
                // Optional telemetry also runs during partial startup and native
                // fixture construction; absence is unknown, never verified safety.
                result["status"] = "unknown"; result["scanComplete"] = false;
                result["pendingFiles"] = 0; result["knownFiles"] = 0; result["errors"] = new JSONArray();
                result["message"] = "Save inspection has not initialized.";
                result["liveFixtureTestingSupported"] = false;
                return result;
            }
            result["scanComplete"] = _saveScanComplete;
            result["pendingFiles"] = _saveScanPending.Count;
            result["knownFiles"] = _knownSaves.Count;
            result["liveFixtureTestingSupported"] = false;
            result["liveFixtureBlocker"] = "Native autosaves, Steam Cloud/achievements, and automation checkpoints are not isolated. Destructive fixture loads are disabled.";
            var errors = new JSONArray();
            foreach (var error in _saveScanErrors.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var row = new JSONObject(); row["path"] = error.Key; row["message"] = error.Value; errors.Add(row);
            }
            result["errors"] = errors;
            try
            {
                var current = CurrentSaveIdentity();
                var longest = LongestKnownSave(out var ambiguous);
                result["current"] = SaveIdentityJson(current); result["longest"] = SaveIdentityJson(longest);
                result["ambiguousLongest"] = ambiguous;
                var behind = longest != null && current.TotalPlaytimeSeconds + 0.001 < longest.TotalPlaytimeSeconds;
                result["status"] = _saveRecoveryIssue != null ? "recoveryRequired" : behind ? "behind"
                    : !_saveScanComplete || longest == null || ambiguous ? "unknown" : "verified";
                result["message"] = _saveRecoveryIssue ?? (behind ? "The loaded game has less total playtime than a decoded save. Do not overwrite your newer progress."
                    : !_saveScanComplete ? "Save inspection is incomplete. Longest known playtime is not yet verified."
                    : ambiguous ? "Different saves tie for longest playtime. Review the saved branches; no automatic branch choice is safe."
                    : longest == null ? "No native save has been verified yet." : "Current playtime is at least the longest known save.");
            }
            catch (Exception error)
            {
                result["status"] = _saveRecoveryIssue != null ? "recoveryRequired" : "unknown";
                result["message"] = _saveRecoveryIssue ?? error.Message;
            }
            return result;
        }

        private SaveIdentity CaptureVerifiedLocalSave()
        {
            var before = CurrentSaveIdentity();
            // Native export does not stamp lastTime. Stamp only the synchronous
            // capture so restoring it cannot double-count stale offline progress.
            var oldTime = Character.lastTime;
            string encoded;
            try { Character.lastTime = Epoch.Current(); encoded = Character.importExport.getBase64Data(); }
            finally { Character.lastTime = oldTime; }
            var bytes = new UTF8Encoding(false, true).GetBytes(encoded);
            var directory = Path.Combine(SaveSafetyDirectory, "backups");
            var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".txt");
            var decoded = SaveSafetyCatalog.FromBytes(bytes, path, Character.getVersion());
            if (decoded.Rebirth != before.Rebirth || Math.Abs(decoded.TotalPlaytimeSeconds - before.TotalPlaytimeSeconds) > 0.001
                || Math.Abs(decoded.RebirthSeconds - before.RebirthSeconds) > 0.001 || decoded.PlayerName != before.PlayerName)
                throw new InvalidOperationException("Native export did not match the current game's playtime/rebirth. No load is permitted.");
            Directory.CreateDirectory(directory);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
            var verified = SaveSafetyCatalog.Read(path, Character.getVersion());
            if (verified.Sha256 != decoded.Sha256) throw new IOException("Protective save read-back hash mismatch.");
            _knownSaves[path] = verified;
            var info = new FileInfo(path);
            _saveScanStamps[path] = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            ObserveSaveHighWater(verified);
            var manifest = SaveIdentityJson(verified); manifest["capturedAt"] = DateTime.UtcNow.ToString("o");
            JsonFiles.WriteAtomic(path + ".json", manifest.ToString(2));
            return verified;
        }

        private void RequireSafeCloudSave()
        {
            VerifySaveCandidatesForMutation();
            var safety = GetSaveSafetyRuntime();
            if (safety["status"].Value != "verified")
                throw new InvalidOperationException("Cloud/quicksave publication blocked: " + safety["message"].Value + " A verified local backup has been retained.");
        }

        private void VerifySaveCandidatesForMutation()
        {
            // File dates only optimize display scans. A destructive action must
            // inspect actual bytes, including every nonselected candidate.
            BeginSaveScan(true);
            while (_saveScanPending.Count != 0) PumpSaveSafety();
        }

        private string LoadLongestKnownSave(JSONNode args, out JSONNode data)
        {
            data = null;
            if (args == null || args.Count != 2 || !args["sha256"].IsString || !args["expectedStartedAt"].IsString
                || args["expectedStartedAt"].Value != ExternalUiStartedAt.ToString("o"))
                throw new FormatException("loadLongestSave requires sha256 and this injector's expectedStartedAt.");
            if (!_saveScanComplete || _saveScanPending.Count != 0 || _saveRecoveryIssue != null)
                throw new InvalidOperationException("A complete save scan without recovery errors is required before loading.");
            VerifySaveCandidatesForMutation();
            if (!_saveScanComplete) throw new InvalidOperationException("Save files changed after inspection. Wait for a fresh complete scan; no load was attempted.");
            var longest = LongestKnownSave(out var ambiguous);
            if (longest == null || longest.Sha256 != args["sha256"].Value)
                throw new InvalidOperationException("Longest-save selection changed. Inspect the fresh scan; no load was attempted.");
            var backup = CaptureVerifiedLocalSave();
            var report = new JSONObject(); report["backup"] = SaveIdentityJson(backup); report["selected"] = SaveIdentityJson(longest); data = report;
            if (ambiguous) throw new InvalidOperationException("Different saves tie for longest playtime. Review the branches before loading.");
            if (backup.TotalPlaytimeSeconds >= longest.TotalPlaytimeSeconds - 0.001)
            {
                report["loaded"] = false;
                return "The current game already has the longest playtime; no unnecessary save reload or scheduler reset was performed.";
            }
            if (longest.PlayerName != backup.PlayerName)
                throw new InvalidOperationException("Longest save has a different player name. Cross-player loading requires manual review.");
            var pinned = SaveSafetyCatalog.ReadSnapshot(longest.Path, Character.getVersion(), out var bytes);
            if (pinned.Sha256 != longest.Sha256) throw new IOException("Selected save changed since inspection; no load was attempted.");
            var encoded = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff');
            var native = Character.importExport.getSaveDataFromString(encoded);
            if (native == null) throw new InvalidOperationException("Native decoder rejected the pinned save; no load was attempted.");
            var journal = new JSONObject(); journal["phase"] = "preparing";
            journal["backup"] = SaveIdentityJson(backup); journal["selected"] = SaveIdentityJson(pinned);
            WriteSaveSafetyJson(SaveRecoveryJournal, journal);
            try
            {
                QuiesceExecution("Preparing longest-playtime save load.");
                SetExecutionLifecycle(ExecutionLifecycle.LoadingSave);
                _protectedTimingLoadingSave = true;
                journal["phase"] = "loading"; WriteSaveSafetyJson(SaveRecoveryJournal, journal);
                Character.saveLoad.loadintoGame(native);
                var actual = CurrentSaveIdentity();
                if (actual.PlayerName != pinned.PlayerName || actual.Rebirth != pinned.Rebirth || actual.TotalPlaytimeSeconds + 0.001 < pinned.TotalPlaytimeSeconds
                    || actual.RebirthSeconds + 0.001 < pinned.RebirthSeconds
                    || Math.Abs((actual.TotalPlaytimeSeconds - actual.RebirthSeconds) - (pinned.TotalPlaytimeSeconds - pinned.RebirthSeconds)) > 5)
                    throw new InvalidOperationException("Native loading did not establish the selected save's playtime/rebirth. Automation remains quiesced.");
                ResetCustomAutomationRuntime();
                journal["phase"] = "loaded"; journal["observed"] = SaveIdentityJson(actual);
                WriteSaveSafetyJson(SaveRecoveryJournal, journal);
                SetExecutionLifecycle(ExecutionLifecycle.Running);
                report["loaded"] = true; report["observed"] = SaveIdentityJson(actual);
                return "Longest-playtime save loaded and verified. Saved automation selections retain their normal review requirements.";
            }
            catch (Exception error)
            {
                _saveRecoveryIssue = "Save load was not verified: " + error.Message + " Protected original: " + backup.Path;
                journal["phase"] = "recoveryRequired"; journal["error"] = _saveRecoveryIssue;
                try { WriteSaveSafetyJson(SaveRecoveryJournal, journal); } catch { }
                throw;
            }
            finally { _protectedTimingLoadingSave = false; }
        }

        private string ExecuteSaveSafetyCommand(string command, JSONNode args, out JSONNode data)
        {
            if (command == "loadLongestSave") return LoadLongestKnownSave(args, out data);
            if (args != null && !args.IsNull && args.Count != 0) throw new FormatException(command + " does not accept arguments.");
            data = new JSONObject();
            if (command == "captureLocalSave")
            { data["save"] = SaveIdentityJson(CaptureVerifiedLocalSave()); return "Native local save written, decoded, and read-back verified. No cloud write was requested."; }
            BeginSaveScan(true);
            data["saveSafety"] = GetSaveSafetyRuntime();
            return "Full native-save inspection requested. Wait for scanComplete before selecting a load.";
        }
    }
}
