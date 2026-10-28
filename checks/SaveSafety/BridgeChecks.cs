// Generate trusted synthetic fixtures with Fixtures/FixtureGenerator.csproj first.
// Run: dotnet run --project checks/SaveSafety/BridgeChecks.csproj -- <synthetic-fixture-directory>
// No game installation, live save path, production build, or cloud access is used.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NGUInjector;
using SimpleJSON;
using InjectorMain = NGUInjector.Main;

internal static class BridgeChecks
{
    private static int cases, assertions, failures;

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Arguments: <synthetic-fixture-directory>; generate with Fixtures/FixtureGenerator.csproj first.");
            return 2;
        }
        string fixtures = Path.GetFullPath(args[0]);
        try
        {
            Equal(SaveSafetyCatalog.Read(Path.Combine(fixtures, "valid.ok.txt"), 1260).PlayerName, "Synthetic Player", "trusted fixture player");
            Equal(SaveSafetyCatalog.Read(Path.Combine(fixtures, "bridge", "earlier.ok.txt"), 1260).TotalPlaytimeSeconds, 100d, "bridge fixtures generated");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Fixture setup failed: " + error);
            return 2;
        }

        Run(fixtures, "partial startup reports unknown without breaking runtime publication", PartialStartup);
        Run(fixtures, "capture writes decoded live identity, read-back hash, and manifest", Capture);
        Run(fixtures, "capture command returns the real verified backup", CaptureCommand);
        foreach (string field in new[] { "total", "elapsed", "rebirth", "name" })
            Run(fixtures, "capture rejects mismatched " + field, s => CaptureMismatch(s, field));
        foreach (string file in new[] { "wrong-checksum.bad.txt", "malformed-outer-base64.bad.txt", "truncated-outer-nrbf.bad.txt", "empty-file.bad.txt" })
            Run(fixtures, "capture rejects corrupt export " + file, s => CorruptExport(s, file));
        Run(fixtures, "throwing native export restores lastTime", ExportThrows);
        Run(fixtures, "filesystem capture failure restores lastTime", CaptureWriteFails);
        foreach (string field in new[] { "total-negative", "total-nan", "total-infinity", "elapsed-negative", "elapsed-nan", "elapsed-infinity", "elapsed-over-total", "rebirth-negative" })
            Run(fixtures, "invalid live identity blocks export " + field, s => InvalidCurrent(s, field));

        Run(fixtures, "scan decodes one candidate per tick in known top directories only", IncrementalScan);
        Run(fixtures, "scan caches unchanged files and force rescans them", ScanCaching);
        Run(fixtures, "discovery exceeding 4096 files blocks completion", DiscoveryLimit);
        Run(fixtures, "quicksave cannot bypass the 4096-candidate bound", QuicksaveLimit);
        Run(fixtures, "pending background scan blocks load before protective export", PendingScan);
        Run(fixtures, "cloud authorization synchronously finishes an initially pending valid scan", PendingPublication);
        Run(fixtures, "corrupt candidate blocks a complete scan", CorruptScan);
        Run(fixtures, "missing native directory blocks a complete scan", MissingDirectory);
        Run(fixtures, "disappeared save retains its known high-water mark", DisappearedSave);
        Run(fixtures, "failed reread retains its known high-water mark", FailedReread);
        Run(fixtures, "unchanged failed reread cannot become complete on cached rescan", FailedRereadStaysIncomplete);
        Run(fixtures, "valid lower reread retains durable high-water across restart", LowerReread);
        foreach (string kind in new[] { "new", "changed", "preserved-mtime" })
            Run(fixtures, "fresh cloud authorization immediately discovers " + kind + " candidate", s => PublicationRefresh(s, kind));
        foreach (string source in new[] { "scan", "capture" })
            Run(fixtures, "failed high-water persistence retains observed maximum and retries " + source, s => HighWaterWriteFails(s, source));
        Run(fixtures, "freshly captured backup permits immediate publication authorization", CapturePublication);
        if (Path.DirectorySeparatorChar == '\\')
        {
            foreach (bool deleted in new[] { false, true })
                Run(fixtures, deleted ? "deleted mixed-slash capture retains high-water and blocks publication"
                    : "mixed-slash capture survives forced scan and authorizes publication", s => MixedSlashCapturePublication(s, deleted));
        }
        else Console.WriteLine("SKIP: mixed-slash capture regressions require Windows path semantics.");
        Run(fixtures, "longest selection uses decoded clocks not file names or mtimes", Selection);
        Run(fixtures, "identical byte copies tie without ambiguity", IdenticalTie);
        Run(fixtures, "different-hash tie blocks native loading after protection", AmbiguousTie);
        foreach (string kind in new[] { "lower", "deleted", "lower-then-deleted" })
            Run(fixtures, "historical tied branch remains ambiguous after restart " + kind, s => HistoricalTie(s, kind));
        Run(fixtures, "foreign player name blocks native loading after protection", ForeignPlayer);
        Run(fixtures, "current already latest rejects tied branches without reload or reset", AlreadyLatest);
        Run(fixtures, "unambiguous current already latest never reloads or resets", AlreadyLatestUnambiguous);
        foreach (string kind in new[] { "hash", "session", "extra", "number-hash", "missing" })
            Run(fixtures, "stale or malformed load request is rejected " + kind, s => InvalidRequest(s, kind));
        foreach (string kind in new[] { "changed-hash", "corrupt", "deleted" })
            Run(fixtures, "selected bytes changed between scan and load " + kind, s => SelectedChanged(s, kind));
        foreach (string kind in new[] { "higher", "corrupt" })
            Run(fixtures, "nonselected unchanged-stamp candidate refreshed before load " + kind, s => NonselectedChanged(s, kind));
        foreach (string kind in new[] { "changed-hash", "corrupt", "deleted", "oversized" })
            Run(fixtures, "selected bytes changed after preflight retain backup " + kind, s => SelectedChangedAfterPreflight(s, kind));
        foreach (string kind in new[] { "null", "throw" })
            Run(fixtures, "native decoder rejection cannot quiesce or load " + kind, s => DecoderRejects(s, kind));
        foreach (string phase in new[] { "preparing", "loading" })
            Run(fixtures, "durable journal write failure prevents native load " + phase, s => JournalWriteFails(s, phase));
        Run(fixtures, "successful native load protects, quiesces, validates, resets, and journals", SuccessfulLoad);
        foreach (string kind in new[] { "throw", "partial", "noop", "wrong-rebirth", "short-elapsed", "wrong-start", "invalid-clock", "wrong-player" })
            Run(fixtures, "unverified native load remains quiesced with durable recovery " + kind, s => FailedLoad(s, kind));
        foreach (string phase in new[] { "preparing", "loading", "recoveryRequired", "unknown-phase" })
            Run(fixtures, "interrupted journal never becomes verified " + phase, s => InterruptedJournal(s, phase));
        Run(fixtures, "malformed interrupted journal never becomes verified", MalformedJournal);
        Run(fixtures, "completed journal permits verified status after restart", CompletedJournal);

        Console.WriteLine((failures == 0 ? "PASS" : "FAIL") + ": " + cases + " bridge cases; " + assertions + " assertions; " + failures + " failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void PartialStartup(Scenario s)
    {
        var partial = (InjectorMain)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(InjectorMain));
        var runtime = partial.Runtime();
        Equal(runtime["status"].Value, "unknown", "an uninitialized native fixture is not verified");
        Assert(!runtime["scanComplete"].AsBool, "partial startup cannot publish a complete scan");
        Assert(runtime["errors"].IsArray && runtime["errors"].Count == 0, "unavailable observation is not a fabricated decode error");
        Assert(!runtime["liveFixtureTestingSupported"].AsBool, "partial startup never grants fixture permission");
        partial.Tick();
        Equal(s.Character.importExport.ExportCalls, 0, "partial telemetry does not export or load a save");
    }

    private static void Capture(Scenario s)
    {
        s.SetCurrent("valid.ok.txt");
        var identity = s.Main.Capture();
        Equal(s.Character.lastTime, Scenario.OriginalLastTime, "original lastTime restored");
        Equal(s.ExportLastTime, Epoch.Now, "native export observes synchronous stamp");
        Equal(identity.LastTime, Epoch.Now, "decoded stamp matches synchronous export");
        Equal(s.Backups.Length, 1, "one protected local save");
        Equal(identity.Path, s.Backups[0], "returned path is protected save");
        Equal(File.ReadAllText(identity.Path), s.ExportText, "native export bytes are retained exactly");
        Equal(identity.Sha256, Hash(File.ReadAllBytes(identity.Path)), "independent disk SHA-256");
        var readback = SaveSafetyCatalog.Read(identity.Path, 1260);
        SameIdentity(readback, s.CurrentFixture, "decoded live capture");
        Equal(readback.Sha256, identity.Sha256, "returned read-back hash");
        var manifest = JsonFiles.ParseObject(File.ReadAllText(identity.Path + ".json"));
        Equal(manifest["path"].Value, identity.Path, "manifest path");
        Equal(manifest["sha256"].Value, readback.Sha256, "manifest hash");
        Equal(manifest["playerName"].Value, s.Character.playerName, "manifest player");
        Equal(manifest["totalPlaytimeSeconds"].Value, "1000.25", "manifest exact total");
        Equal(manifest["rebirthSeconds"].Value, "60.5", "manifest exact elapsed");
        Equal(manifest["rebirth"].Value, "7", "manifest rebirth");
        Assert(DateTime.TryParse(manifest["capturedAt"].Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _), "manifest capture timestamp");
        Equal(s.Main.Runtime()["knownFiles"].AsInt, 1, "capture registered with real catalog");
        Equal(s.Character.importExport.ExportCalls, 1, "one synchronous export");
        NoNativeLoad(s);
    }

    private static void CaptureCommand(Scenario s)
    {
        JSONNode data;
        string message = s.Main.Command("captureLocalSave", null, out data);
        Equal(data["save"]["sha256"].Value, Hash(File.ReadAllBytes(s.Backups.Single())), "command returns read-back identity");
        Assert(message.Contains("No cloud write", StringComparison.Ordinal), "capture explicitly reports local-only result");
        Equal(s.Character.lastTime, Scenario.OriginalLastTime, "command restores native time");
        NoNativeLoad(s);
    }

    private static void CaptureMismatch(Scenario s, string field)
    {
        s.SetCurrent("valid.ok.txt");
        if (field == "total") s.Character.totalPlaytime.totalseconds += 0.01;
        if (field == "elapsed") s.Character.rebirthTime.totalseconds += 0.01;
        if (field == "rebirth") s.Character.stats.rebirthNumber++;
        if (field == "name") s.Character.playerName = "synthetic player";
        Throws<InvalidOperationException>(() => s.Main.Capture(), "did not match");
        Equal(s.Character.lastTime, Scenario.OriginalLastTime, "mismatch restores lastTime");
        Equal(s.Backups.Length, 0, "mismatched export not persisted");
        NoNativeLoad(s);
    }

    private static void CorruptExport(Scenario s, string file)
    {
        s.ExportText = File.ReadAllText(Path.Combine(s.Fixtures, file));
        Throws<InvalidDataException>(() => s.Main.Capture());
        Equal(s.Character.lastTime, Scenario.OriginalLastTime, "corrupt export restores lastTime");
        Equal(s.Backups.Length, 0, "corrupt export not persisted");
        NoNativeLoad(s);
    }

    private static void ExportThrows(Scenario s)
    {
        s.Character.importExport.Export = () =>
        {
            Equal(s.Character.lastTime, Epoch.Now, "stamp set before throwing export");
            throw new IOException("synthetic export failure");
        };
        Throws<IOException>(() => s.Main.Capture(), "synthetic export failure");
        Equal(s.Character.lastTime, Scenario.OriginalLastTime, "throwing export restores lastTime");
        Equal(s.Backups.Length, 0, "throwing export creates no backup");
        NoNativeLoad(s);
    }

    private static void CaptureWriteFails(Scenario s)
    {
        Directory.CreateDirectory(s.Safety);
        File.WriteAllText(Path.Combine(s.Safety, "backups"), "directory obstruction");
        Throws<IOException>(() => s.Main.Capture());
        Equal(s.Character.lastTime, Scenario.OriginalLastTime, "write failure restores lastTime");
        Equal(s.Main.Runtime()["knownFiles"].AsInt, 0, "failed write not catalogued");
        NoNativeLoad(s);
    }

    private static void InvalidCurrent(Scenario s, string field)
    {
        if (field == "total-negative") s.Character.totalPlaytime.totalseconds = -1;
        if (field == "total-nan") s.Character.totalPlaytime.totalseconds = double.NaN;
        if (field == "total-infinity") s.Character.totalPlaytime.totalseconds = double.PositiveInfinity;
        if (field == "elapsed-negative") s.Character.rebirthTime.totalseconds = -1;
        if (field == "elapsed-nan") s.Character.rebirthTime.totalseconds = double.NaN;
        if (field == "elapsed-infinity") s.Character.rebirthTime.totalseconds = double.PositiveInfinity;
        if (field == "elapsed-over-total") s.Character.rebirthTime.totalseconds = 101;
        if (field == "rebirth-negative") s.Character.stats.rebirthNumber = -1;
        Throws<InvalidOperationException>(() => s.Main.Capture(), "invalid");
        Equal(s.Character.importExport.ExportCalls, 0, "invalid live state rejected before native export");
        Equal(s.Character.lastTime, Scenario.OriginalLastTime, "invalid live state never stamped");
        Equal(s.Backups.Length, 0, "invalid live state not persisted");
    }

    private static void IncrementalScan(Scenario s)
    {
        s.NativeFile("a.txt", "bridge/earlier.ok.txt");
        s.NativeFile("z.txt", "valid.ok.txt");
        s.Write(Path.Combine(s.Safety, "backups", "b.txt"), "bridge/earlier.ok.txt");
        s.Write(Path.Combine(s.RuntimeDirectory, "NGUSave.txt"), "valid.ok.txt");
        s.Write(Path.Combine(s.NativeDirectory, "nested", "hidden.txt"), "bridge/other-player.ok.txt");
        s.NativeFile("ignored.json", "bridge/tied-branch.ok.txt");
        s.Main.Scan(true);
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, 4, "only four top-level candidates discovered");
        Equal(s.Main.Runtime()["knownFiles"].AsInt, 0, "discovery does not synchronously decode");
        for (int i = 1; i <= 4; i++)
        {
            s.Main.Tick();
            var runtime = s.Main.Runtime();
            Equal(runtime["knownFiles"].AsInt, i, "one file decoded per tick " + i);
            Equal(runtime["pendingFiles"].AsInt, 4 - i, "pending count " + i);
            Equal(runtime["scanComplete"].AsBool, i == 4, "completion only after final candidate " + i);
        }
        Equal(s.Character.importExport.ExportCalls, 0, "scan has no native export");
        NoNativeLoad(s);
    }

    private static void ScanCaching(Scenario s)
    {
        string path = s.NativeFile("save.txt", "bridge/earlier.ok.txt");
        s.ScanAll();
        s.Main.Scan(false);
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, 0, "unchanged stamp skips decode");
        Assert(s.Main.Runtime()["scanComplete"].AsBool, "unchanged catalog remains complete");
        var previous = File.GetLastWriteTimeUtc(path);
        s.Write(path, "valid.ok.txt");
        File.SetLastWriteTimeUtc(path, previous.AddMinutes(1));
        s.Main.Scan(false);
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, 1, "changed stamp schedules reread");
        s.Main.Tick();
        Equal(s.Main.Runtime()["longest"]["totalPlaytimeSeconds"].Value, "1000.25", "changed file reread through real codec");
        s.Main.Scan(true);
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, 1, "forced scan ignores cached stamps");
        Assert(!s.Main.Runtime()["scanComplete"].AsBool, "forced scan incomplete until processed");
    }

    private static void DiscoveryLimit(Scenario s)
    {
        for (int i = 0; i < SaveSafetyCatalog.MaxCandidates + 1; i++) s.NativeFile(i.ToString("D4") + ".txt", "bridge/earlier.ok.txt");
        s.Main.Scan(true);
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, SaveSafetyCatalog.MaxCandidates, "discovery bounded at 4096");
        Assert(s.Main.Runtime()["errors"].Count > 0, "overflow retained as scan error");
        Assert(!s.Main.Runtime()["scanComplete"].AsBool, "overflow never complete");
        Throws<InvalidOperationException>(() => s.Load("not-selected"), "complete save scan");
        Equal(s.Character.importExport.ExportCalls, 0, "overflow cannot trigger protective export or load");
    }

    private static void QuicksaveLimit(Scenario s)
    {
        for (int i = 0; i < SaveSafetyCatalog.MaxCandidates; i++) s.NativeFile(i.ToString("D4") + ".txt", "bridge/earlier.ok.txt");
        s.Write(Path.Combine(s.RuntimeDirectory, "NGUSave.txt"), "valid.ok.txt");
        s.Main.Scan(true);
        var runtime = s.Main.Runtime();
        Assert(runtime["pendingFiles"].AsInt <= SaveSafetyCatalog.MaxCandidates,
            "quicksave must share candidate bound; pending=" + runtime["pendingFiles"].AsInt);
        Assert(runtime["errors"].Count > 0, "quicksave overflow cannot silently drop a candidate");
    }

    private static void PendingScan(Scenario s)
    {
        s.NativeFile("latest.txt", "valid.ok.txt");
        s.Main.Scan(true);
        Throws<InvalidOperationException>(() => s.Load(Hash(File.ReadAllBytes(Path.Combine(s.Fixtures, "valid.ok.txt")))), "complete save scan");
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, 1, "load rejected before preflight rather than draining incomplete background scan");
        Equal(s.Character.importExport.ExportCalls, 0, "pending scan rejected before protective export");
        Equal(s.Backups.Length, 0, "pending command has no file side effects");
        NoNativeLoad(s);
    }

    private static void PendingPublication(Scenario s)
    {
        s.SetCurrent("valid.ok.txt");
        string path = s.NativeFile("latest.txt", "valid.ok.txt");
        s.Main.Scan(true);
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, 1, "background inspection initially pending");
        s.Main.RequirePublication();
        var runtime = s.Main.Runtime();
        Equal(runtime["pendingFiles"].AsInt, 0, "publication finishes verification synchronously");
        Assert(runtime["scanComplete"].AsBool, "publication requires freshly complete scan");
        Equal(runtime["status"].Value, "verified", "matching live state authorized after actual byte inspection");
        Equal(runtime["longest"]["sha256"].Value, Hash(File.ReadAllBytes(path)), "publication exposes real inspected hash");
        Equal(s.Character.importExport.ExportCalls, 0, "finishing inspection does not export or load native state");
        NoNativeLoad(s);
    }

    private static void CorruptScan(Scenario s)
    {
        s.SetCurrent("valid.ok.txt");
        s.NativeFile("a.txt", "valid.ok.txt");
        string bad = s.NativeFile("z.txt", "wrong-checksum.bad.txt");
        var runtime = s.ScanAll();
        Assert(!runtime["scanComplete"].AsBool, "decode error blocks completion");
        Equal(runtime["knownFiles"].AsInt, 1, "corrupt save not catalogued");
        Equal(runtime["errors"][0]["path"].Value, bad, "decode error identifies exact file");
        Equal(runtime["status"].Value, "unknown", "incomplete scan cannot claim verified");
        Throws<InvalidOperationException>(() => s.Load(), "complete save scan");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
    }

    private static void MissingDirectory(Scenario s)
    {
        Directory.Delete(s.NativeDirectory);
        var runtime = s.ScanAll();
        Equal(runtime["errors"][0]["path"].Value, s.NativeDirectory, "missing native directory reported");
        Assert(!runtime["scanComplete"].AsBool, "unavailable native directory blocks completion");
        Throws<InvalidOperationException>(() => s.Load("unused"), "complete save scan");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
    }

    private static void DisappearedSave(Scenario s)
    {
        string selected = s.PrepareLatest();
        string hash = s.Main.Runtime()["longest"]["sha256"].Value;
        File.Delete(selected);
        var runtime = s.ScanAll();
        Equal(runtime["longest"]["sha256"].Value, hash, "deleted save high-water identity retained");
        Equal(runtime["longest"]["totalPlaytimeSeconds"].Value, "1000.25", "deleted high-water playtime retained");
        Assert(!runtime["scanComplete"].AsBool, "deleted known file blocks completion");
        Equal(runtime["status"].Value, "behind", "current remains behind disappeared save");
        Throws<InvalidOperationException>(() => s.Load(hash), "complete save scan");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
    }

    private static void FailedReread(Scenario s)
    {
        string path = s.PrepareLatest();
        string hash = s.Main.Runtime()["longest"]["sha256"].Value;
        s.Write(path, "wrong-checksum.bad.txt");
        var runtime = s.ScanAll();
        Equal(runtime["longest"]["sha256"].Value, hash, "failed reread cannot lower known high-water mark");
        Assert(!runtime["scanComplete"].AsBool, "failed reread blocks completion");
        Equal(runtime["status"].Value, "behind", "failed reread does not hide newer progress");
        Throws<InvalidOperationException>(() => s.Load(hash), "complete save scan");
    }

    private static void Selection(Scenario s)
    {
        string olderName = s.NativeFile("ancient-0000.txt", "valid.ok.txt");
        string newerName = s.NativeFile("LATEST-9999.txt", "bridge/earlier.ok.txt");
        File.SetLastWriteTimeUtc(olderName, new DateTime(2000, 1, 1));
        File.SetLastWriteTimeUtc(newerName, new DateTime(2030, 1, 1));
        var runtime = s.ScanAll();
        Equal(runtime["longest"]["path"].Value, olderName, "decoded total outranks file name and mtime");
        Equal(runtime["longest"]["sha256"].Value, Hash(File.ReadAllBytes(olderName)), "selected hash belongs to selected bytes");
        Equal(runtime["longest"]["playerName"].Value, "Synthetic Player", "decoded player name exposed");
        Equal(runtime["current"]["totalPlaytimeSeconds"].Value, "100", "live identity exposed");
        Equal(runtime["status"].Value, "behind", "behind current is not green");
        Assert(!runtime["ambiguousLongest"].AsBool, "unique longest identity");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
    }

    private static void FailedRereadStaysIncomplete(Scenario s)
    {
        s.SetCurrent("valid.ok.txt");
        string path = s.PrepareLatest();
        s.Write(path, "wrong-checksum.bad.txt");
        Assert(!s.ScanAll()["scanComplete"].AsBool, "failed reread initially incomplete");
        s.Main.Scan(false);
        var runtime = s.Main.Runtime();
        Assert(!runtime["scanComplete"].AsBool, "unchanged corrupt file must not inherit previous successful decode; status=" + runtime["status"].Value);
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, 0, "failed authorization completed corrupt source inspection rather than leaving queued work");
        Assert(!s.Main.Runtime()["scanComplete"].AsBool, "synchronously reread corrupt source remains incomplete");
        Throws<InvalidOperationException>(() => s.Load(), "complete save scan");
    }

    private static void LowerReread(Scenario s)
    {
        s.SetCurrent("valid.ok.txt");
        string path = s.PrepareLatest();
        var highWaterPath = Path.Combine(s.Safety, "high-water.json");
        string retained = File.ReadAllText(highWaterPath);
        var original = JsonFiles.ParseObject(retained);
        Equal(original["path"].Value, path, "durable high-water path");
        Equal(original["sha256"].Value, Hash(File.ReadAllBytes(path)), "durable high-water original hash");
        Equal(original["totalPlaytimeSeconds"].Value, "1000.25", "durable maximum before regression");
        s.Write(path, "bridge/earlier.ok.txt");
        Equal(SaveSafetyCatalog.Read(path, 1260).TotalPlaytimeSeconds, 100d, "replacement is valid but earlier, not corrupt");
        var runtime = s.ScanAll();
        Assert(!runtime["scanComplete"].AsBool, "valid lower reread cannot complete scan");
        Equal(runtime["status"].Value, "unknown", "matching live total cannot green a regressed file");
        Equal(runtime["longest"]["sha256"].Value, original["sha256"].Value, "lower reread retains maximum identity");
        Equal(runtime["errors"][0]["path"].Value, path, "regression identifies replaced maximum");
        Equal(File.ReadAllText(highWaterPath), retained, "lower reread does not overwrite durable maximum");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        s.Restart();
        Equal(s.Main.Runtime()["longest"]["totalPlaytimeSeconds"].Value, "1000.25", "maximum restored before any startup scan");
        runtime = s.ScanAll();
        Assert(!runtime["scanComplete"].AsBool, "regression remains blocking after restart");
        Equal(runtime["status"].Value, "unknown", "restart cannot green regressed evidence");
        Equal(runtime["longest"]["sha256"].Value, original["sha256"].Value, "restart retains pre-regression hash");
        Equal(File.ReadAllText(highWaterPath), retained, "restart reread preserves persisted maximum");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        Throws<InvalidOperationException>(() => s.Load(), "complete save scan");
        s.SetCurrent("bridge/earlier.ok.txt");
        Equal(s.Main.Runtime()["status"].Value, "behind", "lower current still compared against historical maximum");
        Equal(s.Backups.Length, 0, "high-water inspection never exports");
        NoNativeLoad(s);
    }

    private static void PublicationRefresh(Scenario s, string kind)
    {
        string path = s.NativeFile("existing.txt", "bridge/earlier.ok.txt");
        Equal(s.ScanAll()["status"].Value, "verified", "initial current matches known maximum");
        s.Main.RequirePublication();
        if (kind == "new") path = s.NativeFile("newer.txt", "valid.ok.txt");
        else if (kind == "preserved-mtime")
        {
            s.ReplaceKeepingStamp(path, File.ReadAllBytes(Path.Combine(s.Fixtures, "valid.ok.txt")));
            s.Main.Scan(false);
            Equal(s.Main.Runtime()["pendingFiles"].AsInt, 0, "background stamp optimization deliberately misses same-stamp replacement");
        }
        else
        {
            var previous = File.GetLastWriteTimeUtc(path);
            s.Write(path, "valid.ok.txt");
            File.SetLastWriteTimeUtc(path, previous.AddMinutes(1));
        }
        Equal(s.Main.Runtime()["status"].Value, "verified", "cached status predates changed discovery");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        var runtime = s.Main.Runtime();
        Equal(runtime["pendingFiles"].AsInt, 0, "authorization synchronously inspects every candidate");
        Assert(runtime["scanComplete"].AsBool, "valid changed candidates finish inspection before authorization returns");
        Equal(runtime["status"].Value, "behind", "freshly decoded maximum immediately blocks publication");
        Equal(runtime["longest"]["path"].Value, path, "authorization selects actual changed candidate");
        Equal(runtime["longest"]["sha256"].Value, Hash(File.ReadAllBytes(path)), "authorization pins current bytes rather than stale metadata");
        Equal(runtime["longest"]["totalPlaytimeSeconds"].Value, "1000.25", "authorization exposes refreshed total playtime");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, 0, "repeated authorization also completes byte inspection");
        Equal(s.Main.Runtime()["status"].Value, "behind", "repeated authorization cannot restore stale verified status");
        Equal(s.Character.importExport.ExportCalls, 0, "publication authorization does not invoke native export");
        NoNativeLoad(s);
    }

    private static void CapturePublication(Scenario s)
    {
        s.SetCurrent("valid.ok.txt");
        s.NativeFile("earlier.txt", "bridge/earlier.ok.txt");
        s.ScanAll();
        var backup = s.Main.Capture();
        s.Main.RequirePublication();
        var runtime = s.Main.Runtime();
        Equal(runtime["pendingFiles"].AsInt, 0, "fresh protective backup synchronously verified for publication");
        Equal(runtime["status"].Value, "verified", "fresh protective backup does not block its own publication");
        Equal(runtime["longest"]["sha256"].Value, backup.Sha256, "publication sees newly captured maximum");
        Equal(s.Character.importExport.ExportCalls, 1, "authorization does not repeat capture");
        NoNativeLoad(s);
    }

    private static void MixedSlashCapturePublication(Scenario s, bool deleted)
    {
        string directory = Path.Combine(s.Root.Replace('\\', '/'), "runtime");
        Assert(directory.Contains("/") && directory.Contains("\\"), "runtime directory actually uses mixed slashes");
        Assert(directory != Path.GetFullPath(directory), "raw runtime directory differs from its canonical spelling");
        s.Main = new InjectorMain(directory, s.Character);
        s.SetCurrent("valid.ok.txt");
        var backup = s.Main.Capture();
        Assert(File.Exists(backup.Path), "captured bytes exist at returned canonical path");
        Equal(backup.Path, Path.GetFullPath(backup.Path), "capture identity uses canonical path");
        Equal(backup.Sha256, Hash(File.ReadAllBytes(backup.Path)), "mixed-slash capture read-back hash");
        Equal(JsonFiles.ParseObject(File.ReadAllText(backup.Path + ".json"))["path"].Value, backup.Path, "manifest uses canonical capture path");
        string evidencePath = Path.Combine(s.Safety, "high-water.json");
        string evidence = File.ReadAllText(evidencePath);
        Equal(JsonFiles.ParseObject(evidence)["path"].Value, backup.Path, "high-water uses canonical capture path");
        if (deleted) File.Delete(backup.Path);

        var runtime = s.ScanAll();
        Equal(runtime["knownFiles"].AsInt, 1, "capture and discovery share one catalog key");
        Equal(runtime["longest"]["path"].Value, backup.Path, "forced scan retains captured path");
        Equal(runtime["longest"]["sha256"].Value, backup.Sha256, "forced scan retains captured high-water hash");
        Equal(File.ReadAllText(evidencePath), evidence, "rescan cannot rewrite captured high-water evidence");
        if (deleted)
        {
            Assert(!runtime["scanComplete"].AsBool, "actual missing captured bytes still block completion");
            Assert(runtime["errors"].Children.Any(error => error["path"].Value == backup.Path
                && error["message"].Value.Contains("disappeared", StringComparison.Ordinal)), "missing capture reported at its canonical path");
            Equal(runtime["status"].Value, "unknown", "missing captured bytes never authorize publication");
            Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        }
        else
        {
            Assert(runtime["scanComplete"].AsBool, "existing mixed-slash capture completes forced inspection");
            Equal(runtime["errors"].Count, 0, "existing capture is not falsely reported missing");
            Equal(runtime["status"].Value, "verified", "matching live state remains verified");
            s.Main.RequirePublication();
            Equal(s.Main.Runtime()["knownFiles"].AsInt, 1, "publication rescan cannot duplicate the capture");
            s.Main.Scan(false);
            Equal(s.Main.Runtime()["pendingFiles"].AsInt, 0, "canonical capture remains cached after inspection");
            Assert(s.Main.Runtime()["scanComplete"].AsBool, "cached scan retains verified capture");
        }
        Equal(s.Character.lastTime, Scenario.OriginalLastTime, "mixed-slash capture restores native time");
        Equal(s.Character.importExport.ExportCalls, 1, "inspection and publication guard do not recapture");
        NoNativeLoad(s);
    }

    private static void IdenticalTie(Scenario s)
    {
        s.NativeFile("z.txt", "valid.ok.txt");
        string a = s.NativeFile("a.txt", "valid.ok.txt");
        var runtime = s.ScanAll();
        Equal(runtime["longest"]["path"].Value, a, "identical copies select deterministic lexical path");
        Assert(!runtime["ambiguousLongest"].AsBool, "same hash copies do not create ambiguity");
        var selected = SaveSafetyCatalog.Read(a, 1260);
        s.Character.saveLoad.Load = data => s.Apply(selected);
        Assert(s.Load()["loaded"].AsBool, "identical copies may load");
        Equal(s.Character.saveLoad.LoadCalls, 1, "identical-copy selection performs one native load");
    }

    private static void HighWaterWriteFails(Scenario s, string source)
    {
        s.SetCurrent("valid.ok.txt");
        string path = s.NativeFile("only.txt", "bridge/earlier.ok.txt");
        Equal(s.ScanAll()["status"].Value, "verified", "higher current may publish before a new maximum is observed");
        string evidencePath = Path.Combine(s.Safety, "high-water.json");
        string previous = File.ReadAllText(evidencePath);
        var higher = SaveSafetyCatalog.Read(Path.Combine(s.Fixtures, "valid.ok.txt"), 1260);
        using (var evidenceLock = new FileStream(evidencePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            if (source == "scan")
            {
                s.Write(path, "valid.ok.txt");
                Assert(!s.ScanAll()["scanComplete"].AsBool, "failed high-water publication blocks scan completion");
                Equal(s.Main.Runtime()["knownFiles"].AsInt, 1, "higher native identity retained despite failed persistence");
            }
            else
            {
                Throws<IOException>(() => s.Main.Capture());
                path = s.Backups.Single();
                Equal(s.Character.lastTime, Scenario.OriginalLastTime, "failed capture evidence publication restores native time");
            }
            var runtime = s.Main.Runtime();
            Equal(runtime["longest"]["sha256"].Value, higher.Sha256, "failed write cannot forget newly observed maximum hash");
            Equal(runtime["longest"]["totalPlaytimeSeconds"].Value, "1000.25", "failed write cannot forget newly observed maximum clock");
            Equal(runtime["status"].Value, "unknown", "dirty high-water evidence cannot leave runtime green");
            s.Write(path, "bridge/earlier.ok.txt");
            Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
            runtime = s.Main.Runtime();
            Equal(runtime["longest"]["sha256"].Value, higher.Sha256, "lowered candidate cannot replace failed-to-persist observation");
            Assert(!runtime["scanComplete"].AsBool, "persistence retry and candidate regression remain blocking");
            Equal(runtime["status"].Value, "unknown", "repeated failed persistence remains non-green");
            Throws<InvalidOperationException>(() => s.Load(higher.Sha256), "complete save scan");
            Assert(!s.ScanAll()["scanComplete"].AsBool, "forced retry while evidence remains locked cannot complete");
        }
        Equal(File.ReadAllText(evidencePath), previous, "failed atomic persistence leaves prior durable record intact");
        Assert(!s.ScanAll()["scanComplete"].AsBool, "successful persistence retry still detects lowered source");
        var durable = JsonFiles.ParseObject(File.ReadAllText(evidencePath));
        Equal(durable["totalPlaytimeSeconds"].Value, "1000.25", "completion retries persistence of retained maximum");
        Equal(durable["sha256"].Value, higher.Sha256, "retry persists original higher hash, not lowered source");
        Equal(durable["path"].Value, path, "retry retains path of higher observation");
        s.Restart();
        Equal(s.Main.Runtime()["longest"]["sha256"].Value, higher.Sha256, "retried evidence survives restart before scanning");
        Assert(!s.ScanAll()["scanComplete"].AsBool, "restart cannot erase source regression");
        Equal(s.Main.Runtime()["status"].Value, "unknown", "lowered source remains non-green after persistence retry and restart");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        Equal(s.Character.importExport.ExportCalls, source == "capture" ? 1 : 0, "failed persistence never causes additional native export");
        Assert(!File.Exists(s.Journal), "failed high-water persistence never starts native load journal");
        NoNativeLoad(s);
    }

    private static void AmbiguousTie(Scenario s)
    {
        s.NativeFile("a.txt", "valid.ok.txt");
        s.NativeFile("z.txt", "bridge/tied-branch.ok.txt");
        var runtime = s.ScanAll();
        Assert(runtime["ambiguousLongest"].AsBool, "different bytes at maximum marked ambiguous");
        Throws<InvalidOperationException>(() => s.Load(), "tie");
        Equal(s.Backups.Length, 1, "ambiguous branch still protects live original");
        Assert(!File.Exists(s.Journal), "branch review does not begin native recovery journal");
        NoNativeLoad(s);
    }

    private static void ForeignPlayer(Scenario s)
    {
        s.NativeFile("other.txt", "bridge/other-player.ok.txt");
        Equal(s.ScanAll()["longest"]["playerName"].Value, "Other Synthetic Player", "foreign name decoded, not inferred from path");
        Throws<InvalidOperationException>(() => s.Load(), "different player name");
        Equal(s.Backups.Length, 1, "cross-player rejection retains protective backup");
        NoNativeLoad(s);
    }

    private static void HistoricalTie(Scenario s, string kind)
    {
        s.SetCurrent("valid.ok.txt");
        string surviving = s.NativeFile("a-survives.txt", "valid.ok.txt");
        string branch = s.NativeFile("z-branch.txt", "bridge/tied-branch.ok.txt");
        string survivingHash = Hash(File.ReadAllBytes(surviving));
        string branchHash = Hash(File.ReadAllBytes(branch));
        Assert(s.ScanAll()["ambiguousLongest"].AsBool, "distinct maximum branches initially decoded");
        string evidencePath = Path.Combine(s.Safety, "high-water.json");
        var original = JsonFiles.ParseObject(File.ReadAllText(evidencePath));
        Equal(original["competingHashes"].Count, 2, "two competing byte identities persisted");
        var expectedHashes = new HashSet<string>(new[] { survivingHash, branchHash }, StringComparer.Ordinal);
        Assert(expectedHashes.SetEquals(original["competingHashes"].Children.Select(hash => hash.Value)), "durable branch identities match real byte hashes");
        if (kind != "deleted")
        {
            s.Write(branch, "bridge/earlier.ok.txt");
            var lowered = s.ScanAll();
            Assert(lowered["scanComplete"].AsBool, "lowering nonprimary branch does not rely on read failure");
            Assert(lowered["ambiguousLongest"].AsBool, "lower replacement cannot erase historical branch ambiguity");
            Equal(lowered["status"].Value, "unknown", "valid lower replacement cannot green maximum");
        }
        if (kind != "lower")
        {
            File.Delete(branch);
            Assert(s.ScanAll()["ambiguousLongest"].AsBool, "branch deletion cannot erase historical ambiguity");
        }
        s.Restart();
        Assert(s.Main.Runtime()["ambiguousLongest"].AsBool, "branch ambiguity restored before any restart scan");
        var runtime = s.ScanAll();
        Assert(runtime["scanComplete"].AsBool, "restart scan is complete without relying on missing in-memory file errors");
        Equal(runtime["knownFiles"].AsInt, kind == "lower" ? 2 : 1, "removed maximum branch is not reintroduced into current catalog");
        Assert(runtime["ambiguousLongest"].AsBool, "persisted competing hashes independently retain ambiguity");
        Equal(runtime["status"].Value, "unknown", "surviving maximum alone cannot green historical tie");
        var persisted = JsonFiles.ParseObject(File.ReadAllText(evidencePath));
        Assert(expectedHashes.SetEquals(persisted["competingHashes"].Children.Select(hash => hash.Value)), "restart preserves both historical hashes");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        Throws<InvalidOperationException>(() => s.Load(), "tie");
        Equal(s.Backups.Length, 1, "historically ambiguous load retains protective current backup");
        Assert(!File.Exists(s.Journal), "historical ambiguity never begins native loading");
        NoNativeLoad(s);
    }

    private static void AlreadyLatest(Scenario s)
    {
        s.SetCurrent("valid.ok.txt");
        s.NativeFile("a.txt", "valid.ok.txt");
        s.NativeFile("z.txt", "bridge/tied-branch.ok.txt");
        var runtime = s.ScanAll();
        Assert(runtime["ambiguousLongest"].AsBool, "distinct maximum branches remain ambiguous even when current is latest");
        Equal(runtime["status"].Value, "unknown", "matching total cannot green ambiguous maximum");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        Throws<InvalidOperationException>(() => s.Load(), "tie");
        Equal(s.Backups.Length, 1, "already-latest command still protects live state");
        Assert(!File.Exists(s.Journal), "ambiguous current creates no load journal");
        NoNativeLoad(s);
    }

    private static void AlreadyLatestUnambiguous(Scenario s)
    {
        s.SetCurrent("valid.ok.txt");
        s.NativeFile("latest.txt", "valid.ok.txt");
        Equal(s.ScanAll()["status"].Value, "verified", "unambiguous current already at known maximum");
        s.Main.RequirePublication();
        var report = s.Load();
        Assert(report["loaded"].IsBoolean && !report["loaded"].AsBool, "unambiguous latest explicitly reports no load");
        Equal(s.Backups.Length, 1, "unambiguous latest retains protective original");
        Assert(s.LastMessage.Contains("no unnecessary save reload", StringComparison.Ordinal), "no-op result explains no reload");
        Assert(!File.Exists(s.Journal), "already-latest no-op creates no load journal");
        NoNativeLoad(s);
    }

    private static void InvalidRequest(Scenario s, string kind)
    {
        s.PrepareLatest();
        var args = s.LoadArgs();
        if (kind == "hash") args["sha256"] = new string('0', 64);
        if (kind == "session") args["expectedStartedAt"] = "stale-session";
        if (kind == "extra") args["unexpected"] = true;
        if (kind == "number-hash") args["sha256"] = 1;
        if (kind == "missing") args.Remove("sha256");
        if (kind == "hash") Throws<InvalidOperationException>(() => s.CommandLoad(args), "selection changed");
        else Throws<FormatException>(() => s.CommandLoad(args), "requires sha256");
        Equal(s.Character.importExport.ExportCalls, 0, "invalid request rejected before export");
        Equal(s.Backups.Length, 0, "invalid request creates no backup");
        NoNativeLoad(s);
    }

    private static void SelectedChanged(Scenario s, string kind)
    {
        string path = s.PrepareLatest();
        string hash = s.Main.Runtime()["longest"]["sha256"].Value;
        if (kind == "changed-hash") File.AppendAllText(path, "\n");
        if (kind == "corrupt") s.Write(path, "wrong-checksum.bad.txt");
        if (kind == "deleted") File.Delete(path);
        Throws<InvalidOperationException>(() => s.Load(hash), kind == "changed-hash" ? "selection changed" : "changed after inspection");
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, 0, "preflight completes candidate inspection before rejection");
        if (kind == "changed-hash")
        {
            Assert(s.Main.Runtime()["scanComplete"].AsBool, "valid changed source is decoded before stale selection is rejected");
            Equal(s.Main.Runtime()["longest"]["sha256"].Value, Hash(File.ReadAllBytes(path)), "stale selection rejected against refreshed byte hash");
        }
        else Assert(!s.Main.Runtime()["scanComplete"].AsBool, "corrupt or missing source rejects incomplete scan");
        Equal(s.Character.importExport.ExportCalls, 0, "preflight rejects selected-file changes before native export");
        Equal(s.Backups.Length, 0, "preflight rejection creates no backup");
        Assert(!File.Exists(s.Journal), "preflight rejection never begins native load");
        NoNativeLoad(s);
    }

    private static void NonselectedChanged(Scenario s, string kind)
    {
        string selected = s.NativeFile("a-selected.txt", "valid.ok.txt");
        string other = s.NativeFile("z-other.txt", "bridge/earlier.ok.txt");
        s.ScanAll();
        Equal(s.Main.Runtime()["longest"]["path"].Value, selected, "selected fixture initially outranks other candidate");
        string hash = Hash(File.ReadAllBytes(selected));
        byte[] replacement = File.ReadAllBytes(kind == "higher" ? Path.Combine(s.Fixtures, "large-finite-clocks.ok.txt") : other);
        if (kind == "corrupt") replacement[0] = (byte)'!';
        s.ReplaceKeepingStamp(other, replacement);
        s.Main.Scan(false);
        Equal(s.Main.Runtime()["pendingFiles"].AsInt, 0, "background cache cannot see nonselected same-stamp replacement");
        Assert(s.Main.Runtime()["scanComplete"].AsBool, "background scan still has only old decoded metadata");
        Throws<InvalidOperationException>(() => s.Load(hash), kind == "higher" ? "selection changed" : "changed after inspection");
        var runtime = s.Main.Runtime();
        Equal(runtime["pendingFiles"].AsInt, 0, "load authorizes only after synchronously inspecting all candidates");
        if (kind == "higher")
        {
            Assert(runtime["scanComplete"].AsBool, "valid nonselected replacement decoded successfully");
            Equal(runtime["longest"]["path"].Value, other, "previously nonselected candidate now outranks stale requested save");
            Equal(runtime["longest"]["sha256"].Value, Hash(replacement), "new maximum hash matches actual replacement bytes");
            Equal(runtime["longest"]["totalPlaytimeSeconds"].Value, double.MaxValue.ToString("R", CultureInfo.InvariantCulture), "nonselected high-water metadata refreshed");
        }
        else
        {
            Assert(!runtime["scanComplete"].AsBool, "nonselected corrupt source also blocks loading selected valid save");
            Assert(runtime["errors"].Children.Any(error => error["path"].Value == other), "scan failure identifies nonselected corrupted file");
        }
        Equal(s.Character.importExport.ExportCalls, 0, "nonselected preflight rejection occurs before protective export");
        Equal(s.Backups.Length, 0, "nonselected preflight rejection creates no backup");
        Assert(!File.Exists(s.Journal), "nonselected change never starts native loading");
        NoNativeLoad(s);
    }

    private static void SelectedChangedAfterPreflight(Scenario s, string kind)
    {
        string path = s.PrepareLatest();
        string hash = s.Main.Runtime()["longest"]["sha256"].Value;
        s.Character.importExport.Export = () =>
        {
            Equal(s.Main.Runtime()["pendingFiles"].AsInt, 0, "race occurs after preflight discovery completed");
            Equal(s.Character.lastTime, Epoch.Now, "race occurs inside synchronous protective capture");
            if (kind == "changed-hash") File.AppendAllText(path, "\n");
            if (kind == "corrupt") s.Write(path, "wrong-checksum.bad.txt");
            if (kind == "deleted") File.Delete(path);
            if (kind == "oversized")
                using (var output = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                    output.SetLength(SaveSafetyCatalog.MaxSaveBytes + 1L);
            return s.ExportText;
        };
        if (kind == "changed-hash") Throws<IOException>(() => s.Load(hash), "changed since inspection");
        if (kind == "corrupt") Throws<InvalidDataException>(() => s.Load(hash));
        if (kind == "deleted") Throws<FileNotFoundException>(() => s.Load(hash));
        if (kind == "oversized") Throws<InvalidDataException>(() => s.Load(hash), "size is outside the safety limit");
        Equal(s.Character.importExport.ExportCalls, 1, "post-preflight rejection performs one protective capture");
        Equal(s.Character.lastTime, Scenario.OriginalLastTime, "post-preflight rejection restores native time");
        Equal(s.Backups.Length, 1, "selected-file race retains protective original");
        SameIdentity(SaveSafetyCatalog.Read(s.Backups[0], 1260), s.CurrentFixture, "race backup identity");
        Assert(!File.Exists(s.Journal), "selected-file race never begins native load");
        NoNativeLoad(s);
    }

    private static void JournalWriteFails(Scenario s, string phase)
    {
        s.PrepareLatest();
        FileStream journalLock = null;
        if (phase == "preparing")
        {
            JsonFiles.WriteAtomic(s.Journal, "{\"phase\":\"loaded\"}");
            journalLock = new FileStream(s.Journal, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        else s.Main.OnLifecycle = value =>
        {
            if (value == "lifecycle:LoadingSave")
                journalLock = new FileStream(s.Journal, FileMode.Open, FileAccess.Read, FileShare.None);
        };
        try { Throws<IOException>(() => s.Load()); }
        finally { if (journalLock != null) journalLock.Dispose(); }
        Equal(s.Character.importExport.DecodeCalls, 1, "journal write follows read-only native decode");
        Equal(s.Character.saveLoad.LoadCalls, 0, "failed durable journal write prevents native mutation");
        Equal(s.Main.RuntimeResets, 0, "failed journal write never resets runtime");
        Assert(!s.Main.LoadingSave, "failed journal write clears temporary timing flag");
        Equal(s.Backups.Length, 1, "journal IO failure retains protective backup");
        SameIdentity(SaveSafetyCatalog.Read(s.Backups[0], 1260), s.CurrentFixture, "journal IO failure backup");
        if (phase == "preparing")
        {
            Equal(s.Main.Lifecycle, InjectorMain.ExecutionLifecycle.Running, "preparation write failure never quiesces unchanged game");
            Equal(s.Main.LifecycleEvents.Count, 0, "preparation write failure precedes lifecycle transition");
            Equal(s.ReadJournal()["phase"].Value, "loaded", "failed preparation does not overwrite prior durable journal");
        }
        else
        {
            Equal(s.Main.Lifecycle, InjectorMain.ExecutionLifecycle.LoadingSave, "loading write failure leaves execution quiesced");
            Equal(s.Main.Runtime()["status"].Value, "recoveryRequired", "loading journal failure cannot green");
            var journal = s.ReadJournal();
            Equal(journal["phase"].Value, "preparing", "unwritable loading and recovery updates retain earlier durable preparation");
            VerifyJournalBackup(s, journal);
            s.Restart();
            Equal(s.Main.Lifecycle, InjectorMain.ExecutionLifecycle.LoadingSave, "durable preparation interlocks immediately on restart");
            Equal(s.Main.Runtime()["status"].Value, "recoveryRequired", "journal IO failure survives restart");
            Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        }
    }

    private static void DecoderRejects(Scenario s, string kind)
    {
        s.PrepareLatest();
        s.Character.importExport.Decode = text =>
        {
            if (kind == "throw") throw new FormatException("synthetic native decode failure");
            return null;
        };
        if (kind == "throw") Throws<FormatException>(() => s.Load(), "synthetic native decode failure");
        else Throws<InvalidOperationException>(() => s.Load(), "Native decoder rejected");
        Equal(s.Character.importExport.DecodeCalls, 1, "native decoder invoked exactly once");
        Equal(s.Character.saveLoad.LoadCalls, 0, "rejected native data not loaded");
        Equal(s.Backups.Length, 1, "decoder rejection retains backup");
        Equal(s.Main.LifecycleEvents.Count, 0, "decoder rejection does not disturb automation");
        Assert(!s.Main.LoadingSave && !File.Exists(s.Journal), "decoder rejection does not start loading");
    }

    private static void SuccessfulLoad(Scenario s)
    {
        string path = s.PrepareLatest();
        var selected = SaveSafetyCatalog.Read(path, 1260);
        s.Main.OnLifecycle = value =>
        {
            if (value == "quiesce")
            {
                var journal = s.ReadJournal();
                Equal(journal["phase"].Value, "preparing", "durable preparation before quiescence");
                VerifyJournalBackup(s, journal);
            }
            if (value == "reset")
            {
                Equal(s.Character.stats.rebirthNumber, selected.Rebirth, "reset only after selected rebirth established");
                Equal(s.Character.totalPlaytime.totalseconds, selected.TotalPlaytimeSeconds + 2, "offline advancement validated before reset");
                Assert(s.Main.LoadingSave, "timing protection covers runtime reset");
            }
            if (value == "lifecycle:Running") Equal(s.ReadJournal()["phase"].Value, "loaded", "durable success before automation resumes");
        };
        s.Character.saveLoad.Load = data =>
        {
            Equal(s.Main.Lifecycle, InjectorMain.ExecutionLifecycle.LoadingSave, "native load only after quiescing and loading lifecycle");
            Assert(s.Main.LoadingSave, "native load under protected timing flag");
            Equal(s.Main.RuntimeResets, 0, "no premature scheduler reset");
            Equal(s.ReadJournal()["phase"].Value, "loading", "durable loading phase before native mutation");
            Equal(s.ReadJournal()["selected"]["sha256"].Value, selected.Sha256, "journal pins inspected byte identity");
            Equal(data.Encoded, File.ReadAllText(path), "native load receives exact pinned bytes");
            s.Main.LifecycleEvents.Add("native-load");
            s.Apply(selected);
            s.Character.totalPlaytime.totalseconds += 2;
            s.Character.rebirthTime.totalseconds += 2;
        };
        var report = s.Load();
        Assert(report["loaded"].AsBool, "successful report");
        Equal(report["selected"]["sha256"].Value, selected.Sha256, "report selected hash");
        Equal(report["observed"]["totalPlaytimeSeconds"].Value, "1002.25", "report observed live total");
        Equal(report["observed"]["rebirthSeconds"].Value, "62.5", "report observed live elapsed");
        Equal(s.Main.RuntimeResets, 1, "one runtime reset after validation");
        Equal(s.Character.importExport.DecodeCalls, 1, "one native decode");
        Equal(s.Character.saveLoad.LoadCalls, 1, "one native load");
        Equal(string.Join(",", s.Main.LifecycleEvents), "quiesce,lifecycle:LoadingSave,native-load,reset,lifecycle:Running", "load lifecycle boundary order");
        Equal(s.Main.Lifecycle, InjectorMain.ExecutionLifecycle.Running, "successful load resumes automation");
        Assert(!s.Main.LoadingSave, "successful load clears protected timing flag");
        var completed = s.ReadJournal();
        Equal(completed["phase"].Value, "loaded", "durable completed journal");
        Equal(completed["observed"]["rebirth"].Value, "7", "completed observed rebirth");
        VerifyJournalBackup(s, completed);
        s.Restart();
        Equal(s.ScanAll()["status"].Value, "verified", "success remains verified across injector restart");
        s.Main.RequirePublication();
    }

    private static void FailedLoad(Scenario s, string kind)
    {
        string path = s.PrepareLatest();
        var selected = SaveSafetyCatalog.Read(path, 1260);
        var original = s.CurrentFixture;
        s.Character.saveLoad.Load = data =>
        {
            Assert(s.Main.LoadingSave, "failing native boundary still protected");
            Equal(s.Main.Lifecycle, InjectorMain.ExecutionLifecycle.LoadingSave, "failing native boundary entered only while quiesced");
            Equal(s.ReadJournal()["phase"].Value, "loading", "failure has durable pre-mutation journal");
            if (kind == "throw") throw new IOException("synthetic native load failure");
            if (kind == "noop") return;
            if (kind == "partial") { s.Character.totalPlaytime.totalseconds = selected.TotalPlaytimeSeconds; return; }
            s.Apply(selected);
            if (kind == "wrong-rebirth") s.Character.stats.rebirthNumber++;
            if (kind == "short-elapsed") s.Character.rebirthTime.totalseconds -= 1;
            if (kind == "wrong-start") s.Character.totalPlaytime.totalseconds += 6;
            if (kind == "invalid-clock") s.Character.totalPlaytime.totalseconds = double.NaN;
            if (kind == "wrong-player") s.Character.playerName = "Wrong Native Player";
        };
        if (kind == "throw") Throws<IOException>(() => s.Load(), "synthetic native load failure");
        else Throws<InvalidOperationException>(() => s.Load());
        Equal(s.Character.saveLoad.LoadCalls, 1, "failed native load not automatically retried");
        Equal(s.Main.Lifecycle, InjectorMain.ExecutionLifecycle.LoadingSave, "failed load leaves automation quiesced");
        Equal(s.Main.RuntimeResets, 0, "unverified state never resets runtime or resumes");
        Assert(!s.Main.LoadingSave, "failure clears temporary timing flag");
        Equal(s.Main.Runtime()["status"].Value, "recoveryRequired", "unverified native result never green");
        var journal = s.ReadJournal();
        Equal(journal["phase"].Value, "recoveryRequired", "durable failure journal");
        Equal(journal["selected"]["sha256"].Value, selected.Sha256, "failure pins selected identity");
        Assert(journal["error"].Value.Contains("Protected original:", StringComparison.Ordinal), "failure journal identifies retained original");
        VerifyJournalBackup(s, journal);
        SameIdentity(SaveSafetyCatalog.Read(journal["backup"]["path"].Value, 1260), original, "durable pre-load original");
        int exports = s.Character.importExport.ExportCalls;
        Throws<InvalidOperationException>(() => s.Load(), "complete save scan");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        Equal(s.Character.importExport.ExportCalls, exports, "recovery block prevents another export or overwrite");
        s.Restart();
        Equal(s.Main.Lifecycle, InjectorMain.ExecutionLifecycle.LoadingSave, "failed native load interlocks before restart schedules or scans");
        s.ScanAll();
        Equal(s.Main.Runtime()["status"].Value, "recoveryRequired", "durable failure survives injector restart");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
    }

    private static void InterruptedJournal(Scenario s, string phase)
    {
        s.SetCurrent("valid.ok.txt");
        s.NativeFile("latest.txt", "valid.ok.txt");
        string backup = s.Write(Path.Combine(s.Safety, "backups", "original.txt"), "bridge/earlier.ok.txt");
        var journal = new JSONObject();
        journal["phase"] = phase;
        journal["backup"]["path"] = backup;
        JsonFiles.WriteAtomic(s.Journal, journal.ToString());
        s.Restart();
        Equal(s.Main.Lifecycle, InjectorMain.ExecutionLifecycle.LoadingSave, "interrupted journal sets startup lifecycle before any pump or scan");
        Equal(s.Main.Runtime()["status"].Value, "recoveryRequired", "startup recovery immediately visible");
        var runtime = s.ScanAll();
        Assert(runtime["scanComplete"].AsBool, "interrupted journal does not rely on a scan failure");
        Equal(runtime["status"].Value, "recoveryRequired", "interrupted phase dominates otherwise-current playtime");
        Assert(runtime["message"].Value.Contains(backup, StringComparison.Ordinal), "recovery message retains backup path");
        for (int i = 0; i < 3; i++) s.Main.Tick();
        s.ScanAll();
        Equal(s.Main.Runtime()["status"].Value, "recoveryRequired", "rescans cannot erase interrupted recovery issue");
        Throws<InvalidOperationException>(() => s.Load(), "complete save scan");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        Equal(s.Character.importExport.ExportCalls, 0, "interrupted journal blocks destructive command before export");
        NoNativeLoad(s, true);
    }

    private static void MalformedJournal(Scenario s)
    {
        s.SetCurrent("valid.ok.txt");
        s.NativeFile("latest.txt", "valid.ok.txt");
        Directory.CreateDirectory(s.Safety);
        File.WriteAllText(s.Journal, "{\"phase\":\"loading\",\"backup\":");
        s.Restart();
        Equal(s.Main.Lifecycle, InjectorMain.ExecutionLifecycle.LoadingSave, "bad journal sets startup lifecycle before any work");
        Equal(s.Main.Runtime()["status"].Value, "recoveryRequired", "bad journal immediately reports recovery");
        s.Main.Tick();
        Assert(s.Main.Runtime()["status"].Value != "verified", "malformed journal initially blocks green");
        s.ScanAll();
        for (int i = 0; i < 3; i++) s.Main.Tick();
        var runtime = s.Main.Runtime();
        Equal(runtime["status"].Value, "recoveryRequired", "malformed journal remains recovery-required after rescan");
        Throws<InvalidOperationException>(() => s.Main.RequirePublication(), "blocked");
        Throws<InvalidOperationException>(() => s.Load());
        NoNativeLoad(s, true);
    }

    private static void CompletedJournal(Scenario s)
    {
        s.SetCurrent("valid.ok.txt");
        s.NativeFile("latest.txt", "valid.ok.txt");
        JsonFiles.WriteAtomic(s.Journal, "{\"phase\":\"loaded\"}");
        s.Restart();
        Equal(s.ScanAll()["status"].Value, "verified", "completed journal is not interrupted recovery");
        s.Main.RequirePublication();
        Equal(s.Character.importExport.ExportCalls, 0, "status and publication guard do not export");
        NoNativeLoad(s);
    }

    private static void VerifyJournalBackup(Scenario s, JSONNode journal)
    {
        string path = journal["backup"]["path"].Value;
        Assert(File.Exists(path), "journal original backup exists on real filesystem");
        Equal(Hash(File.ReadAllBytes(path)), journal["backup"]["sha256"].Value, "journal original hash matches durable bytes");
        Equal(SaveSafetyCatalog.Read(path, 1260).Sha256, journal["backup"]["sha256"].Value, "journal original remains independently decodable");
        Assert(File.Exists(path + ".json"), "journal original retains capture manifest");
    }

    private static void SameIdentity(SaveIdentity actual, SaveIdentity expected, string context)
    {
        Equal(actual.TotalPlaytimeSeconds, expected.TotalPlaytimeSeconds, context + " total");
        Equal(actual.RebirthSeconds, expected.RebirthSeconds, context + " elapsed");
        Equal(actual.Rebirth, expected.Rebirth, context + " rebirth");
        Equal(actual.PlayerName, expected.PlayerName, context + " name");
        Equal(actual.Version, expected.Version, context + " version");
    }

    private static void NoNativeLoad(Scenario s, bool recovering = false)
    {
        Equal(s.Character.importExport.DecodeCalls, 0, "no native deserialization");
        Equal(s.Character.saveLoad.LoadCalls, 0, "no native load");
        Equal(s.Main.RuntimeResets, 0, "no scheduler/runtime reset");
        Equal(s.Main.LifecycleEvents.Count, recovering ? 1 : 0, "only expected startup lifecycle transition");
        Equal(s.Main.Lifecycle, recovering ? InjectorMain.ExecutionLifecycle.LoadingSave : InjectorMain.ExecutionLifecycle.Running, "automation remains in expected lifecycle");
        if (recovering) Equal(s.Main.LifecycleEvents[0], "lifecycle:LoadingSave", "startup interlock never runs a native load or scheduler reset");
        Assert(!s.Main.LoadingSave, "no loading timing flag");
    }

    private static void Run(string fixtures, string name, Action<Scenario> test)
    {
        cases++;
        using (var scenario = new Scenario(fixtures))
        {
            try
            {
                test(scenario);
                Equal(scenario.Character.saveLoad.CloudCalls, 0, "no cloud callback: " + name);
                Console.WriteLine("PASS: " + name);
            }
            catch (Exception error)
            {
                failures++;
                scenario.Preserve = true;
                Console.Error.WriteLine("FAIL: " + name + "\n" + error + "\nRuntime: " + scenario.Main.Runtime().ToString() + "\nArtifacts: " + scenario.Root);
            }
        }
    }

    private static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static void Throws<T>(Action action, string contains = null) where T : Exception
    {
        assertions++;
        try { action(); }
        catch (Exception error)
        {
            if (!(error is T)) throw new CheckFailure("Expected " + typeof(T).Name + "; actual=" + error.GetType().Name, error);
            if (contains != null) Assert(error.Message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0, "expected error containing '" + contains + "'; actual=" + error.Message);
            return;
        }
        throw new CheckFailure("Expected " + typeof(T).Name + " but the operation succeeded.");
    }

    private static void Equal<T>(T actual, T expected, string name)
    {
        Assert(EqualityComparer<T>.Default.Equals(actual, expected), name + "; expected=" + expected + "; actual=" + actual);
    }

    private static void Assert(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new CheckFailure(name);
    }

    // Boundary assertions must not be mistaken for expected native load failures.
    private sealed class CheckFailure : Exception
    {
        internal CheckFailure(string message, Exception inner = null) : base(message, inner) { }
    }

    private sealed class Scenario : IDisposable
    {
        internal const int OriginalLastTime = 123456789;
        internal readonly string Fixtures, Root, RuntimeDirectory, NativeDirectory, Safety, Journal;
        internal readonly Character Character = new Character();
        internal InjectorMain Main;
        internal SaveIdentity CurrentFixture;
        internal string ExportText, LastMessage;
        internal JSONNode LastData;
        internal int ExportLastTime;
        internal bool Preserve;

        internal Scenario(string fixtures)
        {
            Fixtures = fixtures;
            Root = Path.Combine(Path.GetTempPath(), "opencode", "save-safety-bridge-" + Guid.NewGuid().ToString("N"));
            RuntimeDirectory = Path.Combine(Root, "runtime");
            NativeDirectory = Path.Combine(Root, "native");
            Safety = Path.Combine(RuntimeDirectory, "save-safety");
            Journal = Path.Combine(Safety, "recovery.json");
            Directory.CreateDirectory(RuntimeDirectory);
            Directory.CreateDirectory(NativeDirectory);
            Character.importExport.Export = () => { ExportLastTime = Character.lastTime; return ExportText; };
            SetCurrent("bridge/earlier.ok.txt");
            Restart();
        }

        internal string[] Backups
        {
            get
            {
                string directory = Path.Combine(Safety, "backups");
                return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.txt") : new string[0];
            }
        }

        internal void SetCurrent(string fixture)
        {
            string path = Path.Combine(Fixtures, fixture);
            CurrentFixture = SaveSafetyCatalog.Read(path, 1260);
            Apply(CurrentFixture);
            Character.lastTime = OriginalLastTime;
            ExportText = File.ReadAllText(path);
        }

        internal void Apply(SaveIdentity identity)
        {
            Character.totalPlaytime.totalseconds = identity.TotalPlaytimeSeconds;
            Character.rebirthTime.totalseconds = identity.RebirthSeconds;
            Character.stats.rebirthNumber = identity.Rebirth;
            Character.playerName = identity.PlayerName;
            Character.lastTime = identity.LastTime;
        }

        internal void Restart()
        {
            UnityEngine.Application.persistentDataPath = NativeDirectory;
            Main = new InjectorMain(RuntimeDirectory, Character);
        }

        internal string Write(string path, string fixture)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.Copy(Path.Combine(Fixtures, fixture), path, true);
            return path;
        }

        internal string NativeFile(string name, string fixture) { return Write(Path.Combine(NativeDirectory, name), fixture); }

        internal void ReplaceKeepingStamp(string path, byte[] bytes)
        {
            long length = new FileInfo(path).Length;
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            Equal((long)bytes.Length, length, "synthetic replacement has identical encoded length");
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, stamp);
            Equal(new FileInfo(path).Length, length, "replacement preserves exact file length");
            Equal(File.GetLastWriteTimeUtc(path).Ticks, stamp.Ticks, "replacement preserves exact modification timestamp");
        }

        internal JSONObject ScanAll()
        {
            Main.Scan(true);
            int budget = SaveSafetyCatalog.MaxCandidates + 2;
            do { Main.Tick(); }
            while (Main.Runtime()["pendingFiles"].AsInt > 0 && --budget > 0);
            Equal(Main.Runtime()["pendingFiles"].AsInt, 0, "scan drains within candidate bound");
            return Main.Runtime();
        }

        internal string PrepareLatest()
        {
            string path = NativeFile("latest.txt", "valid.ok.txt");
            Assert(ScanAll()["scanComplete"].AsBool, "latest fixture scan complete");
            return path;
        }

        internal JSONObject LoadArgs(string hash = null)
        {
            var args = new JSONObject();
            args["sha256"] = hash ?? Main.Runtime()["longest"]["sha256"].Value;
            args["expectedStartedAt"] = Main.StartedAt;
            return args;
        }

        internal JSONNode Load(string hash = null) { return CommandLoad(LoadArgs(hash)); }

        internal JSONNode CommandLoad(JSONNode args)
        {
            LastMessage = Main.Command("loadLongestSave", args, out LastData);
            return LastData;
        }

        internal JSONObject ReadJournal() { return JsonFiles.ParseObject(File.ReadAllText(Journal)); }

        public void Dispose()
        {
            InjectorMain.Character = null;
            UnityEngine.Application.persistentDataPath = null;
            if (!Preserve) Directory.Delete(Root, true);
        }
    }
}
