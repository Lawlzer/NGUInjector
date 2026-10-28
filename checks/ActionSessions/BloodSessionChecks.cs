using System;
using System.IO;
using NGUInjector;
using NGUInjector.CustomPlans;
using SimpleJSON;

// Source-linked session tests. Native spell/controller behavior and execution
// authority are supplied boundaries; checkpoint IO and session orchestration are real.
internal static class BloodSessionChecks
{
    private static int assertions;
    private static string root;
    private static Main runtime;
    private static CustomPlan plan;
    private static string Checkpoint => Path.Combine(Main.FixtureDirectory, "iron-pill-window-v1.json");
    private static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!Equals(expected, actual)) throw new Exception(message + ": expected " + expected + ", actual " + actual);
    }
    private static void Setup()
    {
        Main.FixtureDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Main.FixtureDirectory);
        Main.Character = new Character(); Main.Settings = new Settings();
        runtime = Main.reference = new Main(); plan = new CustomPlan();
        Time(5900d);
    }
    private static void Time(double seconds)
    {
        Main.Character.rebirthTime.totalseconds = seconds;
        Main.Character.totalPlaytime.totalseconds = seconds + 100000d;
    }
    private static void Restart() { runtime = Main.reference = new Main(); }
    private static void Run() { runtime.BloodPass(plan); }
    private static void ReadinessDenialIsRetryable()
    {
        Setup(); runtime.GrantReady = false; Run();
        Equal(0, Main.Character.bloodSpells.NumberCalls, "preflight denial enters no NUMBER callback");
        Equal("starting", JsonFiles.ParseObject(File.ReadAllText(Checkpoint))["phase"].Value, "starting checkpoint precedes callback");
        Equal(false, runtime.Saving, "preflight denial claims no saving reservation");
        runtime.GrantReady = true; Run();
        Equal(1, Main.Character.bloodSpells.NumberCalls, "same uninvoked window retries after preflight recovery");
        Equal(true, runtime.Saving, "verified transfer starts saving");
        Equal(10011d, Main.Character.bloodMagic.rebirthPower, "NUMBER receives the exact bank");
    }
    private static void SavingRestoreKeepsOriginalWindow()
    {
        Setup(); Run(); var original = File.ReadAllText(Checkpoint);
        Main.Character.bloodMagic.bloodPoints = 321d; Time(6000d); Restart(); Run();
        Equal(true, runtime.Saving, "restored saving window reserves blood");
        Equal(5900d, runtime.Started, "restoration retains original start");
        Equal(321d, Main.Character.bloodMagic.bloodPoints, "restoration does not flush new blood");
        Equal(1, Main.Character.bloodSpells.NumberCalls, "restoration never re-enters NUMBER");
        Equal(original, File.ReadAllText(Checkpoint), "restoration preserves deadline and configured targets");
        plan.IronPillSaveSeconds = 1; Run();
        Equal(true, runtime.Saving, "live target edit cannot shorten acquired window");
    }
    private static void StartingRestoreCancelsLocally()
    {
        Setup(); runtime.GrantReady = false; Run();
        Restart(); Time(5910d); Run();
        Equal(false, runtime.Saving, "restored starting phase is cancelled");
        Equal(0, Main.Character.bloodSpells.NumberCalls, "ambiguous restoration invokes no spell");
        Equal(6810d, runtime.RetryAt, "ambiguous starting phase receives finite backoff");
        Equal("closed", JsonFiles.ParseObject(File.ReadAllText(Checkpoint))["phase"].Value, "cancellation closes checkpoint");
        Time(6810d); Run();
        Equal(true, runtime.Saving, "bounded backoff permits a new native opportunity");
        Equal(6810d, runtime.Started, "new attempt does not resurrect old elapsed time");
    }
    private static void EntryCheckpointFailureDoesNotInvoke()
    {
        Setup(); runtime.LoadCheckpoint(); File.WriteAllText(Checkpoint, "{}");
        using (var held = new FileStream(Checkpoint, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Run(); Equal(0, Main.Character.bloodSpells.NumberCalls, "starting checkpoint write failure withholds native flush");
            Equal(false, runtime.Saving, "failed starting write does not reserve blood");
        }
        Run(); Equal(1, Main.Character.bloodSpells.NumberCalls, "storage recovery retries uninvoked entry");
    }
    private static void SavingCheckpointFailureDoesNotReflush()
    {
        Setup(); FileStream held = null;
        Main.Character.bloodSpells.AfterNumber = () => held = new FileStream(Checkpoint, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Run(); Equal(true, runtime.Saving, "verified flush remains saving when saving checkpoint write fails");
            Equal("starting", JsonFiles.ParseObject(File.ReadAllText(Checkpoint))["phase"].Value, "failed replacement leaves original starting file");
            Main.Character.bloodMagic.bloodPoints = 321d; Run();
            Equal(1, Main.Character.bloodSpells.NumberCalls, "saving checkpoint retry cannot repeat NUMBER");
            Equal(321d, Main.Character.bloodMagic.bloodPoints, "saving checkpoint retry preserves new blood");
        }
        finally { held?.Dispose(); Main.Character.bloodSpells.AfterNumber = null; }
        Run(); Equal("saving", JsonFiles.ParseObject(File.ReadAllText(Checkpoint))["phase"].Value, "saving checkpoint recovers without native work");
    }
    private static void ClosedWindowCannotRecast()
    {
        Setup(); plan.IronPillMinWorth = 1d; Run(); Main.Character.bloodMagic.bloodPoints = 100d;
        Main.Settings.HistoryFailure = true;
        using (var held = new FileStream(Checkpoint, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Run(); Equal(false, runtime.Saving, "cast retires reservation before closure/history failure");
            Equal(1, Main.Character.bloodSpells.PillCalls, "successful native cast entered once");
            Main.Character.bloodMagic.bloodPoints = 100d;
            Main.Character.bloodMagic.adventureSpellTime.totalseconds = 10000d;
            Equal(false, runtime.Cast(), "closed in-memory window cannot recast even when cooldown is reset externally");
            Run(); Equal(1, Main.Character.bloodSpells.PillCalls, "closure write failure cannot recast through ordinary pass");
            Equal(100d, Main.Character.bloodMagic.bloodPoints, "closed window preserves later blood pool");
            Equal(3f, Main.Character.adventure.attack, "history failure grants no fabricated second pill credit");
        }
    }
    private static void AmbiguousInvocationsCancelOnlyWindow()
    {
        Setup(); Main.Character.bloodSpells.RejectNumber = true; Run();
        Equal(false, runtime.Saving, "unverified NUMBER invocation cancels saving attempt");
        Equal(6800d, runtime.RetryAt, "unverified entry gets finite local backoff");
        Main.Character.bloodSpells.RejectNumber = false; Time(6800d); Run();
        Equal(true, runtime.Saving, "unverified entry does not permanently block future windows");
        Main.Character.bloodMagic.bloodPoints = 100d; Main.Character.bloodSpells.RejectPill = true;
        Time(7700d); Run();
        Equal(false, runtime.Saving, "unverified pill invocation retires only its window");
        Equal(8600d, runtime.RetryAt, "unverified pill gets bounded retry");
        Equal(0, Main.Settings.History.Count, "unverified pill receives no history credit");
    }
    private static void CancellationAndTimeoutAreBounded()
    {
        Setup(); Run(); Time(6860d); Run();
        Equal(false, runtime.Saving, "hard deadline closes window");
        Equal(7760d, runtime.RetryAt, "timeout yields for 900 seconds");
        Main.Settings.CustomPlanFile = "new-plan"; Run();
        Equal(6860d, runtime.RetryAt, "new plan does not inherit previous closed plan's timeout budget");
        Equal(true, runtime.Saving, "new plan can acquire its own window when no active old reservation needs cancellation");
        plan.AutoIronPillEnabled = false; Run(); Equal(false, runtime.Saving, "plan opt-out closes reservation");
        Restart(); Run(); Equal(false, runtime.Saving, "closed checkpoint cannot restore cancelled reservation");
    }
    private static void LegacyAndCorruptFilesAreLocal()
    {
        Setup(); System.IO.Directory.CreateDirectory(Path.Combine(Main.FixtureDirectory, "finite-actions-v1"));
        File.WriteAllText(Path.Combine(Main.FixtureDirectory, "finite-actions-v1", "broken.json"), "{");
        Run(); Equal(false, runtime.Saving, "retired ledger is not parsed or rebound");
        Equal(6800d, runtime.RetryAt, "legacy uncertainty cancels only Iron Pill window for finite delay");
        Time(6800d); Run(); Equal(true, runtime.Saving, "retired file cannot block later Iron Pill opportunities");
        Setup(); File.WriteAllText(Checkpoint, "{"); Run();
        Equal(6800d, runtime.RetryAt, "corrupt bounded checkpoint receives local backoff");
        Equal(0, Main.Character.bloodSpells.NumberCalls, "corrupt window is not interpreted as an uninvoked entry");
    }
    private static void NativeModelReplacementCancelsOldWindow()
    {
        Setup(); Run(); Main.Character = new Character(); Time(5900d); Run();
        Equal(false, runtime.Saving, "foreign model replacement cannot inherit in-session reservation");
        Equal(0, Main.Character.bloodSpells.NumberCalls, "foreign model is not flushed during cancellation");
        Setup(); Run(); Main.Character.bloodMagic = new Blood(); Run();
        Equal(false, runtime.Saving, "foreign blood submodel cannot inherit the in-session window");
    }
    private static void InvalidBackoffCannotQuarantineForever()
    {
        Setup(); Time(5900.123456789d); Run();
        Main.Character.bloodMagic.bloodPoints = 321d; Restart(); Run();
        Equal(true, runtime.Saving, "fractional native timestamps restore original saving checkpoint");
        Equal(321d, Main.Character.bloodMagic.bloodPoints, "fractional timestamp restoration never reflushes");
        var data = JsonFiles.ParseObject(File.ReadAllText(Checkpoint));
        data["phase"] = "closed"; data["retryNotBefore"] = 1e300;
        File.WriteAllText(Checkpoint, data.ToString()); Restart(); Run();
        Equal(6800.123456789d, runtime.RetryAt, "invalid enormous backoff is cancelled locally rather than imported as permanent quarantine");
        Equal(false, runtime.Saving, "malformed closed window does not resurrect saving");
    }
    private static void ThresholdSpellUsesCurrentNativeOpportunity()
    {
        Setup(); Main.Settings.CastBloodSpells = true; Main.Settings.BloodMacGuffinBThreshold = 1;
        Main.Character.settings.rebirthDifficulty = difficulty.evil;
        Main.Character.adventure.itopod.perkLevel[73] = 1;
        Main.Character.inventory.macguffins.Add(new Guffin { id = 1 });
        Main.Character.bloodMagic.macguffin2Time.totalseconds = 4000d;
        runtime.GrantReady = false; runtime.RequestThresholdBloodSpells(false);
        Equal(0, Main.Character.bloodSpells.MacguffinCalls, "threshold preflight denial consumes no native spell opportunity");
        runtime.GrantReady = true; Main.Character.bloodSpells.RejectMacguffin = true;
        runtime.RequestThresholdBloodSpells(false);
        Equal(1, Main.Character.bloodSpells.MacguffinCalls, "threshold spell enters native method once for current cooldown opportunity");
        Time(6000d); Main.Character.bloodMagic.macguffin2Time.totalseconds = 4100d;
        runtime.RequestThresholdBloodSpells(false);
        Equal(1, Main.Character.bloodSpells.MacguffinCalls, "later ordinary pass cannot retry an ambiguous unchanged native cooldown opportunity");
        Main.Character.bloodMagic.macguffin2Time.totalseconds = 3600d;
        Main.Character.bloodSpells.RejectMacguffin = false; runtime.RequestThresholdBloodSpells(false);
        Equal(2, Main.Character.bloodSpells.MacguffinCalls, "new native cooldown origin admits another opportunity without global kind blocking");
        Equal(2, Main.Character.inventory.macguffins[0].level, "only verified native spell grants levels");
    }
    internal static void RunChecks()
    {
        root = Path.Combine(Path.GetTempPath(), "ngu-blood-session-unit-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            foreach (var check in new Action[] { ReadinessDenialIsRetryable, SavingRestoreKeepsOriginalWindow, StartingRestoreCancelsLocally,
                EntryCheckpointFailureDoesNotInvoke, SavingCheckpointFailureDoesNotReflush, ClosedWindowCannotRecast,
                AmbiguousInvocationsCancelOnlyWindow, CancellationAndTimeoutAreBounded, LegacyAndCorruptFilesAreLocal,
                NativeModelReplacementCancelsOldWindow, InvalidBackoffCannotQuarantineForever, ThresholdSpellUsesCurrentNativeOpportunity })
            {
                Console.WriteLine("CHECK " + check.Method.Name);
                check();
            }
            Console.WriteLine("Blood session source-unit checks PASS: " + assertions + " assertions; native/authority boundaries supplied, no live or native build.");
        }
        finally { System.IO.Directory.Delete(root, true); }
    }
}

internal static class BloodSessionEntry
{
    private static void Main() { BloodSessionChecks.RunChecks(); }
}
