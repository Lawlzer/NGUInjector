using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SimpleJSON;

namespace NGUInjector
{
    // The coordinator must keep the injector lease and all writers quiescent for every call.
    // Capture or explicit coordinator pins establish trust; disk metadata cannot select roots.
    internal static class SaveTestBundle
    {
        private const int MaxFiles = 4096;
        private const int MaxFileBytes = 32 * 1024 * 1024;
        private const long MaxTotalBytes = 512L * 1024 * 1024;
        private const int MaxManifestBytes = 16 * 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>(Names);
        private static readonly string[] Trees = { "profiles", "custom-plans" };
        private static readonly string[] ConfigNames = { "settings.json", "presets.json", "zoneOverride.json", "allocation.json" };

        private sealed class Session
        {
            internal string Runtime, Native, Directory, ManifestHash;
            internal string Bundle { get { return Path.Combine(Directory, "bundle"); } }
        }

        private sealed class Entry
        {
            internal string Area, Path, Hash;
            internal long Length;
            internal int Index;
            internal byte[] Data;
            internal string Key { get { return Area + "/" + Path; } }
        }

        internal static JSONObject Capture(string runtimeRoot, string nativeRoot, string sessionDirectory)
        {
            lock (Gate)
            {
                var session = ValidateSession(runtimeRoot, nativeRoot, sessionDirectory);
                if (Sessions.ContainsKey(session.Directory) || Exists(session.Bundle))
                    throw new IOException("A bundle already exists for this session; use a new session directory.");

                var files = Discover(session, false);
                var manifest = new JSONObject();
                manifest["version"] = 1;
                manifest["runtimeRoot"] = session.Runtime;
                manifest["nativeRoot"] = session.Native;
                manifest["sessionDirectory"] = session.Directory;
                var rows = new JSONArray();
                for (var i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    file.Index = i;
                    var bytes = ReadChecked(LivePath(session, file), file.Length, file.Hash);
                    WriteNew(SnapshotPath(session, file), bytes, true);
                    rows.Add(ToJson(file));
                }
                manifest["files"] = rows;
                // A successful capture represents one complete selected file set, including absence.
                RequireEqual(files, Discover(session, false));
                var manifestBytes = Utf8.GetBytes(manifest.ToString(2));
                if (manifestBytes.Length > MaxManifestBytes)
                    throw new InvalidDataException("Bundle manifest is too large.");
                WriteNew(Path.Combine(session.Bundle, "manifest.json"), manifestBytes, true);
                session.ManifestHash = JsonFiles.ComputeRevision(manifestBytes);
                Sessions.Add(session.Directory, session);
                return manifest;
            }
        }

        internal static JSONObject Restore(string sessionDirectory)
        {
            return Restore(sessionDirectory, false);
        }

        // Only the coordinator may authorize losing disposable fixture outputs on archive I/O failure.
        internal static JSONObject Restore(string sessionDirectory, bool allowArchiveFailure)
        {
            lock (Gate)
            {
                var session = TrustedSession(sessionDirectory);
                var original = ReadBundle(session);
                return Apply(session, original, false, allowArchiveFailure);
            }
        }

