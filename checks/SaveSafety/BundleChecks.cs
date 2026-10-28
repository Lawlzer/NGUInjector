// Offline only: real, unique temporary directories; no game, codecs, cloud, or native APIs.
// dotnet run --project checks/SaveSafety/BundleChecks.csproj --configuration Release --no-restore
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using NGUInjector;
using SimpleJSON;

internal static class BundleChecks
{
    private static int cases, assertions, failures, skipped;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    private static int Main(string[] args)
    {
        if (args.Length != 0) return ResumeChild(args);
        Run("capture includes the complete exact file scope", CaptureScope);
        Run("restore archives changed/new files and restores exact original bytes and absence", RoundTrip);
        Run("empty capture tracks absent root, recursive, quicksave, and native files", EmptyOriginal);
        Run("same length and timestamp changes are detected by hash", SameStamp);
        Run("repeated restoration is idempotent with unique archives", RepeatRestore);
        Run("deleted recursive trees are recreated from original bytes", MissingTrees);
        Run("returned manifest is not a mutable trust anchor", ReturnedManifest);
        Run("fixture starts from original config, clears scoped progress, and leaves retired records inert", Fixture);
        Run("fixture does not read locked live native saves or quicksave", FixtureLockedSaves);
        Run("fixture can create disposable settings and restore their original absence", FixtureAbsentSettings);
        Run("malformed original settings prevent any fixture writes", FixtureBadSettings);
        foreach (var locked in new[] { false, true })
        {
            var captured = locked;
            Run("retired malformed records are ignored during capture, fixture setup, and restore: " + (locked ? "locked" : "read-only"),
                s => RetiredRecords(s, captured));
        }
        Run("legacy pinned bundle restores originals without reimporting retired journals", s => LegacyBundleRestore(s, false));
        Run("legacy pinned bundle restores originals despite authorized archive failure", s => LegacyBundleRestore(s, true));
        Run("legacy pinned bundle prepares config without reimporting retired journals", LegacyBundleFixture);
        foreach (var kind in new[] { "corrupt", "missing", "oversized" })
        {
            var captured = kind;
            Run("legacy retired snapshot integrity is checked before any writes: " + kind, s => LegacyBundleBadSnapshot(s, captured));
        }
        foreach (var name in new[] { "", "../x", "a/b", "a\\b", "C:x", "CON", "NUL.json", "x.", "x ", "x\n", new string('a', 129) })
        {
            var captured = name;
            Run("unsafe fixture plan name is rejected: " + name.Replace("\n", "\\n"), s => BadPlanName(s, captured));
        }
        foreach (var field in new[] { "runtimeRoot", "nativeRoot", "sessionDirectory", "version", "hash", "length", "area", "duplicate" })
        {
            var captured = field;
            Run("tampered manifest is rejected before writes: " + field, s => TamperedManifest(s, captured, null));
        }
        foreach (var path in new[] { "../victim.json", "/victim.json", "C:/victim.json", "profiles/../../victim.json",
            "profiles\\..\\victim.json", "settings.json:stream", "profiles//x", "profiles/./x", "profiles/CON.txt", "runtime.json", "commands/claimed.json",
            "finite-actions-v1/../victim.bin", "finite-actions-v1/nested/../../victim.bin", "finite-actions-v1/CON.bin" })
        {
            var captured = path;
            Run("untrusted manifest path is rejected before writes: " + path, s => TamperedManifest(s, "path", captured));
        }
        Run("copied disk-only session cannot select roots", UntrustedSession);
        Run("cold child resumes explicit pins and restores original bytes and absence", ResumeRoundTrip);
        Run("cold resume is read-only and idempotent", ResumeReadOnly);
        Run("cold resumed session can prepare fresh fixture config", ResumeFixture);
        Run("existing session refuses conflicting pins without losing original trust", ResumeConflicts);
        foreach (var pin in new[] { "", "not-a-hash", new string('0', 64), new string('A', 64), new string('g', 64), new string('a', 63) })
        {
            var captured = pin;
            Run("cold resume rejects wrong or malformed manifest pin: " + pin, s => ResumeWrongPin(s, captured));
        }
        foreach (var identity in new[] { "runtime", "native", "swapped", "copied-session", "nested-session", "outside-session", "parent-session",
            "dot-session", "trailing-separator", "missing-native" })
        {
            var captured = identity;
            Run("cold resume rejects wrong or ambiguous identity: " + identity, s => ResumeWrongIdentity(s, captured));
        }
        foreach (var kind in new[] { "corrupt", "missing", "oversized" })
        {
            var captured = kind;
            Run("cold resume rejects invalid original snapshot: " + kind, s => ResumeBadSnapshot(s, captured));
        }
        foreach (var kind in new[] { "file", "files", "total", "depth" })
        {
            var captured = kind;
            Run("cold resume validates pinned manifest bounds: " + kind, s => ResumeBounds(s, captured));
        }
        foreach (var location in new[] { "session", "bundle", "manifest", "snapshot", "profiles", "native-file" })
        {
            var captured = location;
            Run("cold resume rejects reparse points before registering trust: " + location, s => ResumeReparse(s, captured));
        }
        Run("bundle cannot be recaptured or overwrite existing data", Recapture);
        Run("corrupt late snapshot blocks all archival and destination writes", CorruptSnapshot);
        Run("missing snapshot blocks all writes", MissingSnapshot);
        Run("archive obstruction leaves every current file untouched", ArchiveBlocked);
        Run("authorized restoration reports disposable files lost to archive obstruction", ArchiveFailureAuthorized);
        Run("authorized restoration reports archive write permission failures", s => ArchiveWriteDenied(s, false));
        Run("archive-manifest failure does not misreport verified byte copies as lost", s => ArchiveWriteDenied(s, true));
        Run("authorized restoration with a healthy archive loses no outputs", ArchiveOptionalHealthy);
        Run("archive metadata failure with no changed files reports no data loss", ArchiveNoChanges);
        Run("later locked archival source cannot cause earlier destination mutation", ArchiveReadFailure);
        Run("all current outputs are archived before the first destination write", DestinationWriteFailure);
        Run("original path obstruction is rejected before writes", DestinationObstruction);
        Run("missing native root fails capture without writes", MissingNative);
        Run("overlapping roots and out-of-tree sessions are rejected", InvalidRoots);
        foreach (var area in new[] { "runtime", "native" })
        {
            var captured = area;
            Run("4097 files in one area fail capture before writes: " + area, s => CountLimit(s, captured));
            Run("oversized selected file fails capture before writes: " + area, s => FileLimit(s, captured));
            Run("oversized current file fails restore before writes: " + area, s => CurrentFileLimit(s, captured));
        }
        Run("exact 32 MiB file round-trips", ExactFileLimit);
        Run("4096 files per area are accepted independently", ExactCountLimit);
        Run("combined total over 512 MiB fails before writes", TotalLimit);
        Run("deep scoped directory traversal is bounded", DepthLimit);
        foreach (var location in new[] { "runtime-root", "native-root", "session", "profiles", "nested", "directory-symlink",
            "capture-file", "capture-native-file", "archive", "bundle", "manifest", "snapshot", "live-file", "native-file" })
        {
            var captured = location;
            Run("reparse point rejected without touching its target: " + location, s => Reparse(s, captured));
        }
        Run("out-of-scope directory links remain untouched", ExcludedLink);
        Console.WriteLine((failures == 0 ? "PASS" : "FAIL") + ": " + cases + " bundle cases; " + assertions
            + " assertions; " + failures + " failed; " + skipped + " environment skips.");
        return failures == 0 ? 0 : 1;
    }

    private static void CaptureScope(Scenario s)
    {
        s.Seed();
        using (JsonFiles.AcquireInjectorLease(s.Runtime, 0))
        {
            var manifest = s.Capture();
            var disk = JsonFiles.ParseObject(File.ReadAllText(s.Manifest));
            Equal(manifest.ToString(), disk.ToString(), "returned manifest equals immutable disk manifest");
            Equal(disk["runtimeRoot"].Value, s.Runtime, "trusted runtime identity");
            Equal(disk["nativeRoot"].Value, s.Native, "trusted native identity");
            Equal(disk["sessionDirectory"].Value, s.Session, "session identity");
            var expected = s.Original.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            Equal(string.Join("|", disk["files"].Children.Select(f => f["area"].Value + "/" + f["path"].Value)),
                string.Join("|", expected), "complete selected set only");
            var index = 0;
            foreach (var file in disk["files"].Children)
            {
                var key = file["area"].Value + "/" + file["path"].Value;
                var bytes = File.ReadAllBytes(Path.Combine(s.Session, "bundle", "files", (index++).ToString("D6") + ".bin"));
                Bytes(bytes, s.Original[key], "exact immutable snapshot " + key);
                Equal(file["sha256"].Value, Hash(bytes), "independent readback hash " + key);
                Equal((long)file["length"].AsDouble, bytes.LongLength, "length " + key);
                Assert(ReadOnly(s.Snapshot(index - 1)), "snapshot is read-only");
            }
            Assert(ReadOnly(s.Manifest), "manifest is read-only");
            Equal(File.ReadAllText(Path.Combine(s.Runtime, "probe.autorun")), "original-control", "startup marker not consumed");
        }
    }

