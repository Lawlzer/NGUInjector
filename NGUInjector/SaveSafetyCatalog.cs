using System;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace NGUInjector
{
    internal sealed class SaveIdentity
    {
        internal string Path { get; private set; }
        internal string Sha256 { get; private set; }
        internal double TotalPlaytimeSeconds { get; private set; }
        internal long Rebirth { get; private set; }
        internal double RebirthSeconds { get; private set; }
        internal int Version { get; private set; }
        internal int LastTime { get; private set; }
        internal string PlayerName { get; private set; }

        internal SaveIdentity(string path, string sha256, double totalPlaytimeSeconds, long rebirth,
            double rebirthSeconds, int version, int lastTime, string playerName)
        {
            Path = path;
            Sha256 = sha256;
            TotalPlaytimeSeconds = totalPlaytimeSeconds;
            Rebirth = rebirth;
            RebirthSeconds = rebirthSeconds;
            Version = version;
            LastTime = lastTime;
            PlayerName = playerName;
        }
    }

    // Metadata only: NRBF records never become game objects. MD5 is native integrity, not authenticity.
    internal static class SaveSafetyCatalog
    {
        static SaveSafetyCatalog() { SaveSafetyDependencies.Register(); }
        internal const int MaxSaveBytes = 32 * 1024 * 1024;
        internal const int MinimumVersion = 361;
        internal const int NativeVersion = 1260;
        internal const int MaxCandidates = 4096;

        internal static SaveIdentity Read(string path, int maxVersion)
        {
            return ReadSnapshot(path, maxVersion, out _);
        }

        internal static SaveIdentity ReadSnapshot(string path, int maxVersion, out byte[] bytes)
        {
            // Deny writers/replacement while capturing; metadata and hash use this one snapshot.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long length = stream.Length;
                if (length <= 0 || length > MaxSaveBytes) throw new InvalidDataException("Save size is outside the safety limit.");
                bytes = new byte[(int)length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = stream.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0) throw new InvalidDataException("Save was truncated during capture.");
                    offset += count;
                }
                if (stream.ReadByte() != -1 || stream.Length != length)
                    throw new InvalidDataException("Save changed size during capture.");
                return FromBytes(bytes, path, maxVersion);
            }
        }

        internal static SaveIdentity FromBytes(byte[] bytes, string path, int maxVersion)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A save path is required.", nameof(path));
            if (maxVersion < MinimumVersion || maxVersion > NativeVersion) throw new ArgumentOutOfRangeException(nameof(maxVersion));
            if (bytes.Length == 0 || bytes.Length > MaxSaveBytes) throw new InvalidDataException("Save size is outside the safety limit.");
            string fullPath = System.IO.Path.GetFullPath(path);
            // Capture callers can retain their buffer; do not hash a different view from the one decoded.
            bytes = (byte[])bytes.Clone();
            try
            {
                var outer = Decode(Convert.FromBase64String(new UTF8Encoding(false, true).GetString(bytes)), "SaveData");
                string encodedPlayer = Field<string>(outer, "playerData");
                string encodedChecksum = Field<string>(outer, "checksum");
                if (encodedChecksum.Length != 24) throw new InvalidDataException("Invalid native checksum length.");
                byte[] checksum = Convert.FromBase64String(encodedChecksum);
                byte[] player = Convert.FromBase64String(encodedPlayer);
                using (var md5 = MD5.Create())
                {
                    byte[] actual = md5.ComputeHash(player);
                    if (checksum.Length != actual.Length) throw new InvalidDataException("Invalid native checksum length.");
                    int difference = 0;
                    for (int i = 0; i < actual.Length; i++) difference |= checksum[i] ^ actual[i];
                    if (difference != 0) throw new InvalidDataException("Native save checksum mismatch.");
                }

                // jshepler's ModPlayerData serializes as PlayerData with extra fields. No suffix/type fallback.
                var root = Decode(player, "PlayerData");
                int version = Field<int>(root, "version");
                if (version < MinimumVersion || version > maxVersion)
                    throw new InvalidDataException("Unsupported native save version.");
                int lastTime = Field<int>(root, "lastTime");
                double total = Clock(root, "totalPlaytime");
                double rebirthTime = Clock(root, "rebirthTime");
                var stats = Field<ClassRecord>(root, "stats");
                RequireType(stats, "Stats");
                long rebirth = Field<long>(stats, "rebirthNumber");
                string playerName = Field<string>(root, "playerName");
                if (lastTime < 0 || rebirth < 0 || rebirthTime > total)
                    throw new InvalidDataException("Invalid native save clocks or rebirth count.");
                using (var sha256 = SHA256.Create())
                    return new SaveIdentity(fullPath, BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(),
                        total, rebirth, rebirthTime, version, lastTime, playerName);
            }
            catch (Exception ex) when (ex is SerializationException || ex is FormatException || ex is InvalidCastException
                || ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException
                || ex is EndOfStreamException || ex is KeyNotFoundException || ex is OverflowException)
            {
                throw new InvalidDataException("Invalid native save encoding or record data.", ex);
            }
        }

        private static ClassRecord Decode(byte[] bytes, string type)
        {
            using (var stream = new MemoryStream(bytes, false))
            {
                var record = NrbfDecoder.DecodeClassRecord(stream, leaveOpen: true);
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing native save record data.");
                RequireType(record, type);
                return record;
            }
        }

        private static void RequireType(ClassRecord record, string type)
        {
            if (record.TypeName.FullName != type || record.TypeName.AssemblyName == null
                || record.TypeName.AssemblyName.Name != "Assembly-CSharp")
                throw new InvalidDataException("Unexpected native save record type; expected " + type + ".");
        }

        private static T Field<T>(ClassRecord record, string name)
        {
            if (!record.HasMember(name)) throw new InvalidDataException("Missing native save field: " + name + ".");
            // Typed NRBF primitive getters return default(T) for null, so check the raw value instead.
            object value = record.GetRawValue(name);
            if (!(value is T)) throw new InvalidDataException("Invalid native save field type: " + name + ".");
            return (T)value;
        }

        private static double Clock(ClassRecord root, string name)
        {
            var record = Field<ClassRecord>(root, name);
            RequireType(record, "PlayerTime");
            double seconds = Field<double>(record, "totalseconds");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new InvalidDataException("Invalid native save clock: " + name + ".");
            return seconds;
        }

        // Discovery belongs to the caller: scan only known top directories, nonrecursively and incrementally.
        // A tied playtime with different byte hashes remains ambiguous even when a preferred path wins.
        internal static SaveIdentity SelectLatest(IEnumerable<SaveIdentity> candidates, string freshPath, out bool ambiguous)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            string preferred = freshPath == null ? null : System.IO.Path.GetFullPath(freshPath);
            SaveIdentity best = null;
            ambiguous = false;
            int count = 0;
            foreach (var candidate in candidates)
            {
                if (++count > MaxCandidates) throw new InvalidDataException("Save candidate limit exceeded.");
                if (candidate == null) throw new ArgumentException("A save candidate is null.", nameof(candidates));
                if (best == null || candidate.TotalPlaytimeSeconds > best.TotalPlaytimeSeconds)
                {
                    best = candidate;
                    ambiguous = false;
                }
                else if (candidate.TotalPlaytimeSeconds == best.TotalPlaytimeSeconds)
                {
                    if (!string.Equals(candidate.Sha256, best.Sha256, StringComparison.Ordinal)) ambiguous = true;
                    bool candidateFresh = string.Equals(candidate.Path, preferred, StringComparison.OrdinalIgnoreCase);
                    bool bestFresh = string.Equals(best.Path, preferred, StringComparison.OrdinalIgnoreCase);
                    int order = StringComparer.OrdinalIgnoreCase.Compare(candidate.Path, best.Path);
                    if (order == 0) order = StringComparer.Ordinal.Compare(candidate.Path, best.Path);
                    if (order == 0) order = StringComparer.Ordinal.Compare(candidate.Sha256, best.Sha256);
                    if (candidateFresh && !bestFresh || candidateFresh == bestFresh && order < 0) best = candidate;
                }
            }
            return best;
        }
    }

    internal static class SaveSafetyDependencies
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        private static bool _registered;
        internal static void Register()
        {
            lock (Sync)
            {
                if (_registered) return;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                _registered = true;
            }
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var owner = typeof(SaveSafetyDependencies).Assembly;
            var prefix = "NGUInjector.Dependencies.";
            var request = new AssemblyName(args.Name);
            var requester = args.RequestingAssembly;
            if (requester == null || requester != owner && !owner.GetManifestResourceNames()
                .Contains(prefix + requester.GetName().Name + ".dll", StringComparer.Ordinal)) return null;
            // SMI loads the plugin from bytes with no filename. Resolve only our
            // embedded, pinned dependencies; never probe a writable runtime folder.
            lock (Sync)
            {
                if (Loaded.TryGetValue(args.Name, out var existing)) return existing;
                using (var resource = owner.GetManifestResourceStream(prefix + request.Name + ".dll"))
                {
                    if (resource == null) return null;
                    using (var bytes = new MemoryStream())
                    {
                        resource.CopyTo(bytes);
                        var dependency = Assembly.Load(bytes.ToArray());
                        var resolved = dependency.GetName();
                        // CLR may unify the framework tuple facade with its installed
                        // strong-named facade. Do not reject that runtime binding policy.
                        var frameworkTuple = request.Name == "System.ValueTuple" && resolved.Name == request.Name
                            && resolved.GetPublicKeyToken().SequenceEqual(request.GetPublicKeyToken());
                        if (!frameworkTuple && !string.Equals(dependency.FullName, request.FullName, StringComparison.Ordinal))
                            throw new FileLoadException("Embedded save-safety dependency identity mismatch: " + request.FullName);
                        Loaded[args.Name] = dependency;
                        return dependency;
                    }
                }
            }
        }
    }
}