        // These pins come from coordinator configuration/journal, not from the bundle being resumed.
        internal static JSONObject Resume(string runtimeRoot, string nativeRoot, string sessionDirectory, string manifestSHA)
        {
            lock (Gate)
            {
                var session = ValidateSession(runtimeRoot, nativeRoot, sessionDirectory);
                if (!string.Equals(sessionDirectory, session.Directory, StringComparison.Ordinal))
                    throw new InvalidDataException("Resume requires an unambiguous canonical session directory.");
                if (!IsSha256(manifestSHA)) throw new InvalidDataException("Expected lowercase manifest SHA-256 is required.");
                session.ManifestHash = manifestSHA;
                Session existing;
                var alreadyTrusted = Sessions.TryGetValue(session.Directory, out existing);
                if (alreadyTrusted && (!string.Equals(existing.Runtime, session.Runtime, StringComparison.Ordinal)
                    || !string.Equals(existing.Native, session.Native, StringComparison.Ordinal)
                    || !string.Equals(existing.Directory, session.Directory, StringComparison.Ordinal)
                    || existing.ManifestHash != session.ManifestHash))
                    throw new InvalidOperationException("Session is already trusted with conflicting roots or manifest hash.");
                var original = ReadBundle(session);
                var current = Discover(session, false);
                // Publish no trust until all original bytes and all scoped paths have passed validation.
                if (!alreadyTrusted) Sessions.Add(session.Directory, session);
                var result = new JSONObject();
                result["verified"] = true;
                result["alreadyTrusted"] = alreadyTrusted;
                result["runtimeRoot"] = session.Runtime;
                result["nativeRoot"] = session.Native;
                result["sessionDirectory"] = session.Directory;
                result["manifestSha256"] = session.ManifestHash;
                result["scopedFiles"] = original.Count;
                result["currentScopedFiles"] = current.Count;
                return result;
            }
        }

        internal static JSONObject PrepareFixture(string sessionDirectory, string planName)
        {
            lock (Gate)
            {
                ValidateRelative(planName);
                if (planName.IndexOf('/') >= 0 || planName.Length > 128)
                    throw new InvalidDataException("Fixture plan name must be one file-name component.");
                var session = TrustedSession(sessionDirectory);
                var original = ReadBundle(session);
                var desired = original.Where(IsConfiguration).ToList();
                var settings = desired.SingleOrDefault(f => Names.Equals(f.Path, "settings.json"));
                var json = settings == null ? new JSONObject() : JsonFiles.ParseObject(Decode(ReadOriginal(session, settings)));
                json["_customPlanFile"] = planName;
                json["_useCustomRebirthPlan"] = true;
                json["_autoRebirth"] = false;
                json["_manageEnergy"] = true;
                json["_manageMagic"] = true;
                json["_manageGear"] = true;
                json["_manageDiggers"] = true;
                json["_customAutoExpEnabled"] = false;
                json["_customAutoMoneyPitEnabled"] = false;
                if (settings != null) desired.Remove(settings);
                var bytes = Utf8.GetBytes(json.ToString(2));
                if (bytes.Length > MaxFileBytes) throw new InvalidDataException("Fixture settings exceed the file limit.");
                desired.Add(new Entry { Area = "runtime", Path = "settings.json", Length = bytes.Length,
                    Hash = JsonFiles.ComputeRevision(bytes), Data = bytes });
                var result = Apply(session, desired, true);
                result["planName"] = planName;
                return result;
            }
        }

        private static bool IsConfiguration(Entry file)
        {
            return file.Area == "runtime" && (ConfigNames.Contains(file.Path, Names)
                || file.Path.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase)
                || file.Path.StartsWith("custom-plans/", StringComparison.OrdinalIgnoreCase));
        }

        private static Session ValidateSession(string runtimeRoot, string nativeRoot, string sessionDirectory)
        {
            var session = new Session { Runtime = FullPath(runtimeRoot), Native = FullPath(nativeRoot), Directory = FullPath(sessionDirectory) };
            if (Under(session.Runtime, session.Native) || Under(session.Native, session.Runtime))
                throw new InvalidDataException("Runtime and native roots must not overlap.");
            var tests = Path.Combine(session.Runtime, "save-safety", "tests");
            if (!Names.Equals(Path.GetDirectoryName(session.Directory), tests))
                throw new InvalidDataException("Session must be a direct child of runtimeRoot/save-safety/tests.");
            ValidateRelative(Path.GetFileName(session.Directory));
            RequireDirectory(session.Runtime);
            RequireDirectory(session.Native);
            CheckPath(session.Directory);
            CheckPath(session.Bundle);
            return session;
        }

        private static Session TrustedSession(string directory)
        {
            Session session;
            if (!Sessions.TryGetValue(FullPath(directory), out session))
                throw new InvalidOperationException("Session was not captured or explicitly resumed by this loaded SaveTestBundle instance.");
            CheckPath(session.Directory);
            return session;
        }