    private static void RoundTrip(Scenario s)
    {
        s.Seed();
        using (JsonFiles.AcquireInjectorLease(s.Runtime, 0))
        {
            s.Capture();
            var bundleBefore = TreeBytes(Path.Combine(s.Session, "bundle"));
            var excluded = s.Excluded.Select(p => s.Put(p, "current-control")).ToArray();
            var changed = new Dictionary<string, byte[]>();
            foreach (var key in new[] { "runtime/settings.json", "runtime/NGUSave.txt", "runtime/profiles/deep/opaque.bin",
                "runtime/stage-progress-x.json", "native/save.txt" })
                changed[key] = s.PutKey(key, "fixture-" + key);
            File.Delete(s.Live("runtime/custom-plans/default.json"));
            File.Delete(s.Live("native/AutoSave.TXT"));
            foreach (var key in new[] { "runtime/new-execution.json", "runtime/profiles/new/no-extension", "native/fixture-auto.txt" })
                changed[key] = s.PutKey(key, "new-" + key);
            var report = SaveTestBundle.Restore(s.Session);
            Assert(report["verified"].AsBool, "restore is verified");
            Equal(report["filesRestored"].AsInt, 7, "five changed and two deleted originals restored");
            Equal(report["filesDeleted"].AsInt, 3, "three newly created files removed");
            Equal(report["archivedFiles"].AsInt, changed.Count, "all changed and new current files archived");
            Assert(report["archiveComplete"].AsBool && report["archiveManifestWritten"].AsBool, "strict archive completed durably");
            Equal(report["archiveErrors"].Count, 0, "no strict archive warnings");
            Equal(report["filesLost"].Count, 0, "no outputs lost in strict restore");
            foreach (var file in s.Original) Bytes(File.ReadAllBytes(s.Live(file.Key)), file.Value, "original restored " + file.Key);
            foreach (var key in changed.Keys.Where(k => !s.Original.ContainsKey(k))) Assert(!File.Exists(s.Live(key)), "original absence restored " + key);
            CheckArchive(report, changed);
            for (var i = 0; i < s.Excluded.Count; i++) Bytes(File.ReadAllBytes(s.Excluded[i]), excluded[i], "excluded control preserved");
            EqualTrees(TreeBytes(Path.Combine(s.Session, "bundle")), bundleBefore, "bundle was never modified");
        }
    }

    private static void EmptyOriginal(Scenario s)
    {
        Equal(s.Capture()["files"].Count, 0, "empty original set");
        var created = new Dictionary<string, byte[]>();
        foreach (var key in new[] { "runtime/settings.json", "runtime/NGUSave.txt", "runtime/profiles/new.dat",
            "runtime/custom-plans/new.json", "native/new.txt" })
            created[key] = s.PutKey(key, key);
        s.Retired["runtime/finite-actions-v1/new.bin"] = s.PutKey("runtime/finite-actions-v1/new.bin", "retired output");
        var retired = RetiredBytes(s);
        s.Put(Path.Combine(s.Native, "nested", "untouched.txt"), "nested");
        s.Put(Path.Combine(s.Runtime, "runtime.json"), "telemetry");
        var result = SaveTestBundle.Restore(s.Session);
        Equal(result["scopedFiles"].AsInt, 0, "empty scope verified");
        Equal(result["filesDeleted"].AsInt, created.Count, "all generated selected files deleted");
        foreach (var key in created.Keys) Assert(!File.Exists(s.Live(key)), "absent again " + key);
        CheckArchive(result, created);
        EqualTrees(RetiredBytes(s), retired, "new retired files remain outside restoration scope");
        Equal(File.ReadAllText(Path.Combine(s.Runtime, "runtime.json")), "telemetry", "telemetry is not removed");
        Equal(File.ReadAllText(Path.Combine(s.Native, "nested", "untouched.txt")), "nested", "nested native saves ignored");
    }

    private static void SameStamp(Scenario s)
    {
        var path = Path.Combine(s.Native, "save.txt");
        s.Put(path, "1111");
        var stamp = File.GetLastWriteTimeUtc(path);
        s.Capture();
        s.Put(path, "2222");
        File.SetLastWriteTimeUtc(path, stamp);
        var report = SaveTestBundle.Restore(s.Session);
        Equal(File.ReadAllText(path), "1111", "same-stamp current bytes replaced");
        Equal(File.ReadAllText(Path.Combine(report["archiveDirectory"].Value, "native", "save.txt")), "2222", "same-stamp output preserved");
    }

    private static void RepeatRestore(Scenario s)
    {
        s.Seed(); s.Capture();
        s.PutKey("native/save.txt", "output-one");
        var first = SaveTestBundle.Restore(s.Session);
        var second = SaveTestBundle.Restore(s.Session);
        Equal(second["filesRestored"].AsInt, 0, "idempotent restore does not rewrite originals");
        Equal(second["filesDeleted"].AsInt, 0, "idempotent restore does not delete files");
        Equal(second["archivedFiles"].AsInt, 0, "unchanged originals are not archived");
        Assert(first["archiveDirectory"].Value != second["archiveDirectory"].Value, "each report has a unique archive");
        Equal(File.ReadAllText(Path.Combine(first["archiveDirectory"].Value, "native", "save.txt")), "output-one", "prior output is immutable");
    }

    private static void ReturnedManifest(Scenario s)
    {
        s.PutKey("native/save.txt", "original");
        var returned = s.Capture();
        returned["nativeRoot"] = s.Outside;
        returned["files"][0]["path"] = "../victim.txt";
        s.PutKey("native/save.txt", "output");
        SaveTestBundle.Restore(s.Session);
        Equal(File.ReadAllText(s.Live("native/save.txt")), "original", "mutating returned JSON grants no root or path authority");
    }

    private static void MissingTrees(Scenario s)
    {
        s.Seed(); s.Capture();
        foreach (var name in new[] { "profiles", "custom-plans" }) Directory.Delete(Path.Combine(s.Runtime, name), true);
        Directory.Delete(Path.Combine(s.Runtime, "finite-actions-v1"), true);
        Equal(SaveTestBundle.Restore(s.Session)["filesRestored"].AsInt, 4, "all four configuration-tree originals recreated");
        foreach (var file in s.Original) Bytes(File.ReadAllBytes(s.Live(file.Key)), file.Value, "original restored after tree removal " + file.Key);
        Assert(!Directory.Exists(Path.Combine(s.Runtime, "finite-actions-v1")), "deleted retired tree is never recreated");
    }

    private static void Fixture(Scenario s)
    {
        s.Seed(); s.Capture();
        var bundle = TreeBytes(Path.Combine(s.Session, "bundle"));
        foreach (var key in s.Original.Keys) s.PutKey(key, key == "runtime/settings.json" ? "{}" : "previous-fixture");
        s.Retired["runtime/execution-new-selection.json"] = s.PutKey("runtime/execution-new-selection.json", "new-state");
        s.PutKey("runtime/profiles/fixture-only.txt", "new-profile");
        s.PutKey("runtime/custom-plans/fixture-only.json", "new-plan");
        s.Retired["runtime/finite-actions-v1/fixture-only.bin"] = s.PutKey("runtime/finite-actions-v1/fixture-only.bin", "new-action");
        var retired = RetiredBytes(s);
        var result = SaveTestBundle.PrepareFixture(s.Session, "default");
        Assert(result["verified"].AsBool, "fixture config verified");
        var settings = JsonFiles.ParseObject(File.ReadAllText(s.Live("runtime/settings.json")));
        Equal(settings["_customPlanFile"].Value, "default", "requested plan");
        foreach (var key in new[] { "_useCustomRebirthPlan", "_manageEnergy", "_manageMagic", "_manageGear", "_manageDiggers" })
            Assert(settings[key].IsBoolean && settings[key].AsBool, key + " enabled");
        foreach (var key in new[] { "_autoRebirth", "_customAutoExpEnabled", "_customAutoMoneyPitEnabled" })
            Assert(settings[key].IsBoolean && !settings[key].AsBool, key + " disabled");
        Equal(settings["nativeConfig"]["integer"].Value, "9007199254740993", "unknown exact integer retained");
        Equal(settings["nativeConfig"]["array"].ToString(), "[1,true,\"keep\"]", "other native configuration retained");
        foreach (var key in new[] { "runtime/presets.json", "runtime/zoneOverride.json", "runtime/allocation.json",
            "runtime/profiles/deep/opaque.bin", "runtime/custom-plans/default.json" })
            Bytes(File.ReadAllBytes(s.Live(key)), s.Original[key], "config comes from original " + key);
        foreach (var key in new[] { "runtime/stage-progress-x.json", "runtime/future-execution.json",
            "runtime/profiles/fixture-only.txt", "runtime/custom-plans/fixture-only.json" })
            Assert(!File.Exists(s.Live(key)), "fixture state removed " + key);
        EqualTrees(RetiredBytes(s), retired, "fixture ignores retired records");
        foreach (var key in new[] { "native/save.txt", "native/AutoSave.TXT", "runtime/NGUSave.txt" })
            Equal(File.ReadAllText(s.Live(key)), "previous-fixture", "live saves untouched by config setup " + key);
        s.PutKey("runtime/settings.json", "{}");
        SaveTestBundle.PrepareFixture(s.Session, "next");
        settings = JsonFiles.ParseObject(File.ReadAllText(s.Live("runtime/settings.json")));
        Equal(settings["nativeConfig"]["integer"].Value, "9007199254740993", "next fixture resets from original, not previous fixture");
        Equal(settings["_customPlanFile"].Value, "next", "next plan selected");
        SaveTestBundle.Restore(s.Session);
        foreach (var file in s.Original) Bytes(File.ReadAllBytes(s.Live(file.Key)), file.Value, "full restore after fixtures " + file.Key);
        EqualTrees(RetiredBytes(s), retired, "full restore ignores retired records");
        EqualTrees(TreeBytes(Path.Combine(s.Session, "bundle")), bundle, "fixture never changes original snapshots");
    }

