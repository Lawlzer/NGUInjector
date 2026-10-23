// Run the net48 Fixtures/FixtureGenerator.csproj first, targeting a new temporary directory.
// Arguments: <synthetic-fixture-directory> [optional-real-save-to-read-only]
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NGUInjector;

internal static class Program
{
    private static int assertions;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1 || args.Length > 2) throw new ArgumentException("Arguments: <synthetic-fixture-directory> [optional-real-save-to-read-only]");
            string fixtures = Path.GetFullPath(args[0]);
            int accepted = 0, rejected = 0;
            foreach (string path in Directory.EnumerateFiles(fixtures, "*.ok.txt", SearchOption.TopDirectoryOnly))
            {
                var bytes = File.ReadAllBytes(path);
                var identity = SaveSafetyCatalog.FromBytes(bytes, path, 1260);
                Equal(identity.Sha256, Hash(bytes), path + " capture hash");
                Equal(SaveSafetyCatalog.Read(path, 1260).Sha256, identity.Sha256, path + " read hash");
                accepted++;
            }
            foreach (string path in Directory.EnumerateFiles(fixtures, "*.bad.txt", SearchOption.TopDirectoryOnly))
            {
                Invalid(() => SaveSafetyCatalog.FromBytes(File.ReadAllBytes(path), path, 1260), Path.GetFileName(path) + " capture");
                Invalid(() => SaveSafetyCatalog.Read(path, 1260), Path.GetFileName(path) + " read");
                rejected++;
            }
            Equal(accepted, 7, "all accepted fixture scenarios were discovered");
            Equal(rejected, 82, "all rejected fixture scenarios were discovered");
            MetadataChecks(fixtures);
            SelectionChecks(fixtures);
            BoundsAndSnapshotChecks(fixtures);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                Assert(name != "Assembly-CSharp" && name != "DeliberatelyMissing.ModAssembly", "serialized assemblies were not loaded");
            }
            if (args.Length == 2)
            {
                // Only Read touches the optional real path. No copying, serialization, or native getters.
                var real = SaveSafetyCatalog.Read(args[1], 1260);
                Console.WriteLine("Real fixture: " + real.Path);
                Console.WriteLine("SHA-256: " + real.Sha256);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Metadata: version={0}; lastTime={1}; totalPlaytimeSeconds={2:R}; rebirth={3}; rebirthSeconds={4:R}; playerName={5}",
                    real.Version, real.LastTime, real.TotalPlaytimeSeconds, real.Rebirth, real.RebirthSeconds, real.PlayerName));
            }
            Console.WriteLine("PASS: " + assertions + " assertions; " + accepted + " accepted and " + rejected + " rejected synthetic fixtures.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void MetadataChecks(string fixtures)
    {
        string path = Path.Combine(fixtures, "valid.ok.txt");
        byte[] bytes = File.ReadAllBytes(path);
        var identity = SaveSafetyCatalog.FromBytes(bytes, path, 1260);
        Equal(identity.Path, Path.GetFullPath(path), "full path");
        Equal(identity.Version, 1260, "version");
        Equal(identity.LastTime, 1720000000, "lastTime");
        Equal(identity.TotalPlaytimeSeconds, 1000.25, "fractional total playtime");
        Equal(identity.Rebirth, 7L, "int64 rebirth");
        Equal(identity.RebirthSeconds, 60.5, "fractional rebirth playtime");
        Equal(identity.PlayerName, "Synthetic Player", "player name");
        Equal(identity.Sha256.Length, 64, "hash length");
        Equal(identity.Sha256, identity.Sha256.ToLowerInvariant(), "hash case");
        bytes[0] ^= 1;
        Equal(identity.Sha256, Hash(File.ReadAllBytes(path)), "caller buffer does not mutate identity");
        Invalid(() => SaveSafetyCatalog.Read(path, 1259), "caller maximum version");
        Equal(SaveSafetyCatalog.Read(Path.Combine(fixtures, "minimum-version.ok.txt"), 361).Version, 361, "inclusive minimum and maximum");
        Throws<ArgumentOutOfRangeException>(() => SaveSafetyCatalog.Read(path, 360), "maximum below minimum");
        Throws<ArgumentOutOfRangeException>(() => SaveSafetyCatalog.Read(path, 1261), "maximum beyond supported native version");
        Throws<ArgumentNullException>(() => SaveSafetyCatalog.FromBytes(null, path, 1260), "null capture");
        Throws<ArgumentException>(() => SaveSafetyCatalog.FromBytes(bytes, null, 1260), "missing identity path");
        var large = SaveSafetyCatalog.Read(Path.Combine(fixtures, "large-finite-clocks.ok.txt"), 1260);
        Equal(large.TotalPlaytimeSeconds, double.MaxValue, "large finite double retained");
        Equal(large.Rebirth, long.MaxValue, "full int64 rebirth retained");

        byte[] valid = File.ReadAllBytes(path);
        for (int length = 0; length < valid.Length; length++)
        {
            var prefix = new byte[length];
            Buffer.BlockCopy(valid, 0, prefix, 0, length);
            Invalid(() => SaveSafetyCatalog.FromBytes(prefix, path, 1260), "text truncation at byte " + length);
        }
        var random = new Random(1260);
        for (int i = 0; i < 256; i++)
        {
            var malformed = new byte[random.Next(1, 1024)];
            random.NextBytes(malformed);
            Invalid(() => SaveSafetyCatalog.FromBytes(malformed, path, 1260), "random malformed input " + i);
        }
    }

    private static void SelectionChecks(string fixtures)
    {
        var original = SaveSafetyCatalog.Read(Path.Combine(fixtures, "valid.ok.txt"), 1260);
        string aPath = Path.Combine(fixtures, "a.txt"), zPath = Path.Combine(fixtures, "z.txt");
        var a = Identity(aPath, original.Sha256, 1000.25);
        var z = new SaveIdentity(zPath, new string('f', 64), 1000.25, long.MaxValue, 1d, 361, int.MaxValue, "Other Player");
        var lower = Identity(Path.Combine(fixtures, "lower.txt"), new string('e', 64), 1000.249999, long.MaxValue, int.MaxValue);
        var higher = Identity(Path.Combine(fixtures, "higher.txt"), new string('d', 64), 1000.250001, 0L, 0);
        bool ambiguous;
        Equal(SaveSafetyCatalog.SelectLatest(new[] { lower, a, higher }, lower.Path, out ambiguous), higher, "total playtime outranks other metadata and fresh path");
        Assert(!ambiguous, "unique maximum is unambiguous");
        Equal(SaveSafetyCatalog.SelectLatest(new[] { a, z }, null, out ambiguous), a, "deterministic lexical tie");
        Assert(ambiguous, "different hashes at maximum are ambiguous");
        Equal(SaveSafetyCatalog.SelectLatest(new[] { z, a }, zPath.ToUpperInvariant(), out ambiguous), z, "fresh tie path is case-insensitive");
        Assert(ambiguous, "fresh preference does not hide ambiguity");
        foreach (var order in new[] { new[] { a, z, lower }, new[] { z, lower, a }, new[] { lower, a, z }, new[] { lower, z, a } })
        {
            Equal(SaveSafetyCatalog.SelectLatest(order, null, out ambiguous), a, "ordering independent of enumeration");
            Assert(ambiguous, "ambiguity independent of enumeration");
        }
        Equal(SaveSafetyCatalog.SelectLatest(new[] { a, z, higher }, zPath, out ambiguous), higher, "new maximum resets tied lower candidates");
        Assert(!ambiguous, "lower ambiguity is irrelevant");
        Equal(SaveSafetyCatalog.SelectLatest(new[] { higher, a, z }, zPath, out ambiguous), higher, "lower ties after maximum");
        Assert(!ambiguous, "lower ties do not introduce ambiguity");
        var copy = Identity(zPath, a.Sha256, a.TotalPlaytimeSeconds);
        Equal(SaveSafetyCatalog.SelectLatest(new[] { a, copy }, zPath, out ambiguous), copy, "same-hash fresh copy");
        Assert(!ambiguous, "identical bytes at different paths are not ambiguous");
        Equal(SaveSafetyCatalog.SelectLatest(new[] { copy, a }, null, out ambiguous).Sha256, original.Sha256, "selection preserves original hash");
        var samePath = Identity(aPath, new string('0', 64), a.TotalPlaytimeSeconds);
        Equal(SaveSafetyCatalog.SelectLatest(new[] { a, samePath }, null, out ambiguous), samePath, "same-path hash fallback");
        Equal(SaveSafetyCatalog.SelectLatest(new[] { samePath, a }, null, out ambiguous), samePath, "same-path hash fallback reversed");
        Assert(ambiguous, "different snapshots at the same path are ambiguous");
        var upperPath = Identity(aPath.ToUpperInvariant(), a.Sha256, a.TotalPlaytimeSeconds);
        var firstCase = SaveSafetyCatalog.SelectLatest(new[] { a, upperPath }, null, out ambiguous);
        Equal(SaveSafetyCatalog.SelectLatest(new[] { upperPath, a }, null, out ambiguous), firstCase, "path-case fallback is deterministic");
        Equal(SaveSafetyCatalog.SelectLatest(new SaveIdentity[0], null, out ambiguous), null, "empty selection");
        Assert(!ambiguous, "empty selection is not ambiguous");
        Throws<ArgumentNullException>(() => SaveSafetyCatalog.SelectLatest(null, null, out ambiguous), "null selection");
        Throws<ArgumentException>(() => SaveSafetyCatalog.SelectLatest(new SaveIdentity[] { null }, null, out ambiguous), "null candidate");
        var limit = new SaveIdentity[SaveSafetyCatalog.MaxCandidates];
        for (int i = 0; i < limit.Length; i++) limit[i] = a;
        Equal(SaveSafetyCatalog.SelectLatest(limit, null, out ambiguous), a, "inclusive candidate bound");
        Invalid(() => SaveSafetyCatalog.SelectLatest(Repeat(a), null, out ambiguous), "infinite candidate stream is bounded");
    }

    private static void BoundsAndSnapshotChecks(string fixtures)
    {
        string work = Path.Combine(fixtures, "checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            byte[] original = File.ReadAllBytes(Path.Combine(fixtures, "valid.ok.txt"));
            var padded = new byte[SaveSafetyCatalog.MaxSaveBytes];
            Buffer.BlockCopy(original, 0, padded, 0, original.Length);
            for (int i = original.Length; i < padded.Length; i++) padded[i] = (byte)' ';
            string bounded = Path.Combine(work, "at-limit.txt");
            File.WriteAllBytes(bounded, padded);
            var atLimit = SaveSafetyCatalog.FromBytes(padded, bounded, 1260);
            Equal(atLimit.Sha256, Hash(padded), "inclusive byte bound and exact whitespace hash");
            Equal(SaveSafetyCatalog.Read(bounded, 1260).Sha256, atLimit.Sha256, "inclusive file bound");
            Invalid(() => SaveSafetyCatalog.FromBytes(new byte[SaveSafetyCatalog.MaxSaveBytes + 1], bounded, 1260), "oversized capture");
            using (var file = new FileStream(bounded, FileMode.Open, FileAccess.Write, FileShare.None)) file.SetLength(SaveSafetyCatalog.MaxSaveBytes + 1L);
            Invalid(() => SaveSafetyCatalog.Read(bounded, 1260), "oversized file rejected before allocation");

            string older = Path.Combine(work, "older.txt"), newer = Path.Combine(work, "newer.txt");
            File.WriteAllBytes(older, original);
            File.WriteAllBytes(newer, File.ReadAllBytes(Path.Combine(fixtures, "zero-clocks.ok.txt")));
            File.SetLastWriteTimeUtc(older, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newer, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var oldIdentity = SaveSafetyCatalog.Read(older, 1260);
            var newIdentity = SaveSafetyCatalog.Read(newer, 1260);
            bool ambiguous;
            Equal(SaveSafetyCatalog.SelectLatest(new[] { newIdentity, oldIdentity }, newer, out ambiguous).Path, older, "mtime and freshness cannot outrank playtime");
            File.SetLastWriteTimeUtc(older, new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Equal(SaveSafetyCatalog.Read(older, 1260).Sha256, oldIdentity.Sha256, "mtime does not change identity hash");
            Equal(SaveSafetyCatalog.SelectLatest(new[] { oldIdentity, newIdentity }, null, out ambiguous).Path, older, "mtime changes do not change ranking");
            File.WriteAllBytes(newer, original);
            File.SetLastWriteTimeUtc(newer, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var tiedCopy = SaveSafetyCatalog.Read(newer, 1260);
            Equal(SaveSafetyCatalog.SelectLatest(new[] { oldIdentity, tiedCopy }, null, out ambiguous).Path, newer, "mtime cannot break a playtime tie");
            Assert(!ambiguous, "tied copies with different mtimes preserve identical byte identity");
            File.WriteAllBytes(older, File.ReadAllBytes(Path.Combine(fixtures, "zero-clocks.ok.txt")));
            Assert(SaveSafetyCatalog.Read(older, 1260).Sha256 != oldIdentity.Sha256, "replacement at same path is detected by hash");
            Equal(oldIdentity.Sha256, Hash(original), "retained identity preserves prior snapshot hash");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using (var writer = new FileStream(older, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                    Throws<IOException>(() => SaveSafetyCatalog.Read(older, 1260), "active writer prevents inconsistent capture");
            }
        }
        finally { Directory.Delete(work, true); }
    }

    private static SaveIdentity Identity(string path, string hash, double total, long rebirth = 7L, int lastTime = 1720000000)
    {
        return new SaveIdentity(Path.GetFullPath(path), hash, total, rebirth, 1d, 1260, lastTime, "Synthetic Player");
    }

    private static IEnumerable<SaveIdentity> Repeat(SaveIdentity identity) { while (true) yield return identity; }

    private static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static void Invalid(Action action, string name) { Throws<InvalidDataException>(action, name); }

    private static void Throws<T>(Action action, string name) where T : Exception
    {
        assertions++;
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + name);
    }

    private static void Equal<T>(T actual, T expected, string name)
    {
        Assert(EqualityComparer<T>.Default.Equals(actual, expected), name + "; expected=" + expected + "; actual=" + actual);
    }

    private static void Assert(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(name);
    }
}