        private static List<Entry> ReadBundle(Session session)
        {
            var manifestBytes = ReadBytes(Path.Combine(session.Bundle, "manifest.json"), MaxManifestBytes);
            if (JsonFiles.ComputeRevision(manifestBytes) != session.ManifestHash)
                throw new InvalidDataException("Immutable bundle manifest changed.");
            var manifest = JsonFiles.ParseObject(Decode(manifestBytes));
            if (!manifest["version"].IsNumber || manifest["version"].AsDouble != 1
                || !StringFieldEquals(manifest, "runtimeRoot", session.Runtime)
                || !StringFieldEquals(manifest, "nativeRoot", session.Native)
                || !StringFieldEquals(manifest, "sessionDirectory", session.Directory)
                || !manifest["files"].IsArray)
                throw new InvalidDataException("Invalid bundle identity or schema.");
            var files = new List<Entry>();
            var keys = new HashSet<string>(Names);
            var counts = new int[2];
            long total = 0;
            foreach (var row in manifest["files"].Children)
            {
                if (!row.IsObject || !row["area"].IsString || !row["path"].IsString
                    || !row["sha256"].IsString || !row["length"].IsNumber)
                    throw new InvalidDataException("Invalid bundle file record.");
                var file = new Entry { Area = row["area"].Value, Path = row["path"].Value,
                    Hash = row["sha256"].Value, Index = files.Count };
                ValidateRelative(file.Path);
                var length = row["length"].AsDouble;
                if (!(InScope(file.Area, file.Path) || file.Area == "runtime" && IsRetiredActionRecord(file.Path)) || file.Path.Count(c => c == '/') > 64
                    || length < 0 || length > MaxFileBytes || length != Math.Floor(length) || !IsSha256(file.Hash)
                    || !keys.Add(file.Key))
                    throw new InvalidDataException("Invalid bundle path, hash, length, or duplicate file.");
                file.Length = (long)length;
                if (++counts[file.Area == "runtime" ? 0 : 1] > MaxFiles || (total += file.Length) > MaxTotalBytes)
                    throw new InvalidDataException("Bundle exceeds file count or total byte limit.");
                files.Add(file);
            }
            foreach (var file in files)
            {
                for (var parent = Path.GetDirectoryName(file.Path.Replace('/', Path.DirectorySeparatorChar));
                    !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                    if (keys.Contains(file.Area + "/" + parent.Replace(Path.DirectorySeparatorChar, '/')))
                        throw new InvalidDataException("Bundle contains conflicting file paths.");
            }
            // Validate every original before any archive or destination write, even for fixture setup.
            foreach (var file in files) ReadOriginal(session, file);
            // Old protected bundles may contain retired journals. Verify their bytes,
            // but do not restore transient authority or include them in new captures.
            return files.Where(file => InScope(file.Area, file.Path)).ToList();
        }

        private static bool StringFieldEquals(JSONObject value, string key, string expected)
        {
            return value[key].IsString && string.Equals(value[key].Value, expected, StringComparison.Ordinal);
        }

        private static JSONObject Apply(Session session, List<Entry> desired, bool fixture, bool allowArchiveFailure = false)
        {
            var current = Discover(session, fixture);
            var wanted = desired.ToDictionary(f => f.Key, Names);
            var existing = current.ToDictionary(f => f.Key, Names);
            var changed = current.Where(f => !wanted.ContainsKey(f.Key) || !Same(f, wanted[f.Key])).ToList();
            var restore = desired.Where(f => !existing.ContainsKey(f.Key) || !Same(f, existing[f.Key])).ToList();
            var remove = current.Where(f => !wanted.ContainsKey(f.Key)).ToList();
            if (desired.Count(f => f.Area == "runtime") > MaxFiles || desired.Count(f => f.Area == "native") > MaxFiles)
                throw new InvalidDataException("Desired files exceed the per-area file count limit.");
            long total = 0;
            foreach (var file in desired)
            {
                if ((total += file.Length) > MaxTotalBytes) throw new InvalidDataException("Desired files exceed total byte limit.");
                var path = LivePath(session, file);
                CheckPath(path);
                if (Directory.Exists(path)) throw new IOException("A directory obstructs an original file: " + path);
                ReadOriginal(session, file);
            }

            var output = Path.Combine(session.Directory, "test-outputs", Guid.NewGuid().ToString("N"));
            var archived = new JSONArray();
            var archiveErrors = new JSONArray();
            var archivedKeys = new HashSet<string>(Names);
            var archiveReady = true;
            void ArchiveError(string path, Exception error)
            {
                var row = new JSONObject();
                row["path"] = path;
                row["error"] = error.GetType().Name;
                row["message"] = error.Message;
                archiveErrors.Add(row);
            }
            try
            {
                CheckPath(output);
                if (Exists(output)) throw new IOException("Archive directory already exists.");
            }
            catch (Exception error) when (allowArchiveFailure && IsArchiveIoError(error))
            {
                archiveReady = false;
                ArchiveError(output, error);
            }
            // Attempt ALL archival copies before changing ANY scoped file. Validation is never waived.
            foreach (var file in changed)
            {
                var bytes = ReadChecked(LivePath(session, file), file.Length, file.Hash);
                if (!archiveReady) continue;
                var path = Path.Combine(output, file.Area, file.Path.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    WriteNew(path, bytes, true);
                    archived.Add(ToJson(file));
                    archivedKeys.Add(file.Key);
                }
                catch (Exception error) when (allowArchiveFailure && IsArchiveIoError(error))
                {
                    ArchiveError(path, error);
                }
            }
            var filesLost = new JSONArray();
            foreach (var file in changed.Where(f => !archivedKeys.Contains(f.Key))) filesLost.Add(ToJson(file));
            var record = new JSONObject();
            record["operation"] = fixture ? "prepareFixture" : "restore";
            record["files"] = archived;
            record["archiveErrors"] = archiveErrors;
            record["filesLost"] = filesLost;
            var manifestWritten = false;
            if (archiveReady)
            {
                var path = Path.Combine(output, "manifest.json");
                try
                {
                    WriteNew(path, Utf8.GetBytes(record.ToString(2)), true);
                    manifestWritten = true;
                }
                catch (Exception error) when (allowArchiveFailure && IsArchiveIoError(error))
                {
                    ArchiveError(path, error);
                }
            }
            RequireEqual(current, Discover(session, fixture));
            foreach (var file in remove)
            {
                var path = LivePath(session, file);
                ReadChecked(path, file.Length, file.Hash);
                File.Delete(path);
            }
            foreach (var file in restore)
                ReplaceFile(LivePath(session, file), ReadOriginal(session, file));
            RequireEqual(desired, Discover(session, fixture));
            var result = new JSONObject();
            result["verified"] = true;
            result["filesRestored"] = restore.Count;
            result["filesDeleted"] = remove.Count;
            result["archivedFiles"] = archived.Count;
            result["archiveDirectory"] = output;
            result["archiveComplete"] = archiveErrors.Count == 0;
            result["archiveManifestWritten"] = manifestWritten;
            result["archiveErrors"] = archiveErrors;
            result["filesLost"] = filesLost;
            result["scopedFiles"] = desired.Count;
            return result;
        }

        private static List<Entry> Discover(Session session, bool fixture)
        {
            var files = new List<Entry>();
            var counts = new int[2];
            var directories = 0;
            long total = 0;
            void Visit(string root, string area, string relative, int depth)
            {
                if (depth > 64 || ++directories > MaxFiles)
                    throw new InvalidDataException("Scoped directory count or depth limit exceeded.");
                var directory = relative.Length == 0 ? root : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                RequireDirectory(directory);
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    var name = Path.GetFileName(path);
                    var child = relative.Length == 0 ? name : relative + "/" + name;
                    if (relative.Length == 0 && area == "runtime" && Trees.Contains(name, Names))
                    {
                        Visit(root, area, child, depth + 1);
                        continue;
                    }
                    if (relative.Length == 0 && (!InScope(area, child) || fixture && Names.Equals(child, "NGUSave.txt")))
                        continue;
                    ValidateRelative(child);
                    CheckPath(path);
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (relative.Length == 0) throw new IOException("Scoped file is a directory: " + path);
                        Visit(root, area, child, depth + 1);
                        continue;
                    }
                    if (++counts[area == "runtime" ? 0 : 1] > MaxFiles)
                        throw new InvalidDataException("Scoped file count limit exceeded.");
                    var length = new FileInfo(path).Length;
                    if (length > MaxFileBytes || (total += length) > MaxTotalBytes)
                        throw new InvalidDataException("Scoped file or total byte limit exceeded.");
                    var bytes = ReadBytes(path, MaxFileBytes);
                    if (bytes.LongLength != length) throw new IOException("Scoped file changed while reading: " + path);
                    files.Add(new Entry { Area = area, Path = child, Length = length, Hash = JsonFiles.ComputeRevision(bytes) });
                }
            }
            Visit(session.Runtime, "runtime", "", 0);
            if (!fixture) Visit(session.Native, "native", "", 0);
            var ordered = files.OrderBy(f => f.Key, Names).ToList();
            if (ordered.Select(f => f.Key).Distinct(Names).Count() != ordered.Count)
                throw new InvalidDataException("Case-ambiguous scoped file paths.");
            return ordered;
        }

        private static bool InScope(string area, string path)
        {
            if (area == "native") return path.IndexOf('/') < 0 && path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            if (area != "runtime") return false;
            if (IsRetiredActionRecord(path)) return false;
            var slash = path.IndexOf('/');
            if (slash >= 0) return Trees.Contains(path.Substring(0, slash), Names);
            return Names.Equals(path, "NGUSave.txt") || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !Names.Equals(path, "runtime.json") && !Names.Equals(path, "NGUSave.json");
        }

        private static bool IsRetiredActionRecord(string path)
        {
            return path.StartsWith("finite-actions-v1/", StringComparison.OrdinalIgnoreCase)
                || Names.Equals(path, "execution-selection.json")
                || path.StartsWith("execution-", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith("-selection.json", StringComparison.OrdinalIgnoreCase)
                || Names.Equals(path, "route-execution-v1.json")
                || Names.Equals(path, "lifecycle-route-commitments-v1.json")
                || Names.Equals(path, "combat-prep-os.json");
        }

        private static bool Same(Entry left, Entry right)
        {
            return left.Length == right.Length && left.Hash == right.Hash;
        }

        private static bool IsArchiveIoError(Exception error)
        {
            return !(error is InvalidDataException) && (error is IOException || error is UnauthorizedAccessException);
        }

        private static bool IsSha256(string hash)
        {
            return hash != null && hash.Length == 64 && hash.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        }

        private static void RequireEqual(List<Entry> expected, List<Entry> actual)
        {
            var index = actual.ToDictionary(f => f.Key, Names);
            if (expected.Count != actual.Count || expected.Any(f => !index.ContainsKey(f.Key) || !Same(f, index[f.Key])))
                throw new IOException("Scoped file set or bytes changed; verification failed.");
        }

        private static JSONObject ToJson(Entry file)
        {
            var row = new JSONObject();
            row["area"] = file.Area;
            row["path"] = file.Path;
            row["length"] = file.Length;
            row["sha256"] = file.Hash;
            return row;
        }

        private static string LivePath(Session session, Entry file)
        {
            ValidateRelative(file.Path);
            if (!InScope(file.Area, file.Path)) throw new InvalidDataException("File is outside the bundle scope.");
            return Path.Combine(file.Area == "runtime" ? session.Runtime : session.Native, file.Path.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string SnapshotPath(Session session, Entry file)
        {
            // No snapshot path is accepted from disk; its ordinal fixes its only possible location.
            return Path.Combine(session.Bundle, "files", file.Index.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".bin");
        }

        private static byte[] ReadOriginal(Session session, Entry file)
        {
            if (file.Data != null) return file.Data;
            return ReadChecked(SnapshotPath(session, file), file.Length, file.Hash);
        }

        private static byte[] ReadChecked(string path, long length, string hash)
        {
            var bytes = ReadBytes(path, MaxFileBytes);
            if (bytes.LongLength != length || JsonFiles.ComputeRevision(bytes) != hash)
                throw new InvalidDataException("File length or SHA-256 verification failed: " + path);
            return bytes;
        }

        private static byte[] ReadBytes(string path, int limit)
        {
            CheckPath(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > limit) throw new InvalidDataException("File exceeds byte limit: " + path);
                var bytes = new byte[(int)stream.Length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0) throw new EndOfStreamException("File changed while reading: " + path);
                    offset += read;
                }
                if (stream.ReadByte() != -1) throw new IOException("File grew while reading: " + path);
                return bytes;
            }
        }

        private static string Decode(byte[] bytes)
        {
            var start = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            return Utf8.GetString(bytes, start, bytes.Length - start);
        }

        private static void WriteNew(string path, byte[] bytes, bool immutable)
        {
            CheckPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            CheckPath(path);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            var readback = ReadBytes(path, Math.Max(MaxFileBytes, MaxManifestBytes));
            if (readback.LongLength != bytes.LongLength || JsonFiles.ComputeRevision(readback) != JsonFiles.ComputeRevision(bytes))
                throw new IOException("Written file failed read-back verification: " + path);
            if (immutable) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }

        private static void ReplaceFile(string path, byte[] bytes)
        {
            CheckPath(path);
            var temporary = Path.Combine(Path.GetDirectoryName(path), ".bundle-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                WriteNew(temporary, bytes, false);
                CheckPath(path);
                if (Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                ReadChecked(path, bytes.Length, JsonFiles.ComputeRevision(bytes));
            }
            finally
            {
                if (Exists(temporary))
                {
                    CheckPath(temporary);
                    File.Delete(temporary);
                }
            }
        }

        private static string FullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)
                || path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\.\\", StringComparison.Ordinal))
                throw new InvalidDataException("An absolute non-device path is required.");
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (!Names.Equals(Path.GetPathRoot(path), root)
                || full.StartsWith("\\\\?\\", StringComparison.Ordinal) || full.StartsWith("\\\\.\\", StringComparison.Ordinal))
                throw new InvalidDataException("A fully qualified non-device path is required.");
            while (full.Length > root.Length && (full.EndsWith("\\", StringComparison.Ordinal) || full.EndsWith("/", StringComparison.Ordinal)))
                full = full.Substring(0, full.Length - 1);
            var relative = full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
            ValidateRelative(relative);
            return full;
        }

        private static bool Under(string path, string root)
        {
            return Names.Equals(path, root) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateRelative(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 1024)
                throw new InvalidDataException("Invalid relative file path.");
            foreach (var part in path.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal)
                    || part.EndsWith(" ", StringComparison.Ordinal) || part.Any(c => c < ' ' || "\\:<>\"|?*".IndexOf(c) >= 0))
                    throw new InvalidDataException("Unsafe file path component: " + part);
                var stem = part.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL"
                    || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && stem[3] >= '0' && stem[3] <= '9')
                    throw new InvalidDataException("Device file names are not allowed.");
            }
        }

        private static bool Exists(string path)
        {
            try { File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        private static void RequireDirectory(string path)
        {
            CheckPath(path);
            if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
                throw new IOException("Expected a directory: " + path);
        }

        private static void CheckPath(string path)
        {
            var components = new Stack<string>();
            for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                components.Push(current);
            while (components.Count != 0)
            {
                var current = components.Pop();
                FileAttributes attributes;
                try { attributes = File.GetAttributes(current); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse points are not allowed: " + current);
                if (components.Count != 0 && (attributes & FileAttributes.Directory) == 0)
                    throw new IOException("A file obstructs a directory: " + current);
            }
        }
    }
}