    private static void FixtureLockedSaves(Scenario s)
    {
        s.Seed(); s.Capture();
        using (var native = new FileStream(s.Live("native/save.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var quick = new FileStream(s.Live("runtime/NGUSave.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert(SaveTestBundle.PrepareFixture(s.Session, "default")["verified"].AsBool, "fixture reads no live save bytes");
    }

    private static void FixtureAbsentSettings(Scenario s)
    {
        s.Capture();
        SaveTestBundle.PrepareFixture(s.Session, "default");
        Equal(JsonFiles.ParseObject(File.ReadAllText(s.Live("runtime/settings.json")))["_customPlanFile"].Value, "default", "disposable settings created");
        SaveTestBundle.Restore(s.Session);
        Assert(!File.Exists(s.Live("runtime/settings.json")), "original settings absence restored");
    }

    private static void FixtureBadSettings(Scenario s)
    {
        s.PutKey("runtime/settings.json", "{broken"); s.Capture();
        s.PutKey("runtime/settings.json", "current");
        Throws<FormatException>(() => SaveTestBundle.PrepareFixture(s.Session, "default"));
        Equal(File.ReadAllText(s.Live("runtime/settings.json")), "current", "bad settings do not mutate live config");
        NoArchive(s);
    }

    private static void RetiredRecords(Scenario s, bool locked)
    {
        s.Seed();
        var held = new List<FileStream>();
        try
        {
            foreach (var key in s.Retired.Keys)
            {
                if (locked) held.Add(new FileStream(s.Live(key), FileMode.Open, FileAccess.ReadWrite, FileShare.None));
                else File.SetAttributes(s.Live(key), File.GetAttributes(s.Live(key)) | FileAttributes.ReadOnly);
            }
            var manifest = s.Capture();
            Equal(manifest["files"].Count, s.Original.Count, "only current config, progress, quicksave, and native files captured");
            foreach (var row in manifest["files"].Children)
                Assert(s.Original.ContainsKey(row["area"].Value + "/" + row["path"].Value), "no retired record captured");
            s.PutKey("runtime/settings.json", "{}");
            s.PutKey("runtime/stage-progress-x.json", "fixture progress");
            s.PutKey("native/save.txt", "fixture native bytes");
            Assert(SaveTestBundle.PrepareFixture(s.Session, "default")["verified"].AsBool, "retired storage cannot block fixture setup");
            Equal(File.ReadAllText(s.Live("native/save.txt")), "fixture native bytes", "fixture setup still does not touch native bytes");
            Assert(SaveTestBundle.Restore(s.Session)["verified"].AsBool, "retired storage cannot block protected original restoration");
            foreach (var file in s.Original) Bytes(File.ReadAllBytes(s.Live(file.Key)), file.Value, "exact original restored with inert journals " + file.Key);
        }
        finally { foreach (var stream in held) stream.Dispose(); }
        EqualTrees(RetiredBytes(s), s.Retired, "malformed locked/read-only retired bytes are unchanged");
    }

    private static void LegacyBundleRestore(Scenario s, bool allowArchiveFailure)
    {
        s.Seed();
        var manifest = s.WriteLegacyBundle();
        var pin = Hash(File.ReadAllBytes(s.Manifest));
        var bundle = TreeBytes(Path.Combine(s.Session, "bundle"));
        Equal(manifest["files"].Count, s.Original.Count + s.Retired.Count, "historical bundle includes all original and retired rows");
        var changed = new Dictionary<string, byte[]>();
        foreach (var key in s.Original.Keys)
        {
            if (key == "runtime/profiles/default.json" || key == "native/AutoSave.TXT") File.Delete(s.Live(key));
            else changed[key] = s.PutKey(key, "disposable-" + key);
        }
        foreach (var key in new[] { "runtime/stage-progress-fixture.json", "native/fixture-auto.txt" })
            changed[key] = s.PutKey(key, "new disposable output");
        foreach (var key in s.Retired.Keys) s.PutKey(key, "current retired bytes " + key);
        File.Delete(s.Live("runtime/finite-actions-v1/preparation/opaque.bin"));
        File.Delete(s.Live("runtime/execution-selection.json"));
        s.Retired["runtime/finite-actions-v1/fixture-only.bin"] = s.PutKey("runtime/finite-actions-v1/fixture-only.bin", "new retired output");
        var retired = RetiredBytes(s);
        if (allowArchiveFailure) s.Put(Path.Combine(s.Session, "test-outputs"), "archive obstruction");
        var child = RunResumeChild(s.Runtime, s.Native, s.Session, pin, allowArchiveFailure ? "resume" : "restore");
        Equal(child["exitCode"].AsInt, 0, "cold resume accepts a legitimately pinned old bundle with arbitrary retired extensions: " + child);
        Assert(child["verified"].AsBool && !child["alreadyTrusted"].AsBool, "legacy bundle validated before cold trust registration");
        Equal(child["manifestSha256"].Value, pin, "legacy manifest pin unchanged");
        Equal(child["scopedFiles"].AsInt, s.Original.Count, "only current restore scope remains after all historical bytes are validated");
        JSONObject report;
        if (allowArchiveFailure)
        {
            SaveTestBundle.Resume(s.Runtime, s.Native, s.Session, pin);
            report = SaveTestBundle.Restore(s.Session, true);
            CheckArchiveLoss(report, changed);
        }
        else
        {
            report = child["restoration"].AsObject;
            CheckArchive(report, changed);
        }
        Assert(report["verified"].AsBool, "legacy original restoration independently verifies exact scope and bytes");
        Equal(report["filesRestored"].AsInt, s.Original.Count, "every changed or missing current-scope original restored");
        Equal(report["filesDeleted"].AsInt, 2, "only the two new current-scope outputs removed");
        foreach (var file in s.Original) Bytes(File.ReadAllBytes(s.Live(file.Key)), file.Value, "legacy original restored exactly " + file.Key);
        foreach (var key in new[] { "runtime/stage-progress-fixture.json", "native/fixture-auto.txt" })
            Assert(!File.Exists(s.Live(key)), "legacy original absence restored " + key);
        EqualTrees(RetiredBytes(s), retired, "legacy restore neither writes journals nor resurrects missing retired files");
        EqualTrees(TreeBytes(Path.Combine(s.Session, "bundle")), bundle, "legacy protected snapshots remain immutable");
    }

    private static void LegacyBundleFixture(Scenario s)
    {
        s.Seed(); s.WriteLegacyBundle();
        var pin = Hash(File.ReadAllBytes(s.Manifest));
        var bundle = TreeBytes(Path.Combine(s.Session, "bundle"));
        foreach (var key in s.Original.Keys) s.PutKey(key, key == "runtime/settings.json" ? "{}" : "previous fixture");
        Directory.Delete(Path.Combine(s.Runtime, "finite-actions-v1"), true);
        File.Delete(s.Live("runtime/execution-selection.json"));
        var retired = RetiredBytes(s);
        var child = RunResumeChild(s.Runtime, s.Native, s.Session, pin, "prepare");
        Equal(child["exitCode"].AsInt, 0, "cold legacy bundle prepares configuration: " + child);
        Assert(child["fixture"]["verified"].AsBool, "legacy fixture setup verified");
        var settings = JsonFiles.ParseObject(File.ReadAllText(s.Live("runtime/settings.json")));
        Equal(settings["_customPlanFile"].Value, "default", "legacy fixture selects its requested plan");
        Equal(settings["nativeConfig"]["integer"].Value, "9007199254740993", "legacy fixture preserves exact original configuration tokens");
        Bytes(File.ReadAllBytes(s.Live("runtime/custom-plans/default.json")), s.Original["runtime/custom-plans/default.json"], "legacy fixture restores original plan bytes");
        Assert(!File.Exists(s.Live("runtime/stage-progress-x.json")), "fixture does not import original earned stage progress");
        foreach (var key in new[] { "native/save.txt", "native/AutoSave.TXT", "runtime/NGUSave.txt" })
            Equal(File.ReadAllText(s.Live(key)), "previous fixture", "legacy fixture leaves live saves untouched " + key);
        EqualTrees(RetiredBytes(s), retired, "legacy fixture neither writes nor recreates retired journals");
        Assert(!Directory.Exists(Path.Combine(s.Runtime, "finite-actions-v1")), "legacy fixture does not recreate the retired tree");
        SaveTestBundle.Resume(s.Runtime, s.Native, s.Session, pin);
        SaveTestBundle.Restore(s.Session);
        foreach (var file in s.Original) Bytes(File.ReadAllBytes(s.Live(file.Key)), file.Value, "legacy originals restored after fixture " + file.Key);
        EqualTrees(RetiredBytes(s), retired, "post-fixture original restoration leaves retired history absent/unchanged");
        Assert(!Directory.Exists(Path.Combine(s.Runtime, "finite-actions-v1")), "original restoration does not recreate the retired tree");
        EqualTrees(TreeBytes(Path.Combine(s.Session, "bundle")), bundle, "fixture preserves all historical protected snapshots");
    }

    private static void LegacyBundleBadSnapshot(Scenario s, string kind)
    {
        s.Seed(); var manifest = s.WriteLegacyBundle();
        var pin = Hash(File.ReadAllBytes(s.Manifest));
        SaveTestBundle.Resume(s.Runtime, s.Native, s.Session, pin);
        var index = Enumerable.Range(0, manifest["files"].Count)
            .Single(i => manifest["files"][i]["path"].Value == "finite-actions-v1/preparation/opaque.bin");
        var snapshot = s.Snapshot(index);
        MakeWritable(snapshot);
        if (kind == "missing" || kind == "oversized") File.Delete(snapshot);
        if (kind == "oversized") Sized(snapshot, 32L * 1024 * 1024 + 1);
        else if (kind == "corrupt")
        {
            var bytes = File.ReadAllBytes(snapshot); bytes[0] ^= 1; File.WriteAllBytes(snapshot, bytes);
        }
        s.PutKey("native/save.txt", "current native output");
        s.PutKey("runtime/settings.json", "{}");
        var before = TreeBytes(s.Root);
        ChildRejected(RunResumeChild(s.Runtime, s.Native, s.Session, pin, "resume"));
        foreach (var action in new Action[] { () => SaveTestBundle.Resume(s.Runtime, s.Native, s.Session, pin),
            () => SaveTestBundle.Restore(s.Session), () => SaveTestBundle.Restore(s.Session, true),
            () => SaveTestBundle.PrepareFixture(s.Session, "default") })
        {
            if (kind == "missing") Throws<FileNotFoundException>(action);
            else Throws<InvalidDataException>(action);
        }
        EqualTrees(TreeBytes(s.Root), before, "even filtered retired snapshots are validated before archival, fixture, or destination writes");
        NoArchive(s);
    }

    private static void BadPlanName(Scenario s, string name)
    {
        s.Capture();
        Throws<InvalidDataException>(() => SaveTestBundle.PrepareFixture(s.Session, name));
        NoArchive(s);
    }

    private static void TamperedManifest(Scenario s, string field, string path)
    {
        s.Seed(); s.Capture();
        var outside = s.Put(Path.Combine(s.Outside, "victim.json"), "never-touch");
        var manifest = JsonFiles.ParseObject(File.ReadAllText(s.Manifest));
        if (field == "hash") manifest["files"][0]["sha256"] = new string('0', 64);
        else if (field == "length") manifest["files"][0]["length"] = -1;
        else if (field == "area") manifest["files"][0]["area"] = "outside";
        else if (field == "path") manifest["files"][0]["path"] = path;
        else if (field == "duplicate") manifest["files"].Add(manifest["files"][0]);
        else if (field == "version") manifest["version"] = 2;
        else manifest[field] = s.Outside;
        MakeWritable(s.Manifest);
        File.WriteAllText(s.Manifest, manifest.ToString(), Utf8);
        s.PutKey("native/save.txt", "current");
        Throws<InvalidDataException>(() => SaveTestBundle.Restore(s.Session));
        Throws<InvalidDataException>(() => SaveTestBundle.Restore(s.Session, true));
        Throws<InvalidDataException>(() => SaveTestBundle.PrepareFixture(s.Session, "default"));
        // Even a newly supplied matching hash cannot authorize malformed paths or identities.
        ChildRejected(RunResumeChild(s.Runtime, s.Native, s.Session, Hash(File.ReadAllBytes(s.Manifest)), "resume"));
        Equal(File.ReadAllText(s.Live("native/save.txt")), "current", "tampered metadata causes no restoration");
        Bytes(File.ReadAllBytes(Path.Combine(s.Outside, "victim.json")), outside, "untrusted destination untouched");
        NoArchive(s);
    }

    private static void UntrustedSession(Scenario s)
    {
        s.Capture();
        var copy = Path.Combine(s.Runtime, "save-safety", "tests", "disk-copy");
        s.Put(Path.Combine(copy, "bundle", "manifest.json"), File.ReadAllText(s.Manifest));
        Throws<InvalidOperationException>(() => SaveTestBundle.Restore(copy));
        Assert(!Directory.Exists(Path.Combine(copy, "test-outputs")), "disk-only session cannot write");
    }

    private static void Recapture(Scenario s)
    {
        s.Seed(); s.Capture();
        var original = TreeBytes(Path.Combine(s.Session, "bundle"));
        Throws<IOException>(() => s.Capture());
        EqualTrees(TreeBytes(Path.Combine(s.Session, "bundle")), original, "recapture never overwrites snapshots");
        var abandoned = Path.Combine(s.Runtime, "save-safety", "tests", "abandoned");
        var sentinel = Path.Combine(abandoned, "bundle", "sentinel");
        s.Put(sentinel, "existing");
        Throws<IOException>(() => SaveTestBundle.Capture(s.Runtime, s.Native, abandoned));
        Equal(File.ReadAllText(sentinel), "existing", "untrusted existing bundle remains untouched");
    }

    private static void ResumeRoundTrip(Scenario s)
    {
        s.Seed(); s.Capture();
        var pin = Hash(File.ReadAllBytes(s.Manifest));
        var bundle = TreeBytes(Path.Combine(s.Session, "bundle"));
        var changed = new Dictionary<string, byte[]>();
        foreach (var key in new[] { "runtime/settings.json", "native/save.txt", "runtime/NGUSave.txt", "runtime/fixture.json", "native/fixture.txt" })
            changed[key] = s.PutKey(key, "disposable-" + key);
        File.Delete(s.Live("runtime/profiles/deep/opaque.bin"));
        using (JsonFiles.AcquireInjectorLease(s.Runtime, 0))
        {
            var child = RunResumeChild(s.Runtime, s.Native, s.Session, pin, "restore");
            Equal(child["exitCode"].AsInt, 0, "actual child process resumed and restored: " + child);
            Assert(child["verified"].AsBool, "cold bundle was verified");
            Assert(!child["alreadyTrusted"].AsBool, "child initially had no trusted session");
            Equal(child["manifestSha256"].Value, pin, "coordinator pin echoed");
            Equal(child["scopedFiles"].AsInt, s.Original.Count, "all original snapshots validated");
            Assert(child["restoration"]["verified"].AsBool, "child restoration independently verified");
            foreach (var file in s.Original) Bytes(File.ReadAllBytes(s.Live(file.Key)), file.Value, "cold restored bytes " + file.Key);
            Assert(!File.Exists(s.Live("runtime/fixture.json")) && !File.Exists(s.Live("native/fixture.txt")), "cold restore restores original absence");
            CheckArchive(child["restoration"].AsObject, changed);
            EqualTrees(TreeBytes(Path.Combine(s.Session, "bundle")), bundle, "cold resume/restore preserves immutable original bundle");
        }
    }

    private static void ResumeReadOnly(Scenario s)
    {
        s.Seed(); s.Capture();
        s.PutKey("native/save.txt", "current-output");
        var before = TreeBytes(s.Root);
        var child = RunResumeChild(s.Runtime, s.Native, s.Session, Hash(File.ReadAllBytes(s.Manifest)), "resume");
        Equal(child["exitCode"].AsInt, 0, "cold resume succeeded: " + child);
        Assert(child["verified"].AsBool, "bundle verified without restoring current data");
        EqualTrees(TreeBytes(s.Root), before, "resume and idempotent revalidation make no filesystem writes");
        NoArchive(s);
    }

    private static void ResumeConflicts(Scenario s)
    {
        s.Seed(); s.Capture();
        var pin = Hash(File.ReadAllBytes(s.Manifest));
        Assert(SaveTestBundle.Resume(s.Runtime, s.Native, s.Session, pin)["alreadyTrusted"].AsBool, "matching captured session accepted");
        Throws<InvalidOperationException>(() => SaveTestBundle.Resume(s.Runtime, s.Native, s.Session, new string('0', 64)));
        Throws<InvalidOperationException>(() => SaveTestBundle.Resume(s.Runtime, s.Outside, s.Session, pin));
        Throws<InvalidDataException>(() => SaveTestBundle.Resume(s.Runtime, s.Native, s.Session, null));
        s.PutKey("native/save.txt", "output");
        SaveTestBundle.Restore(s.Session);
        Bytes(File.ReadAllBytes(s.Live("native/save.txt")), s.Original["native/save.txt"], "conflicting pins never replace existing trust");
        MakeWritable(s.Snapshot(0)); File.WriteAllText(s.Snapshot(0), "corrupt", Utf8);
        Throws<InvalidDataException>(() => SaveTestBundle.Resume(s.Runtime, s.Native, s.Session, pin));
        Throws<InvalidDataException>(() => SaveTestBundle.Restore(s.Session, true));
    }

    private static void ResumeFixture(Scenario s)
    {
        s.Seed(); s.Capture();
        s.PutKey("runtime/settings.json", "{}");
        s.PutKey("native/save.txt", "disposable-save");
        var child = RunResumeChild(s.Runtime, s.Native, s.Session, Hash(File.ReadAllBytes(s.Manifest)), "prepare");
        Equal(child["exitCode"].AsInt, 0, "cold session prepared fixture: " + child);
        Assert(child["fixture"]["verified"].AsBool, "resumed fixture verified");
        var settings = JsonFiles.ParseObject(File.ReadAllText(s.Live("runtime/settings.json")));
        Equal(settings["nativeConfig"]["integer"].Value, "9007199254740993", "resumed fixture starts from original configuration");
        Equal(File.ReadAllText(s.Live("native/save.txt")), "disposable-save", "resumed fixture still leaves live native saves untouched");
        EqualTrees(RetiredBytes(s), s.Retired, "resumed fixture ignores retired records");
        SaveTestBundle.Restore(s.Session);
        foreach (var file in s.Original) Bytes(File.ReadAllBytes(s.Live(file.Key)), file.Value, "parent can restore after cold prepared fixture " + file.Key);
    }

    private static void ResumeWrongPin(Scenario s, string pin)
    {
        s.Seed(); s.Capture();
        var before = TreeBytes(s.Root);
        ChildRejected(RunResumeChild(s.Runtime, s.Native, s.Session, pin, "resume"));
        EqualTrees(TreeBytes(s.Root), before, "wrong manifest pin causes no writes");
        NoArchive(s);
    }

    private static void ResumeWrongIdentity(Scenario s, string identity)
    {
        s.Seed(); s.Capture();
        var runtime = s.Runtime;
        var native = s.Native;
        var session = s.Session;
        if (identity == "runtime") runtime = s.Outside;
        else if (identity == "native") native = s.Outside;
        else if (identity == "swapped") { runtime = s.Native; native = s.Runtime; }
        else if (identity == "copied-session")
        {
            session = Path.Combine(s.Runtime, "save-safety", "tests", "copy");
            foreach (var file in TreeBytes(Path.Combine(s.Session, "bundle")))
            {
                var target = Path.Combine(session, "bundle", file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)); File.WriteAllBytes(target, file.Value);
            }
        }
        else if (identity == "nested-session") session = Path.Combine(s.Session, "nested");
        else if (identity == "outside-session") session = s.Outside;
        else if (identity == "parent-session") session = Path.Combine(s.Session, "..", "session");
        else if (identity == "dot-session") session = Path.Combine(s.Session, ".");
        else if (identity == "trailing-separator") session += Path.DirectorySeparatorChar;
        else if (identity == "missing-native") Directory.Delete(s.Native, true);
        var before = TreeBytes(s.Root);
        ChildRejected(RunResumeChild(runtime, native, session, Hash(File.ReadAllBytes(s.Manifest)), "resume"));
        EqualTrees(TreeBytes(s.Root), before, "wrong or ambiguous root identity causes no writes");
        NoArchive(s);
    }

    private static void ResumeBadSnapshot(Scenario s, string kind)
    {
        s.Seed(); var manifest = s.Capture();
        var snapshot = s.Snapshot(manifest["files"].Count - 1);
        MakeWritable(snapshot);
        if (kind == "missing" || kind == "oversized") File.Delete(snapshot);
        if (kind == "oversized") Sized(snapshot, 32L * 1024 * 1024 + 1);
        else if (kind == "corrupt")
        {
            var bytes = File.ReadAllBytes(snapshot); bytes[0] ^= 1; File.WriteAllBytes(snapshot, bytes);
        }
        var before = TreeBytes(s.Root);
        ChildRejected(RunResumeChild(s.Runtime, s.Native, s.Session, Hash(File.ReadAllBytes(s.Manifest)), "resume"));
        EqualTrees(TreeBytes(s.Root), before, "invalid original snapshot causes no writes or registration");
        NoArchive(s);
    }

    private static void ResumeBounds(Scenario s, string kind)
    {
        var manifest = s.Capture();
        var rows = new JSONArray();
        var count = kind == "files" ? 4097 : kind == "total" ? 17 : 1;
        for (var i = 0; i < count; i++)
        {
            var row = new JSONObject();
            row["area"] = kind == "depth" ? "runtime" : "native";
            row["path"] = kind == "depth" ? "profiles/" + string.Concat(Enumerable.Repeat("d/", 65)) + "file" : i + ".txt";
            row["length"] = kind == "file" ? 32 * 1024 * 1024 + 1 : kind == "total" ? 32 * 1024 * 1024 : 0;
            row["sha256"] = Hash(new byte[0]); rows.Add(row);
        }
        manifest["files"] = rows;
        MakeWritable(s.Manifest); File.WriteAllText(s.Manifest, manifest.ToString(), Utf8);
        var before = TreeBytes(s.Root);
        var child = RunResumeChild(s.Runtime, s.Native, s.Session, Hash(File.ReadAllBytes(s.Manifest)), "resume");
        ChildRejected(child);
        Equal(child["error"].Value, "InvalidDataException", "invalid pinned manifest bounds rejected before snapshot reads");
        EqualTrees(TreeBytes(s.Root), before, "bounded resume makes no writes");
        NoArchive(s);
    }

    private static void ResumeReparse(Scenario s, string location)
    {
        s.Seed(); s.Capture();
        var pin = Hash(File.ReadAllBytes(s.Manifest));
        var victim = Path.Combine(s.Outside, "victim.txt"); s.Put(victim, "untouched");
        if (location == "session" || location == "bundle" || location == "profiles")
        {
            var link = location == "session" ? s.Session : location == "bundle" ? Path.Combine(s.Session, "bundle") : Path.Combine(s.Runtime, "profiles");
            var target = Path.Combine(s.Outside, "link-target");
            Directory.Move(link, target); DirectoryLink(link, target);
        }
        else
        {
            var link = location == "manifest" ? s.Manifest : location == "snapshot" ? s.Snapshot(0) : s.Live("native/save.txt");
            MakeWritable(link); File.Delete(link);
            if (!FileLink(link, victim)) return;
        }
        var child = RunResumeChild(s.Runtime, s.Native, s.Session, pin, "resume");
        ChildRejected(child);
        Equal(child["error"].Value, "InvalidDataException", "reparse resume rejected safely");
        Equal(File.ReadAllText(victim), "untouched", "resume does not follow or modify reparse targets");
        NoArchive(s);
    }

    private static void CorruptSnapshot(Scenario s)
    {
        s.Seed(); var manifest = s.Capture();
        s.PutKey("native/save.txt", "current");
        var last = s.Snapshot(manifest["files"].Count - 1);
        MakeWritable(last);
        File.WriteAllText(last, "corrupt", Utf8);
        Throws<InvalidDataException>(() => SaveTestBundle.Restore(s.Session));
        Throws<InvalidDataException>(() => SaveTestBundle.Restore(s.Session, true));
        Equal(File.ReadAllText(s.Live("native/save.txt")), "current", "corruption found before any destination write");
        NoArchive(s);
    }

    private static void MissingSnapshot(Scenario s)
    {
        s.PutKey("native/save.txt", "original"); s.Capture();
        MakeWritable(s.Snapshot(0)); File.Delete(s.Snapshot(0));
        Throws<FileNotFoundException>(() => SaveTestBundle.Restore(s.Session));
        Throws<FileNotFoundException>(() => SaveTestBundle.Restore(s.Session, true));
        NoArchive(s);
    }

    private static void ArchiveBlocked(Scenario s)
    {
        s.Seed(); s.Capture();
        s.PutKey("runtime/settings.json", "current-settings");
        s.PutKey("native/save.txt", "current-native");
        s.Put(Path.Combine(s.Session, "test-outputs"), "obstruction");
        Throws<IOException>(() => SaveTestBundle.Restore(s.Session));
        Equal(File.ReadAllText(s.Live("runtime/settings.json")), "current-settings", "config not replaced on archival failure");
        Equal(File.ReadAllText(s.Live("native/save.txt")), "current-native", "native save not replaced on archival failure");
    }

    private static void ArchiveReadFailure(Scenario s)
    {
        s.Seed(); s.Capture();
        s.PutKey("runtime/settings.json", "current-settings");
        s.PutKey("native/save.txt", "current-native");
        using (var held = new FileStream(s.Live("runtime/settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Throws<IOException>(() => SaveTestBundle.Restore(s.Session));
            Throws<IOException>(() => SaveTestBundle.Restore(s.Session, true));
        }
        Equal(File.ReadAllText(s.Live("runtime/settings.json")), "current-settings", "locked config left unchanged");
        Equal(File.ReadAllText(s.Live("native/save.txt")), "current-native", "earlier native file never restored ahead of archive preflight");
        NoArchive(s);
    }

    private static void DestinationObstruction(Scenario s)
    {
        s.PutKey("runtime/profiles/file.bin", "original"); s.Capture();
        File.Delete(s.Live("runtime/profiles/file.bin"));
        Directory.CreateDirectory(s.Live("runtime/profiles/file.bin"));
        Throws<IOException>(() => SaveTestBundle.Restore(s.Session));
        Throws<IOException>(() => SaveTestBundle.Restore(s.Session, true));
        NoArchive(s);
        Assert(Directory.Exists(s.Live("runtime/profiles/file.bin")), "directory obstruction is not recursively deleted");
    }

    private static void DestinationWriteFailure(Scenario s)
    {
        s.Seed(); s.Capture();
        var changed = new Dictionary<string, byte[]>();
        changed["native/save.txt"] = s.PutKey("native/save.txt", "current-native");
        changed["runtime/settings.json"] = s.PutKey("runtime/settings.json", "current-settings");
        using (var held = new FileStream(s.Live("native/save.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Throws<IOException>(() => SaveTestBundle.Restore(s.Session));
        var archive = Directory.GetDirectories(Path.Combine(s.Session, "test-outputs")).Single();
        var report = new JSONObject(); report["archiveDirectory"] = archive;
        CheckArchive(report, changed);
        Equal(File.ReadAllText(s.Live("native/save.txt")), "current-native", "first blocked destination left untouched");
        Equal(File.ReadAllText(s.Live("runtime/settings.json")), "current-settings", "later output was archived before first replacement");
        Assert(!Directory.GetFiles(s.Native, ".bundle-*.tmp").Any(), "failed replacement cleans its temporary file");
        Assert(SaveTestBundle.Restore(s.Session)["verified"].AsBool, "restore can retry after a write failure");
        CheckArchive(report, changed);
    }

    private static void ArchiveFailureAuthorized(Scenario s)
    {
        s.Seed(); s.Capture();
        var bundle = TreeBytes(Path.Combine(s.Session, "bundle"));
        var changed = new Dictionary<string, byte[]>();
        foreach (var key in new[] { "native/save.txt", "runtime/settings.json", "native/fixture-auto.txt", "runtime/fixture-state.json" })
            changed[key] = s.PutKey(key, "disposable-" + key);
        s.Put(Path.Combine(s.Session, "test-outputs"), "archive-obstruction");
        var result = SaveTestBundle.Restore(s.Session, true);
        Assert(result["verified"].AsBool, "authorized restoration verifies original bytes and scoped set despite archive failure");
        Equal(result["filesRestored"].AsInt, 2, "both changed originals restored");
        Equal(result["filesDeleted"].AsInt, 2, "both new fixture files removed");
        Assert(!File.Exists(s.Live("native/fixture-auto.txt")) && !File.Exists(s.Live("runtime/fixture-state.json")), "fixture outputs do not remain production candidates");
        CheckArchiveLoss(result, changed);
        foreach (var file in s.Original) Bytes(File.ReadAllBytes(s.Live(file.Key)), file.Value, "originals restored despite archive failure " + file.Key);
        Equal(File.ReadAllText(Path.Combine(s.Session, "test-outputs")), "archive-obstruction", "archive obstruction is not deleted");
        EqualTrees(TreeBytes(Path.Combine(s.Session, "bundle")), bundle, "original backup always preserved");
    }

    private static void ArchiveWriteDenied(Scenario s, bool manifestOnly)
    {
        if (!OperatingSystem.IsWindows()) { skipped++; Console.WriteLine("SKIP: Windows archive ACL check."); return; }
        s.Seed(); s.Capture();
        var changed = new Dictionary<string, byte[]>();
        foreach (var key in new[] { "native/save.txt", "runtime/settings.json" }) changed[key] = s.PutKey(key, "disposable");
        var directory = new DirectoryInfo(Path.Combine(s.Session, "test-outputs")); directory.Create();
        var originalAcl = directory.GetAccessControl();
        var deniedAcl = directory.GetAccessControl();
        using (var identity = WindowsIdentity.GetCurrent())
            deniedAcl.AddAccessRule(new FileSystemAccessRule(identity.User,
                manifestOnly ? FileSystemRights.CreateFiles : FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles,
                manifestOnly ? InheritanceFlags.ContainerInherit : InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                manifestOnly ? PropagationFlags.InheritOnly | PropagationFlags.NoPropagateInherit : PropagationFlags.None, AccessControlType.Deny));
        directory.SetAccessControl(deniedAcl);
        try
        {
            Throws<UnauthorizedAccessException>(() => SaveTestBundle.Restore(s.Session));
            Equal(File.ReadAllText(s.Live("native/save.txt")), "disposable", "strict archive write denial preserves current data");
            var result = SaveTestBundle.Restore(s.Session, true);
            if (manifestOnly)
            {
                Assert(result["verified"].AsBool && !result["archiveComplete"].AsBool, "originals verified while archive metadata warning retained");
                Assert(!result["archiveManifestWritten"].AsBool, "failed archive manifest is not reported as durable");
                Equal(result["archivedFiles"].AsInt, changed.Count, "verified archival byte copies remain counted");
                Equal(result["filesLost"].Count, 0, "metadata failure does not lose verified archival copies");
                Equal(result["archiveErrors"].Count, 1, "only the archive-manifest write failed");
                foreach (var file in changed)
                {
                    var path = Path.Combine(result["archiveDirectory"].Value, file.Key.Replace('/', Path.DirectorySeparatorChar));
                    Bytes(File.ReadAllBytes(path), file.Value, "archived current bytes survive missing archive manifest " + file.Key);
                    Assert(ReadOnly(path), "archived output is immutable despite metadata warning");
                }
            }
            else
            {
                CheckArchiveLoss(result, changed);
                Equal(result["archiveErrors"].Count, 3, "two copy failures plus archive-manifest write failure reported");
            }
            foreach (var file in s.Original) Bytes(File.ReadAllBytes(s.Live(file.Key)), file.Value, "archive write denial cannot block authorized original restore " + file.Key);
        }
        finally { directory.SetAccessControl(originalAcl); }
    }

    private static void ArchiveOptionalHealthy(Scenario s)
    {
        s.PutKey("native/save.txt", "original"); s.Capture();
        var changed = new Dictionary<string, byte[]> { { "native/save.txt", s.PutKey("native/save.txt", "output") } };
        var report = SaveTestBundle.Restore(s.Session, true);
        Assert(report["verified"].AsBool && report["archiveComplete"].AsBool, "opt-in does not skip a healthy archive");
        Equal(report["archiveErrors"].Count, 0, "healthy authorized archive has no warnings");
        Equal(report["filesLost"].Count, 0, "healthy authorized archive has no data loss");
        Equal(report["archivedFiles"].AsInt, 1, "healthy authorized archive count");
        CheckArchive(report, changed);
        Equal(File.ReadAllText(s.Live("native/save.txt")), "original", "authorized restore bytes verified");
    }

    private static void ArchiveNoChanges(Scenario s)
    {
        s.Capture(); s.Put(Path.Combine(s.Session, "test-outputs"), "blocked");
        var report = SaveTestBundle.Restore(s.Session, true);
        Assert(report["verified"].AsBool && !report["archiveComplete"].AsBool, "original empty file set verified with archive warning");
        Equal(report["archiveErrors"].Count, 1, "metadata failure reported even with no changed data");
        Equal(report["filesLost"].Count, 0, "metadata failure is not falsely reported as lost data");
        Equal(report["archivedFiles"].AsInt, 0, "no phantom archival copies");
    }

    private static void CheckArchiveLoss(JSONObject report, Dictionary<string, byte[]> changed)
    {
        Assert(report["verified"].AsBool, "original state is verified even when fixture archive fails");
        Assert(!report["archiveComplete"].AsBool && !report["archiveManifestWritten"].AsBool, "failed archive never reported as complete");
        Assert(report["archiveErrors"].IsArray && report["archiveErrors"].Count > 0, "archive failure warnings returned");
        Equal(report["archivedFiles"].AsInt, 0, "unverified archival copies are not counted");
        Equal(report["filesLost"].Count, changed.Count, "all unarchived changed files explicitly reported");
        foreach (var row in report["filesLost"].Children)
        {
            var key = row["area"].Value + "/" + row["path"].Value;
            Assert(changed.ContainsKey(key), "only disposable changed files reported lost: " + key);
            Equal(row["sha256"].Value, Hash(changed[key]), "lost output hash retained: " + key);
            Equal((long)row["length"].AsDouble, changed[key].LongLength, "lost output length retained: " + key);
        }
    }

    private static void MissingNative(Scenario s)
    {
        Directory.Delete(s.Native);
        Throws<IOException>(() => s.Capture());
        Assert(!Directory.Exists(Path.Combine(s.Session, "bundle")), "incomplete native scope creates no bundle");
    }

    private static void InvalidRoots(Scenario s)
    {
        Throws<InvalidDataException>(() => SaveTestBundle.Capture(s.Runtime, s.Runtime, s.Session));
        Throws<InvalidDataException>(() => SaveTestBundle.Capture(s.Runtime, Path.Combine(s.Runtime, "native"), s.Session));
        Throws<InvalidDataException>(() => SaveTestBundle.Capture(s.Runtime, s.Native, s.Outside));
        Throws<InvalidDataException>(() => SaveTestBundle.Capture(s.Runtime, s.Native, Path.Combine(s.Session, "nested")));
        Throws<InvalidDataException>(() => SaveTestBundle.Capture("relative", s.Native, s.Session));
        if (Path.DirectorySeparatorChar == '\\')
        {
            Throws<InvalidDataException>(() => SaveTestBundle.Capture("C:relative", s.Native, s.Session));
            Throws<InvalidDataException>(() => SaveTestBundle.Capture("\\root-relative", s.Native, s.Session));
        }
        Assert(!Directory.Exists(Path.Combine(s.Session, "bundle")), "invalid roots create no bundle");
    }

    private static void CountLimit(Scenario s, string area)
    {
        var root = area == "runtime" ? s.Runtime : s.Native;
        for (var i = 0; i < 4097; i++) File.WriteAllBytes(Path.Combine(root, i + (area == "runtime" ? ".json" : ".txt")), new byte[0]);
        Throws<InvalidDataException>(() => s.Capture());
        Assert(!Directory.Exists(Path.Combine(s.Session, "bundle")), "count bound enforced before snapshot writes");
    }

    private static void FileLimit(Scenario s, string area)
    {
        Sized(s.Live(area + (area == "runtime" ? "/large.json" : "/large.txt")), 32L * 1024 * 1024 + 1);
        Throws<InvalidDataException>(() => s.Capture());
        Assert(!Directory.Exists(Path.Combine(s.Session, "bundle")), "oversize bound enforced before writes");
    }

    private static void CurrentFileLimit(Scenario s, string area)
    {
        s.Capture();
        Sized(s.Live(area + (area == "runtime" ? "/large.json" : "/large.txt")), 32L * 1024 * 1024 + 1);
        Throws<InvalidDataException>(() => SaveTestBundle.Restore(s.Session));
        NoArchive(s);
    }

    private static void ExactFileLimit(Scenario s)
    {
        var path = s.Live("native/large.txt");
        Sized(path, 32L * 1024 * 1024);
        var expected = Hash(File.ReadAllBytes(path));
        Equal((long)s.Capture()["files"][0]["length"].AsDouble, 32L * 1024 * 1024, "exact file bound accepted");
        File.Delete(path);
        Equal(SaveTestBundle.Restore(s.Session)["filesRestored"].AsInt, 1, "boundary-sized original restored");
        Equal(Hash(File.ReadAllBytes(path)), expected, "boundary-sized original hash verified independently");
    }

    private static void ExactCountLimit(Scenario s)
    {
        for (var i = 0; i < 4096; i++)
        {
            File.WriteAllBytes(Path.Combine(s.Runtime, i + ".json"), new byte[0]);
            File.WriteAllBytes(Path.Combine(s.Native, i + ".txt"), new byte[0]);
        }
        Equal(s.Capture()["files"].Count, 8192, "4096 runtime plus 4096 native files accepted");
        var child = RunResumeChild(s.Runtime, s.Native, s.Session, Hash(File.ReadAllBytes(s.Manifest)), "resume");
        Equal(child["exitCode"].AsInt, 0, "maximum-count bundle cold-resumes: " + child);
        Equal(child["scopedFiles"].AsInt, 8192, "cold resume accepts per-area counts independently");
        Equal(SaveTestBundle.Restore(s.Session)["scopedFiles"].AsInt, 8192, "maximum-count scoped set verified");
    }

    private static void TotalLimit(Scenario s)
    {
        for (var i = 0; i < 17; i++) Sized(Path.Combine(i < 8 ? s.Runtime : s.Native, i + (i < 8 ? ".json" : ".txt")), 32L * 1024 * 1024);
        Throws<InvalidDataException>(() => s.Capture());
        Assert(!Directory.Exists(Path.Combine(s.Session, "bundle")), "combined runtime/native total is bounded before writes");
    }

    private static void DepthLimit(Scenario s)
    {
        var path = Path.Combine(s.Runtime, "profiles");
        for (var i = 0; i < 65; i++) path = Path.Combine(path, "d");
        Directory.CreateDirectory(path);
        Throws<InvalidDataException>(() => s.Capture());
        Assert(!Directory.Exists(Path.Combine(s.Session, "bundle")), "empty directory recursion is bounded");
    }

    private static void Reparse(Scenario s, string location)
    {
        var victim = Path.Combine(s.Outside, "victim.txt");
        s.Put(victim, "untouched-target");
        if (location == "runtime-root" || location == "native-root")
        {
            var link = Path.Combine(s.Root, "root-link");
            DirectoryLink(link, location == "runtime-root" ? s.Runtime : s.Native);
            Throws<InvalidDataException>(() => SaveTestBundle.Capture(location == "runtime-root" ? link : s.Runtime,
                location == "native-root" ? link : s.Native, location == "runtime-root" ? Path.Combine(link, "save-safety", "tests", "test") : s.Session));
        }
        else if (location == "session" || location == "profiles" || location == "nested" || location == "directory-symlink")
        {
            var link = location == "session" ? s.Session : location == "directory-symlink" ? Path.Combine(s.Runtime, "custom-plans")
                : Path.Combine(s.Runtime, "profiles", location == "nested" ? "nested" : "");
            Directory.CreateDirectory(Path.GetDirectoryName(link));
            if (location == "directory-symlink")
            {
                try { Directory.CreateSymbolicLink(link, s.Outside); }
                catch (UnauthorizedAccessException) { skipped++; Console.WriteLine("SKIP: directory symlink privilege unavailable."); return; }
            }
            else DirectoryLink(link, s.Outside);
            Throws<InvalidDataException>(() => s.Capture());
        }
        else if (location == "capture-file" || location == "capture-native-file")
        {
            if (!FileLink(s.Live(location == "capture-file" ? "runtime/settings.json" : "native/save.txt"), victim)) return;
            Throws<InvalidDataException>(() => s.Capture());
            Assert(!Directory.Exists(Path.Combine(s.Session, "bundle")), "capture rejects linked files before writing snapshots");
        }
        else
        {
            s.Seed(); s.Capture();
            s.PutKey("native/save.txt", "current-output");
            if (location == "archive") DirectoryLink(Path.Combine(s.Session, "test-outputs"), s.Outside);
            else if (location == "bundle")
            {
                Directory.Move(Path.Combine(s.Session, "bundle"), Path.Combine(s.Outside, "original-bundle"));
                DirectoryLink(Path.Combine(s.Session, "bundle"), Path.Combine(s.Outside, "original-bundle"));
            }
            else
            {
                var link = location == "manifest" ? s.Manifest : location == "snapshot" ? s.Snapshot(0)
                    : s.Live(location == "native-file" ? "native/save.txt" : "runtime/settings.json");
                MakeWritable(link); File.Delete(link);
                if (!FileLink(link, victim)) return;
            }
            Throws<InvalidDataException>(() => SaveTestBundle.Restore(s.Session));
            Throws<InvalidDataException>(() => SaveTestBundle.Restore(s.Session, true));
            if (location != "native-file") Equal(File.ReadAllText(s.Live("native/save.txt")), "current-output", "reparse preflight prevents destination writes");
            if (location != "archive") NoArchive(s);
        }
        Equal(File.ReadAllText(victim), "untouched-target", "reparse target never modified");
    }

    private static void ExcludedLink(Scenario s)
    {
        s.Put(Path.Combine(s.Outside, "victim.txt"), "untouched");
        DirectoryLink(Path.Combine(s.Runtime, "commands"), s.Outside);
        DirectoryLink(Path.Combine(s.Native, "profiles"), s.Outside);
        s.Capture();
        Assert(SaveTestBundle.Restore(s.Session)["verified"].AsBool, "excluded links are never traversed or restored");
        Equal(File.ReadAllText(Path.Combine(s.Outside, "victim.txt")), "untouched", "excluded link target untouched");
    }

    private static int ResumeChild(string[] args)
    {
        if (args.Length != 6 || args[0] != "--resume-child")
        {
            Console.Error.WriteLine("Expected --resume-child <runtime> <native> <session> <manifest-sha256> <resume|restore|prepare>.");
            return 2;
        }
        var resumed = false;
        try
        {
            // This really is a new process, not a mocked or reflection-reset trust registry.
            Throws<InvalidOperationException>(() => SaveTestBundle.Restore(args[3]));
            var result = SaveTestBundle.Resume(args[1], args[2], args[3], args[4]);
            resumed = true;
            Assert(!result["alreadyTrusted"].AsBool, "cold child starts without trust");
            Assert(SaveTestBundle.Resume(args[1], args[2], args[3], args[4])["alreadyTrusted"].AsBool, "matching resume is idempotent");
            if (args[5] == "restore") result["restoration"] = SaveTestBundle.Restore(args[3]);
            else if (args[5] == "prepare") result["fixture"] = SaveTestBundle.PrepareFixture(args[3], "default");
            else if (args[5] != "resume") throw new ArgumentException("Unknown child action.");
            Console.WriteLine(result.ToString());
            return 0;
        }
        catch (Exception error)
        {
            var result = new JSONObject();
            result["verified"] = false;
            result["error"] = error.GetType().Name;
            result["message"] = error.Message;
            var registered = resumed;
            if (!resumed)
            {
                try { SaveTestBundle.Restore(args[3]); registered = true; }
                catch (InvalidOperationException) { }
                catch (Exception) { registered = true; }
            }
            result["registered"] = registered;
            Console.WriteLine(result.ToString());
            return 1;
        }
    }

    private static JSONObject RunResumeChild(string runtime, string native, string session, string pin, string action)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(BundleChecks).Assembly.Location);
        foreach (var argument in new[] { "--resume-child", runtime, native, session, pin, action }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start))
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000)) { process.Kill(true); throw new TimeoutException("Cold resume child timed out."); }
            var text = stdout.GetAwaiter().GetResult();
            var errors = stderr.GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Cold child returned no JSON: " + errors);
            var result = JsonFiles.ParseObject(text);
            result["exitCode"] = process.ExitCode;
            result["stderr"] = errors;
            return result;
        }
    }

    private static void ChildRejected(JSONObject child)
    {
        Equal(child["exitCode"].AsInt, 1, "cold resume rejected invalid session: " + child);
        Assert(child["verified"].IsBoolean && !child["verified"].AsBool, "invalid cold session never verified");
        Assert(child["registered"].IsBoolean && !child["registered"].AsBool, "failed cold resume grants no Restore authority");
    }

    private static void DirectoryLink(string link, string target)
    {
        if (Path.DirectorySeparatorChar != '\\') { Directory.CreateSymbolicLink(link, target); return; }
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + target + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        {
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException("Junction creation failed: " + stdout + stderr);
        }
        Assert((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "real directory junction created");
    }

    private static bool FileLink(string link, string target)
    {
        try { File.CreateSymbolicLink(link, target); }
        catch (UnauthorizedAccessException)
        {
            skipped++;
            Console.WriteLine("SKIP: file symlink privilege unavailable; directory junction checks still run.");
            return false;
        }
        Assert((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "real file symlink created");
        return true;
    }

    private static void CheckArchive(JSONObject report, Dictionary<string, byte[]> expected)
    {
        var directory = report["archiveDirectory"].Value;
        var record = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        Equal(record["files"].Count, expected.Count, "archive manifest file set");
        foreach (var row in record["files"].Children)
        {
            var key = row["area"].Value + "/" + row["path"].Value;
            Assert(expected.ContainsKey(key), "no unexpected archive path " + key);
            var path = Path.Combine(directory, key.Replace('/', Path.DirectorySeparatorChar));
            Bytes(File.ReadAllBytes(path), expected[key], "archived current bytes " + key);
            Equal(row["sha256"].Value, Hash(expected[key]), "archive SHA-256 " + key);
            Assert(ReadOnly(path), "archive output is read-only " + key);
        }
    }

    private static void NoArchive(Scenario s)
    {
        Assert(!Directory.Exists(Path.Combine(s.Session, "test-outputs")), "preflight failure creates no archive");
    }

    private static void Sized(string path, long length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) stream.SetLength(length);
    }

    private static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static bool ReadOnly(string path) { return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0; }
    private static void MakeWritable(string path) { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); }
    private static Dictionary<string, byte[]> TreeBytes(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p.Substring(root.Length + 1), File.ReadAllBytes);
    }
    private static Dictionary<string, byte[]> RetiredBytes(Scenario s)
    {
        var keys = s.Retired.Keys.ToList();
        var tree = Path.Combine(s.Runtime, "finite-actions-v1");
        if (Directory.Exists(tree)) keys.AddRange(Directory.GetFiles(tree, "*", SearchOption.AllDirectories)
            .Select(p => "runtime/" + p.Substring(s.Runtime.Length + 1).Replace(Path.DirectorySeparatorChar, '/')));
        return keys.Distinct(StringComparer.OrdinalIgnoreCase).Where(k => File.Exists(s.Live(k)))
            .ToDictionary(k => k, k => File.ReadAllBytes(s.Live(k)), StringComparer.OrdinalIgnoreCase);
    }
    private static void EqualTrees(Dictionary<string, byte[]> actual, Dictionary<string, byte[]> expected, string message)
    {
        Equal(actual.Count, expected.Count, message + " file count");
        foreach (var file in expected)
        {
            Assert(actual.ContainsKey(file.Key), message + " file exists " + file.Key);
            Bytes(actual[file.Key], file.Value, message + " bytes " + file.Key);
        }
    }
    private static void Bytes(byte[] actual, byte[] expected, string message) { Assert(actual.SequenceEqual(expected), message); }
    private static void Equal<T>(T actual, T expected, string message)
    {
        Assert(EqualityComparer<T>.Default.Equals(actual, expected), message + ": expected " + expected + ", got " + actual);
    }
    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        assertions++;
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Run(string name, Action<Scenario> test)
    {
        cases++;
        try { using (var scenario = new Scenario()) test(scenario); Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + name + "\n" + error); }
    }

    private sealed class Scenario : IDisposable
    {
        internal readonly string Root, Runtime, Native, Session, Outside;
        internal readonly Dictionary<string, byte[]> Original = new Dictionary<string, byte[]>();
        internal readonly Dictionary<string, byte[]> Retired = new Dictionary<string, byte[]>();
        internal readonly List<string> Excluded = new List<string>();
        internal string Manifest { get { return Path.Combine(Session, "bundle", "manifest.json"); } }
        internal Scenario()
        {
            var parent = Path.Combine(Path.GetTempPath(), "opencode");
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Pre-approved test temporary parent is missing: " + parent);
            Root = Path.Combine(parent, "save-bundle-checks-" + Guid.NewGuid().ToString("N"));
            Runtime = Path.Combine(Root, "runtime"); Native = Path.Combine(Root, "native"); Outside = Path.Combine(Root, "outside");
            Session = Path.Combine(Runtime, "save-safety", "tests", "session");
            Directory.CreateDirectory(Runtime); Directory.CreateDirectory(Native); Directory.CreateDirectory(Outside);
        }
        internal JSONObject Capture() { return SaveTestBundle.Capture(Runtime, Native, Session); }
        internal JSONObject WriteLegacyBundle()
        {
            // Independent historical v1 writer: establish trust only through explicit Resume pins,
            // never by editing a captured manifest or resetting the production trust registry.
            var manifest = new JSONObject();
            manifest["version"] = 1;
            manifest["runtimeRoot"] = Runtime;
            manifest["nativeRoot"] = Native;
            manifest["sessionDirectory"] = Session;
            var rows = new JSONArray();
            Directory.CreateDirectory(Path.Combine(Session, "bundle", "files"));
            foreach (var file in Original.Concat(Retired).OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
            {
                var snapshot = Snapshot(rows.Count);
                File.WriteAllBytes(snapshot, file.Value);
                File.SetAttributes(snapshot, File.GetAttributes(snapshot) | FileAttributes.ReadOnly);
                var slash = file.Key.IndexOf('/');
                var row = new JSONObject();
                row["area"] = file.Key.Substring(0, slash);
                row["path"] = file.Key.Substring(slash + 1);
                row["length"] = file.Value.LongLength;
                row["sha256"] = Hash(file.Value);
                rows.Add(row);
            }
            manifest["files"] = rows;
            File.WriteAllText(Manifest, manifest.ToString(2), Utf8);
            File.SetAttributes(Manifest, File.GetAttributes(Manifest) | FileAttributes.ReadOnly);
            return manifest;
        }
        internal string Snapshot(int index) { return Path.Combine(Session, "bundle", "files", index.ToString("D6") + ".bin"); }
        internal string Live(string key)
        {
            var slash = key.IndexOf('/');
            return Path.Combine(key.Substring(0, slash) == "runtime" ? Runtime : Native, key.Substring(slash + 1).Replace('/', Path.DirectorySeparatorChar));
        }
        internal byte[] PutKey(string key, string text) { return Put(Live(key), text); }
        internal byte[] Put(string path, string text)
        {
            var bytes = Utf8.GetBytes(text);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); return bytes;
        }
        internal void Seed()
        {
            foreach (var key in new[] { "runtime/settings.json", "runtime/presets.json", "runtime/zoneOverride.json", "runtime/allocation.json",
                "runtime/stage-progress-x.json", "runtime/future-execution.json",
                "runtime/NGUSave.txt", "runtime/profiles/default.json", "runtime/profiles/deep/opaque.bin", "runtime/profiles/.hidden",
                "runtime/custom-plans/default.json", "native/save.txt", "native/AutoSave.TXT" })
                Original[key] = PutKey(key, key == "runtime/settings.json"
                    ? "\uFEFF{\"_autoRebirth\":true,\"_customPlanFile\":\"production\",\"_customAutoExpEnabled\":true,\"_customAutoMoneyPitEnabled\":true,\"nativeConfig\":{\"integer\":9007199254740993,\"array\":[1,true,\"keep\"]}}"
                    : "original-" + key);
            foreach (var key in new[] { "runtime/finite-actions-v1/action.claim", "runtime/finite-actions-v1/preparation/opaque.bin",
                "runtime/finite-actions-v1/standalone/no-extension", "runtime/finite-actions-v1/nested/action.json",
                "runtime/execution-selection.json", "runtime/execution-manual-selection.json", "runtime/execution-quick-selection.json",
                "runtime/execution-fight-prep-selection.json", "runtime/execution-F8-selection.JSON", "runtime/route-execution-v1.json",
                "runtime/lifecycle-route-commitments-v1.json", "runtime/combat-prep-os.json" })
            {
                Retired[key] = PutKey(key, key.EndsWith(".bin", StringComparison.Ordinal) ? "\0\u0001opaque-retired-bytes" : "{malformed retired " + key);
                Excluded.Add(Live(key));
            }
            foreach (var key in new[] { "runtime/runtime.json", "runtime/NGUSave.json", "runtime/probe.txt", "runtime/probe.autorun",
                "runtime/cloud-load.autorun", "runtime/external-ui.enabled", "runtime/loader-startup.log", "runtime/other.txt",
                "runtime/commands/claimed/id.accepted.json", "runtime/command-results/id.json", "runtime/logs/test.json",
                "runtime/save-safety/recovery.json", "runtime/lease/claim.json", "native/logs/log.txt", "native/profiles/save.txt", "native/settings.json", "native/output.log" })
            {
                var path = Live(key); Put(path, "original-control"); Excluded.Add(path);
            }
        }
        public void Dispose() { DeleteTree(Root); }
        private static void DeleteTree(string path)
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) == 0) DeleteTree(child);
                    else Directory.Delete(child);
                }
                else { MakeWritable(child); File.Delete(child); }
            }
            Directory.Delete(path);
        }
    }
}
