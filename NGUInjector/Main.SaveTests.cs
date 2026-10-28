using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NGUInjector.AllocationProfiles.RebirthStuff;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private static JSONObject _saveTestSession;
        private static Character _saveTestCharacter;
        private static SaveIdentity _saveTestOriginal;
        private static string _saveTestDirectory, _saveTestManifestHash;
        private static bool _saveTestBundleReady, _saveTestBundleTrusted, _saveTestNativeMutation, _saveTestOriginalReleased;
        private static bool _saveTestExpectedStartup, _saveTestRestoringStartup, _saveTestInternalRestart;
        private static int _saveTestOriginalCapture;
        private static DateTime _saveTestFenceUtc;

        internal static bool SaveTestActive => _saveTestSession != null
            && _saveTestSession["phase"].Value != "idle" && _saveTestSession["phase"].Value != "restored";
        internal static bool SaveTestCaptureAllowed => !SaveTestActive || _saveTestOriginalCapture != 0;
        internal static bool SaveTestInternalRestart => _saveTestInternalRestart;
        private static string SaveTestJournalPath => Path.Combine(_dir, "save-safety", "test-session.json");

        internal static bool SaveTestCommandAllowed(string command, JSONNode args = null)
        {
            return !SaveTestActive || command == "status" || command == "readPlan" || command == "readPresetLibrary"
                || command == "startSaveTest" || command == "loadSaveTestFixture"
                || command == "setSaveTestStage" || command == "restoreSaveTest";
        }

        // Apply after claiming a request, including when the session has already restored.
        // This fences old unclaimed mutations without inventing another transport or claim store.
        internal static bool SaveTestCommandCreatedAtAllowed(string command, DateTime createdAt)
        {
            return command == "status" || command == "readPlan" || command == "readPresetLibrary"
                || command == "restoreSaveTest" || command == "loadSaveTestFixture" || command == "setSaveTestStage"
                || _saveTestFenceUtc == default(DateTime) || createdAt.ToUniversalTime() > _saveTestFenceUtc;
        }

        internal bool IsSaveTestRecoveryAdapter(string command)
        {
            return SaveTestActive && Loader.IsSaveTestAdapter(this) && (!_started || _unloaded)
                && (command == "status" || command == "restoreSaveTest");
        }

        internal static JSONObject GetSaveTestRuntime()
        {
            if (_saveTestSession != null) return JsonFiles.ParseObject(_saveTestSession.ToString());
            var idle = NewSaveTestSession(""); idle["phase"] = "idle";
            return idle;
        }

        internal void PumpSaveTestRecovery()
        {
            if (!SaveTestActive || !Loader.IsSaveTestAdapter(this) || _started && !_unloaded) return;
            var now = DateTime.UtcNow;
            if ((now - _externalUiLastPump).TotalMilliseconds < ExternalUiPumpMilliseconds) return;
            _externalUiLastPump = now;
            ProcessExternalUiCommands();
            if ((now - _externalUiLastRuntimeWrite).TotalMilliseconds >= ExternalUiRuntimeMilliseconds)
            {
                _externalUiLastRuntimeWrite = now;
                WriteExternalUiRuntimeSnapshot(0);
            }
        }

        // Called before settings, checkpoint loading, migrations, or ordinary startup writes.
        internal bool InitializeSaveTestStartup()
        {
            if (_saveTestExpectedStartup)
            {
                _saveTestExpectedStartup = false;
                _saveTestInternalRestart = true;
                return true;
            }
            _saveTestInternalRestart = false;
            if (_saveTestSession == null && File.Exists(SaveTestJournalPath))
            {
                try
                {
                    var journal = JsonFiles.ParseObject(File.ReadAllText(SaveTestJournalPath));
                    if (journal["version"].AsInt != 1 || !SaveTestHex(journal["sessionId"].Value, 32)
                        || journal["runtimeRoot"].Value != Path.GetFullPath(_dir)
                        || journal["nativeRoot"].Value != Path.GetFullPath(UnityEngine.Application.persistentDataPath))
                        throw new InvalidDataException("Save-test journal identity is invalid.");
                    _saveTestSession = journal["session"].AsObject;
                    if (_saveTestSession == null || _saveTestSession["sessionId"].Value != journal["sessionId"].Value)
                        throw new InvalidDataException("Save-test journal session is invalid.");
                    _saveTestDirectory = Path.Combine(_dir, "save-safety", "tests", journal["sessionId"].Value);
                    _saveTestManifestHash = journal["manifestSha256"].Value;
                    _saveTestBundleReady = journal["bundleReady"].AsBool;
                    _saveTestNativeMutation = journal["nativeMutationStarted"].AsBool;
                    _saveTestOriginalReleased = journal["originalReleased"].AsBool;
                    _saveTestFenceUtc = DateTime.Parse(journal["fenceUtc"].Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
                    if (_saveTestSession["phase"].Value == "restored") return true;
                    if (journal["original"]["sha256"].IsString)
                    {
                        var original = SaveSafetyCatalog.Read(Path.Combine(_saveTestDirectory, "original.txt"), SaveSafetyCatalog.NativeVersion);
                        if (original.Sha256 != journal["original"]["sha256"].Value)
                            throw new InvalidDataException("Protected original save hash changed.");
                        _saveTestOriginal = original;
                    }
                    if (_saveTestBundleReady)
                    {
                        ResumeSaveTestBundle();
                        _saveTestBundleTrusted = true;
                    }
                    _saveTestSession["phase"] = "recoveryRequired";
                    _saveTestSession["error"] = "Interrupted save test. Explicitly restore the protected original; fixtures are never replayed at startup.";
                }
                catch (Exception error)
                {
                    if (_saveTestSession == null) _saveTestSession = NewSaveTestSession("");
                    _saveTestSession["phase"] = "recoveryRequired";
                    _saveTestSession["error"] = "Save-test recovery evidence could not be verified: " + error.Message;
                }
            }
            if (!SaveTestActive) return true;
            _saveTestInternalRestart = true;
            _saveTestCharacter = UnityEngine.Object.FindObjectOfType<Character>();
            SetExecutionLifecycle(ExecutionLifecycle.LoadingSave);
            Loader.HoldSaveTestLease(this, _injectorLease);
            _injectorLease = null;
            return false;
        }

        internal void CompleteSaveTestStartup()
        {
            if (!SaveTestActive) return;
            _saveTestCharacter = Character;
            if (_saveTestRestoringStartup)
            {
                var observed = SaveTestCurrentIdentity(Character);
                if (_saveTestOriginal != null) VerifySaveTestLoadedIdentity(_saveTestOriginal, observed);
                // Startup normalizes settings and may write review checkpoints. Archive those
                // disposable writes too, so "restored" means original bytes, not just a native load.
                if (_saveTestBundleReady) RecordSaveTestRestoration(SaveTestBundle.Restore(_saveTestDirectory, true));
                if (Settings != null && !Settings.LoadSettings())
                    throw new InvalidDataException("Restored settings could not be rebound to the fresh original owner.");
                _saveTestSession["restoration"]["filesVerified"] = true;
                _saveTestSession["restoration"]["nativeVerified"] = true;
                _saveTestSession["restoration"]["verified"] = true;
                _saveTestSession["observed"] = SaveIdentityJson(observed);
                _saveTestFenceUtc = DateTime.UtcNow;
                _saveTestSession["phase"] = "restored";
                _saveTestSession["error"] = "";
                try { PersistSaveTestJournal(); }
                catch { _saveTestSession["phase"] = "recoveryRequired"; throw; }
                _saveTestRestoringStartup = false;
            }
            else
            {
                if (_saveTestSession["phase"].Value != "loadingFixture")
                    throw new InvalidOperationException("Unexpected save-test startup; explicit restore is required.");
                var pinned = SaveSafetyCatalog.Read(Path.Combine(_saveTestDirectory, "fixtures", _saveTestSession["fixtureGeneration"].Value + ".txt"), Character.getVersion());
                if (pinned.Sha256 != _saveTestSession["fixture"]["sha256"].Value)
                    throw new InvalidDataException("Immutable fixture changed before fresh-owner readiness.");
                VerifySaveTestLoadedIdentity(pinned, SaveTestCurrentIdentity(Character));
                _saveTestSession["phase"] = "running";
                PersistSaveTestJournal();
            }
            Loader.SaveTestMainReady(this);
        }

        private static JSONObject NewSaveTestSession(string id)
        {
            var session = new JSONObject(); session["version"] = 1; session["supported"] = true;
            session["sessionId"] = id; session["phase"] = "starting"; session["fixtureGeneration"] = "0";
            session["fixture"] = new JSONObject(); session["restoreTarget"] = new JSONObject();
            session["observed"] = new JSONObject(); session["resetEvidence"] = new JSONObject();
            session["offlineProgress"]["mode"] = "native";
            session["restoration"]["verified"] = false; session["error"] = "";
            return session;
        }

        private static void PersistSaveTestJournal()
        {
            var journal = new JSONObject(); journal["version"] = 1;
            journal["sessionId"] = _saveTestSession["sessionId"].Value;
            journal["runtimeRoot"] = Path.GetFullPath(_dir);
            journal["nativeRoot"] = Path.GetFullPath(UnityEngine.Application.persistentDataPath);
            journal["manifestSha256"] = _saveTestManifestHash ?? "";
            journal["bundleReady"] = _saveTestBundleReady;
            journal["nativeMutationStarted"] = _saveTestNativeMutation;
            journal["originalReleased"] = _saveTestOriginalReleased;
            journal["fenceUtc"] = _saveTestFenceUtc.ToString("o");
            journal["original"] = SaveIdentityJson(_saveTestOriginal);
            journal["session"] = JsonFiles.ParseObject(_saveTestSession.ToString());
            WriteSaveSafetyJson(SaveTestJournalPath, journal);
        }

        private static void ResumeSaveTestBundle()
        {
            var verification = SaveTestBundle.Resume(_dir, UnityEngine.Application.persistentDataPath, _saveTestDirectory, _saveTestManifestHash);
            if (!verification["verified"].AsBool)
                throw new InvalidDataException("Bundle disk-resume verification was not established.");
        }

        internal void RecordSaveTestFailure(Exception error)
        {
            _saveTestExpectedStartup = false;
            _saveTestSession["phase"] = "recoveryRequired";
            _saveTestSession["error"] = error.Message;
            _saveTestSession["restoration"]["verified"] = false;
            try { PersistSaveTestJournal(); }
            catch (Exception persistenceError) { Loader.TraceStartup("Save-test journal failure: " + persistenceError); }
        }

        private static bool SaveTestHex(string value, int length) => value != null && value.Length == length
            && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');

        private static void ValidateSaveTestCommand(string command, JSONNode args)
        {
            var count = command == "startSaveTest" || command == "loadSaveTestFixture" || command == "setSaveTestStage" ? 4 : 2;
            if (args == null || !args.IsObject || args.Count != count
                || !args["expectedStartedAt"].IsString || args["expectedStartedAt"].Value != ExternalUiStartedAt.ToString("o")
                || !args["sessionId"].IsString || !SaveTestHex(args["sessionId"].Value, 32))
                throw new FormatException(command + " requires this injector's expectedStartedAt and a lowercase 32-hex sessionId, with exactly the documented arguments.");
            if (command == "startSaveTest")
            {
                if (!args["acceptNativeSideEffects"].IsBoolean || !args["acceptNativeSideEffects"].AsBool)
                    throw new FormatException("Explicit acceptNativeSideEffects:true is required (native offline progress, Steam Cloud and achievements).");
            }
            else if (command != "restoreSaveTest" || SaveTestActive)
            {
                if (_saveTestSession == null || args["sessionId"].Value != _saveTestSession["sessionId"].Value)
                    throw new InvalidOperationException("Save-test session changed; no operation was attempted.");
                if (command != "restoreSaveTest" && (!args["expectedFixtureGeneration"].IsString
                    || args["expectedFixtureGeneration"].Value != _saveTestSession["fixtureGeneration"].Value))
                    throw new InvalidOperationException("Fixture generation changed; no operation was attempted.");
            }
            if (command == "setSaveTestStage" && (!args["fixtureSha256"].IsString
                || args["fixtureSha256"].Value != _saveTestSession["fixture"]["sha256"].Value))
                throw new InvalidOperationException("Fixture hash changed; no stage operation was attempted.");
            if (command == "startSaveTest" || command == "loadSaveTestFixture") ValidateSaveTestFixture(args["fixture"]);
        }

        private static void ValidateSaveTestFixture(JSONNode fixture)
        {
            if (fixture == null || !fixture.IsObject || fixture.Count != 6
                || !fixture["name"].IsString || string.IsNullOrWhiteSpace(fixture["name"].Value) || fixture["name"].Value.Length > 128
                || !fixture["path"].IsString || !Path.IsPathRooted(fixture["path"].Value)
                || fixture["path"].Value.StartsWith("\\\\?\\", StringComparison.Ordinal) || fixture["path"].Value.StartsWith("\\\\.\\", StringComparison.Ordinal)
                || Path.GetPathRoot(fixture["path"].Value).Length < 3
                || !fixture["sha256"].IsString || !SaveTestHex(fixture["sha256"].Value, 64)
                || !fixture["plan"].IsString || !RegexSafeSaveTestPlan(fixture["plan"].Value)
                || !fixture["stageIndex"].IsNumber || fixture["stageIndex"].AsDouble < -1 || fixture["stageIndex"].AsDouble > 1023
                || fixture["stageIndex"].AsDouble != Math.Floor(fixture["stageIndex"].AsDouble)
                || !fixture["scenario"].IsString || !new[] { "allocation", "early", "fallback" }.Contains(fixture["scenario"].Value))
                throw new FormatException("fixture requires exactly name, absolute path, lowercase sha256, plan, stageIndex (-1 or 0..1023), and scenario (allocation, early, fallback).");
        }

        private static bool RegexSafeSaveTestPlan(string value) => !string.IsNullOrEmpty(value) && value.Length <= 128
            && value != "." && value != ".." && value.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z'
                || c >= '0' && c <= '9' || c == '-' || c == '_' || c == '.');

        private void RequireSaveTestOriginalHealthy()
        {
            if (!_started || _unloaded || _injectorLease == null || !ReferenceEquals(reference, this)
                || _executionLifecycle != ExecutionLifecycle.Running || Character?.stats == null || Controller?.midDrag != false)
                throw new InvalidOperationException("Save testing requires a healthy, running original owner with no inventory drag.");
            if (Character.bossController != null && (Character.bossController.isFighting || Character.bossController.nukeBoss))
                throw new InvalidOperationException("Wait for the original native boss fight/nuke to stop before save testing; no session was admitted.");
            if (_resourceSetupExecutor.Application.BlockedLanes != 0
                || SelectedSetupConflicts.TryGetValue(Character, out var conflict) && conflict.Rebirth == Character.stats.rebirthNumber
                    && (conflict.Gear != null || conflict.Support != null || conflict.Route != null || conflict.NativeSettings != null)
                || ManualAdventureGearConflicts.TryGetValue(Character, out _) || ManualAdventureSupportConflicts.TryGetValue(Character, out _)
                || _globalPresetNativeTargetConflict != null)
                throw new InvalidOperationException("Resolve the original model/resource conflict before testing; original unresolved evidence is never erased.");
            if (_finiteActionInvoking || _manualRebirthPending || _customPreRebirthPrepRunning || _customPreRebirthPrepCompleted)
                throw new InvalidOperationException("Resolve pending irreversible original actions before save testing.");
            VerifySaveCandidatesForMutation();
            var safety = GetSaveSafetyRuntime();
            if (safety["status"].Value != "verified")
                throw new InvalidOperationException("Prepare the longest verified original save before testing: " + safety["message"].Value);
        }

        internal string ExecuteSaveTestCommand(string command, JSONNode args, out JSONNode data)
        {
            data = new JSONObject();
            if (!new[] { "startSaveTest", "loadSaveTestFixture", "setSaveTestStage", "restoreSaveTest" }.Contains(command))
                throw new FormatException("Unknown save-test command.");
            ValidateSaveTestCommand(command, args);
            try
            {
                if (command == "restoreSaveTest") return RestoreSaveTest(args["sessionId"].Value);
                if (command == "setSaveTestStage") return SetSaveTestStage();
                if (command == "startSaveTest")
                {
                    if (SaveTestActive) throw new InvalidOperationException("Restore the current save-test session first.");
                    var directory = Path.Combine(_dir, "save-safety", "tests", args["sessionId"].Value);
                    if (Directory.Exists(directory) || File.Exists(directory)
                        || _saveTestSession != null && _saveTestSession["sessionId"].Value == args["sessionId"].Value)
                        throw new IOException("Session ID has already been used or cancelled; choose a new client-generated ID.");
                    RequireSaveTestOriginalHealthy();
                    // Decode and plan-check before creating an intent or quiescing the original.
                    ReadSaveTestFixture(args["fixture"], Character, out var unusedBytes);
                    ValidateSaveTestPlan(args["fixture"]);
                    _saveTestSession = NewSaveTestSession(args["sessionId"].Value);
                    _saveTestSession["fixture"] = JsonFiles.ParseObject(args["fixture"].ToString());
                    _saveTestDirectory = directory; _saveTestCharacter = Character; _saveTestOriginal = null;
                    _saveTestBundleReady = _saveTestBundleTrusted = _saveTestNativeMutation = _saveTestOriginalReleased = false;
                    _saveTestManifestHash = null; _saveTestFenceUtc = DateTime.UtcNow;
                    _saveTestInternalRestart = true;
                    PersistSaveTestJournal(); // Durable intent precedes even the first quiesce write.
                    Loader.RetainSaveTestRecovery(this);
                    QuiesceExecution("Preparing protected original for save testing.");
                    SaveIdentity fresh;
                    _saveTestOriginalCapture++;
                    try { fresh = CaptureVerifiedLocalSave(); }
                    finally { _saveTestOriginalCapture--; }
                    var verified = SaveSafetyCatalog.ReadSnapshot(fresh.Path, Character.getVersion(), out var originalBytes);
                    if (verified.Sha256 != fresh.Sha256) throw new IOException("Fresh protected original changed during preparation.");
                    WriteSaveTestImmutable(Path.Combine(directory, "original.txt"), originalBytes);
                    _saveTestOriginal = SaveSafetyCatalog.Read(Path.Combine(directory, "original.txt"), Character.getVersion());
                    _saveTestSession["restoreTarget"] = SaveIdentityJson(_saveTestOriginal);
                    PersistSaveTestJournal();
                    try { Loader.ReplaceSaveTestMain(this, false); }
                    finally { _saveTestOriginalReleased = SaveTestOwnerStopped; }
                    SaveTestBundle.Capture(_dir, UnityEngine.Application.persistentDataPath, directory);
                    _saveTestManifestHash = JsonFiles.ComputeRevision(File.ReadAllBytes(Path.Combine(directory, "bundle", "manifest.json")));
                    _saveTestBundleReady = true;
                    _saveTestBundleTrusted = true;
                    PersistSaveTestJournal();
                }
                else if (_saveTestSession["phase"].Value != "running" || !ReferenceEquals(reference, this) || !_started)
                    throw new InvalidOperationException("A ready running fixture is required before loading its successor.");
                LoadSaveTestFixture(args["fixture"]);
                return "Pinned fixture loaded and reset; waiting for the fresh Main component to become ready.";
            }
            catch (Exception error)
            {
                if (SaveTestActive && _saveTestSession["phase"].Value != "running")
                {
                    RecordSaveTestFailure(error);
                    if (_saveTestOriginalReleased && !_unloaded && ReferenceEquals(reference, this))
                        Loader.ReplaceSaveTestMain(this, true);
                }
                throw;
            }
            finally { data["saveTestSession"] = GetSaveTestRuntime(); }
        }

        private static SaveIdentity ReadSaveTestFixture(JSONNode fixture, Character character, out byte[] bytes)
        {
            var identity = SaveSafetyCatalog.ReadSnapshot(fixture["path"].Value, character.getVersion(), out bytes);
            if (identity.Sha256 != fixture["sha256"].Value) throw new IOException("Fixture changed since selection; no native load was attempted.");
            if (character.importExport.getSaveDataFromString(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff')) == null)
                throw new InvalidDataException("Native decoder rejected the immutable fixture.");
            return identity;
        }

        private static void ValidateSaveTestPlan(JSONNode fixture)
        {
            var plan = new CustomPlanStore(Path.Combine(_dir, "custom-plans")).Load(fixture["plan"].Value);
            if (!plan.StageSchedulerEnabled || plan.Stages == null || plan.Stages.Length == 0
                || fixture["stageIndex"].AsInt >= plan.Stages.Length)
                throw new InvalidOperationException("Fixture plan must have a valid stage schedule and pinned stage index.");
        }

        private void LoadSaveTestFixture(JSONNode fixture)
        {
            var character = _saveTestCharacter;
            var pinned = ReadSaveTestFixture(fixture, character, out var bytes);
            ValidateSaveTestPlan(fixture);
            var generation = checked(long.Parse(_saveTestSession["fixtureGeneration"].Value, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
            WriteSaveTestImmutable(Path.Combine(_saveTestDirectory, "fixtures", generation + ".txt"), bytes);
            _saveTestSession["fixture"] = JsonFiles.ParseObject(fixture.ToString());
            _saveTestSession["fixtureGeneration"] = generation;
            _saveTestSession["resetEvidence"] = new JSONObject();
            _saveTestSession["resetEvidence"]["verified"] = false;
            _saveTestSession["phase"] = "loadingFixture"; _saveTestSession["error"] = "";
            PersistSaveTestJournal();
            if (!_unloaded) Loader.ReplaceSaveTestMain(this, true);
            SaveTestBundle.PrepareFixture(_saveTestDirectory, fixture["plan"].Value);
            // This flag is durable BEFORE native mutation, including a load which throws halfway.
            _saveTestNativeMutation = true;
            PersistSaveTestJournal();
            if (character.bossController != null)
            {
                character.bossController.isFighting = false;
                character.bossController.nukeBoss = false;
            }
            LoadSaveTestNative(character, pinned, bytes);
            _saveTestSession["resetEvidence"] = ResetSaveTestFixture(character, fixture);
            PersistSaveTestJournal();
            _saveTestRestoringStartup = false;
            _saveTestExpectedStartup = true;
            Loader.Init();
        }

        private static SaveIdentity SaveTestCurrentIdentity(Character character)
        {
            if (character?.stats == null || character.totalPlaytime == null || character.rebirthTime == null)
                throw new InvalidOperationException("Native loaded identity is unavailable.");
            var total = character.totalPlaytime.totalseconds; var elapsed = character.rebirthTime.totalseconds;
            if (!(total >= 0 && total <= double.MaxValue) || !(elapsed >= 0 && elapsed <= total)
                || character.stats.rebirthNumber < 0 || character.lastTime < 0)
                throw new InvalidOperationException("Native loaded clocks/rebirth are invalid.");
            return new SaveIdentity("", "", total, character.stats.rebirthNumber, elapsed, character.getVersion(), character.lastTime, character.playerName);
        }

        private static void LoadSaveTestNative(Character character, SaveIdentity pinned, byte[] bytes)
        {
            if (JsonFiles.ComputeRevision(bytes) != pinned.Sha256) throw new IOException("Pinned native bytes changed.");
            var native = character.importExport.getSaveDataFromString(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff'));
            if (native == null) throw new InvalidDataException("Native decoder rejected the pinned save.");
            character.saveLoad.loadintoGame(native);
            var observed = SaveTestCurrentIdentity(character);
            VerifySaveTestLoadedIdentity(pinned, observed);
            _saveTestSession["observed"] = SaveIdentityJson(observed);
            var offline = new JSONObject(); offline["mode"] = "native";
            offline["totalGainSeconds"] = SaveNumber(observed.TotalPlaytimeSeconds - pinned.TotalPlaytimeSeconds);
            offline["rebirthGainSeconds"] = SaveNumber(observed.RebirthSeconds - pinned.RebirthSeconds);
            offline["sourceLastTime"] = pinned.LastTime; offline["observedLastTime"] = observed.LastTime;
            _saveTestSession["offlineProgress"] = offline;
            // Never retire uncertainty on decode alone or on an unverified/partial native load.
            GearOptimizer.ClearCache();
            CustomStageAllocator.ResetAfterVerifiedSaveTestLoad();
            BlackBarAllocator.ResetAfterVerifiedSaveTestLoad();
            CustomPlanRebirth.ResetRuntimeState();
            SelectedSetupConflicts.Remove(character);
            ManualAdventureGearConflicts.Remove(character);
            ManualAdventureSupportConflicts.Remove(character);
            var previous = Character;
            try
            {
                Character = character;
                var application = new ResourceApplication();
                var stats = character.stats; var elapsed = character.rebirthTime; var total = character.totalPlaytime;
                // Normally native epoch replacement retires evidence. An interrupted fixture
                // may have recorded an unknown epoch; only this verified load may retire it.
                if (application.BlockedLanes != 0 && !application.Reauthorize(character, 7,
                    () => ReferenceEquals(Character, character) && ReferenceEquals(character.stats, stats)
                        && ReferenceEquals(character.rebirthTime, elapsed) && ReferenceEquals(character.totalPlaytime, total)
                        && character.stats.rebirthNumber == observed.Rebirth))
                    throw new InvalidOperationException("Verified native load could not retire disposable resource evidence.");
            }
            finally { Character = previous; }
        }

        internal static void VerifySaveTestLoadedIdentity(SaveIdentity pinned, SaveIdentity observed)
        {
            if (pinned == null || observed == null || observed.Version < pinned.Version || observed.Version > SaveSafetyCatalog.NativeVersion
                || !(observed.TotalPlaytimeSeconds >= 0 && observed.TotalPlaytimeSeconds <= double.MaxValue)
                || !(observed.RebirthSeconds >= 0 && observed.RebirthSeconds <= observed.TotalPlaytimeSeconds)
                || observed.LastTime < 0 || observed.Rebirth < 0
                || observed.PlayerName != pinned.PlayerName || observed.Rebirth != pinned.Rebirth
                || observed.TotalPlaytimeSeconds + .001 < pinned.TotalPlaytimeSeconds
                || observed.RebirthSeconds + .001 < pinned.RebirthSeconds
                || Math.Abs((observed.TotalPlaytimeSeconds - observed.RebirthSeconds)
                    - (pinned.TotalPlaytimeSeconds - pinned.RebirthSeconds)) > 5)
                throw new InvalidOperationException("Native load did not establish the pinned player's progression/rebirth; explicit restore is required.");
        }

        private static JSONObject ResetSaveTestFixture(Character character, JSONNode fixture)
        {
            // The old owner is gone. Supply only the native model during this synchronous reset;
            // the fresh owner must never observe pre-reset allocations or support selections.
            Character = character;
            try
            {
                var beforeIdle = new[] { character.idleEnergy, character.magic.idleMagic };
                var returned = new long[2];
                foreach (var target in ResourceWriter.CaptureAllocations(character))
                {
                    var lane = ResourceWriter.Lane(target.Key);
                    if (lane >= 2 || target.Key == ResourceTarget.BasicAttack || target.Key == ResourceTarget.BasicDefense) continue;
                    foreach (var amount in target.Value ?? new long[0]) returned[lane] = checked(returned[lane] + amount);
                }
                ResourceWriter.Recall(true);
                ResourceWriter.Recall(false);
                var map = ResourceWriter.CaptureAllocations(character);
                if (map.Any(x => ResourceWriter.Lane(x.Key) < 2 && x.Key != ResourceTarget.BasicAttack
                    && x.Key != ResourceTarget.BasicDefense && x.Value != null && x.Value.Any(v => v != 0)))
                    throw new InvalidOperationException("Native recall did not empty every nonbasic E/M allocation.");
                var purchased = character.diggers.diggers.Select(x => x.maxLevel).ToArray();
                character.allDiggers.clearAllActiveDiggers();
                foreach (var id in character.beards.activeBeards.ToArray()) character.allBeards.deactivateBeard(id);
                if (character.beards.activeBeards.Count != 0 || character.beards.beards.Any(x => x.active)
                    || character.diggers.activeDiggers.Count != 0 || character.diggers.diggers.Any(x => x.active)
                    || !purchased.SequenceEqual(character.diggers.diggers.Select(x => x.maxLevel)))
                    throw new InvalidOperationException("Native support reset did not disable all selections while preserving purchased digger levels.");
                if (fixture["scenario"].Value == "fallback") SetSaveTestFallbackTargets(character);
                var result = new JSONObject(); result["verified"] = true;
                result["nonbasicEnergyZero"] = true; result["nonbasicMagicZero"] = true;
                result["exactNativeRecallVerified"] = true; result["basicTrainingPreserved"] = true;
                result["beardsInactive"] = true; result["diggersInactive"] = true; result["purchasedDiggerLevelsPreserved"] = true;
                result["idleEnergy"] = character.idleEnergy.ToString(CultureInfo.InvariantCulture);
                result["idleMagic"] = character.magic.idleMagic.ToString(CultureInfo.InvariantCulture);
                for (var lane = 0; lane < 2; lane++)
                {
                    var expectedIdle = checked(beforeIdle[lane] + returned[lane]);
                    var observedIdle = lane == 0 ? character.idleEnergy : character.magic.idleMagic;
                    if (observedIdle != expectedIdle) throw new InvalidOperationException("Fixture support reset changed the exactly returned resource pool.");
                    var row = new JSONObject(); row["beforeIdle"] = beforeIdle[lane].ToString(CultureInfo.InvariantCulture);
                    row["returned"] = returned[lane].ToString(CultureInfo.InvariantCulture);
                    row["expectedIdle"] = expectedIdle.ToString(CultureInfo.InvariantCulture);
                    row["observedIdle"] = observedIdle.ToString(CultureInfo.InvariantCulture);
                    result[lane == 0 ? "energy" : "magic"] = row;
                }
                result["fallbackTargetsCompleted"] = fixture["scenario"].Value == "fallback";
                result["staticTransientsResetAfterVerifiedLoad"] = true;
                // The native load must retire model evidence by its actual epoch markers.
                if (new ResourceApplication().BlockedLanes != 0)
                    throw new InvalidOperationException("Verified native load did not retire prior resource-epoch uncertainty.");
                return result;
            }
            finally { Character = null; }
        }

        private static void SetSaveTestFallbackTargets(Character character)
        {
            var settings = new SavedSettings(_dir);
            if (!settings.LoadSettings()) throw new InvalidDataException("Fixture settings are unavailable for fallback weights.");
            var track = character.settings.nguLevelTrack;
            if (track != difficulty.normal && track != difficulty.evil && track != difficulty.sadistic)
                throw new InvalidOperationException("Fallback scenario has an unsupported NGU track.");
            foreach (var energy in new[] { true, false })
            {
                var weights = energy ? settings.EnergyNguWeights : settings.MagicNguWeights;
                for (var i = 0; i < weights.Length; i++)
                {
                    if (weights[i] <= 0) continue;
                    var level = energy ? (track == difficulty.normal ? character.NGU.skills[i].level : track == difficulty.evil ? character.NGU.skills[i].evilLevel : character.NGU.skills[i].sadisticLevel)
                        : track == difficulty.normal ? character.NGU.magicSkills[i].level : track == difficulty.evil ? character.NGU.magicSkills[i].evilLevel : character.NGU.magicSkills[i].sadisticLevel;
                    if (level <= 0) throw new InvalidOperationException("Fallback scenario unsupported: a positive-weight NGU has zero current level; zero means unlimited, not complete.");
                }
            }
            foreach (var energy in new[] { true, false })
            {
                var weights = energy ? settings.EnergyNguWeights : settings.MagicNguWeights;
                for (var i = 0; i < weights.Length; i++)
                {
                    if (weights[i] <= 0) continue;
                    if (energy)
                    {
                        var row = character.NGU.skills[i];
                        if (track == difficulty.normal) row.target = row.level;
                        else if (track == difficulty.evil) row.evilTarget = row.evilLevel;
                        else row.sadisticTarget = row.sadisticLevel;
                    }
                    else
                    {
                        var row = character.NGU.magicSkills[i];
                        if (track == difficulty.normal) row.target = row.level;
                        else if (track == difficulty.evil) row.evilTarget = row.evilLevel;
                        else row.sadisticTarget = row.sadisticLevel;
                    }
                }
            }
        }

        private string SetSaveTestStage()
        {
            if (_saveTestSession["phase"].Value != "running" || !ReferenceEquals(reference, this) || !_started || _unloaded)
                throw new InvalidOperationException("Wait for the fresh fixture owner to be ready.");
            var fixture = _saveTestSession["fixture"];
            if (fixture["stageIndex"].AsInt == -1) return "Natural stage selection retained.";
            var plan = LoadRuntimeCustomPlan();
            GetProtectedStage(plan);
            var stage = new JSONObject(); stage["name"] = fixture["plan"].Value;
            stage["expectedRevision"] = plan.RuntimeRevision;
            stage["expectedEntry"] = _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture);
            stage["action"] = "set"; stage["stageIndex"] = fixture["stageIndex"].AsInt;
            return OverrideCustomStage(stage);
        }

        private static void RecordSaveTestRestoration(JSONObject report)
        {
            var previous = _saveTestSession["restoration"];
            foreach (var field in new[] { "archiveErrors", "filesLost" })
            {
                var combined = new JSONArray();
                foreach (var row in previous[field].Children) combined.Add(row);
                foreach (var row in report[field].Children) combined.Add(row);
                report[field] = combined;
            }
            if (previous["archiveComplete"].IsBoolean && !previous["archiveComplete"].AsBool
                || report["archiveErrors"].Count != 0 || report["filesLost"].Count != 0)
                report["archiveComplete"] = false;
            _saveTestSession["restoration"] = report;
        }

        private string CancelUnadmittedSaveTest(string sessionId)
        {
            if (SaveTestActive) throw new InvalidOperationException("Restore the active save-test session before cancelling another ID.");
            var directory = Path.GetFullPath(Path.Combine(_dir, "save-safety", "tests", sessionId));
            var path = Path.Combine(directory, "cancelled.json");
            for (var parent = path; !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                if ((File.Exists(parent) || Directory.Exists(parent)) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Cancellation cannot traverse a reparse point; review the session directory.");
            var existing = Directory.Exists(directory);
            if (File.Exists(directory)) throw new IOException("The session path is occupied; review it before cancellation.");
            if (existing)
            {
                var entries = Directory.EnumerateFileSystemEntries(directory).Take(2).ToArray();
                if (entries.Length != 1 || !string.Equals(entries[0], path, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(path) || new FileInfo(path).Length > 64 * 1024)
                    throw new InvalidDataException("An existing session directory may contain protected originals; review it instead of cancelling blindly.");
                var cancelled = JsonFiles.ParseObject(File.ReadAllText(path));
                if (!cancelled["version"].IsNumber || cancelled["version"].AsDouble != 1
                    || !cancelled["sessionId"].IsString || cancelled["sessionId"].Value != sessionId
                    || !cancelled["phase"].IsString || cancelled["phase"].Value != "restored"
                    || !cancelled["restoration"]["cancelledBeforeStart"].IsBoolean || !cancelled["restoration"]["cancelledBeforeStart"].AsBool)
                    throw new InvalidDataException("Existing cancellation evidence is invalid; review the session directory.");
            }
            var observed = SaveTestCurrentIdentity(Character);
            var session = NewSaveTestSession(sessionId);
            session["phase"] = "restored";
            session["restoreTarget"] = SaveIdentityJson(observed); session["observed"] = SaveIdentityJson(observed);
            session["restoration"]["verified"] = true; session["restoration"]["nativeVerified"] = true;
            session["restoration"]["filesVerified"] = true; session["restoration"]["cancelledBeforeStart"] = true;
            session["restoration"]["nativeLoadPerformed"] = false;
            session["offlineProgress"]["totalGainSeconds"] = "0";
            // Admission and cancellation share the Unity thread. A durable directory/marker
            // collision retires even a start request which has not yet been claimed.
            if (!existing) WriteSaveTestImmutable(path, new UTF8Encoding(false, true).GetBytes(session.ToString(2)));
            _saveTestSession = session; _saveTestDirectory = directory; _saveTestOriginal = null;
            _saveTestCharacter = Character; _saveTestManifestHash = null;
            _saveTestBundleReady = _saveTestBundleTrusted = _saveTestNativeMutation = _saveTestOriginalReleased = false;
            _saveTestExpectedStartup = _saveTestRestoringStartup = _saveTestInternalRestart = false;
            PersistSaveTestJournal();
            return "Session cancelled before admission; no native save was loaded or rolled back.";
        }

        private string RestoreSaveTest(string sessionId)
        {
            if (!SaveTestActive)
            {
                if (_saveTestSession != null && _saveTestSession["sessionId"].Value == sessionId
                    && _saveTestSession["phase"].Value == "restored" && !_saveTestSession["restoration"]["cancelledBeforeStart"].AsBool)
                    return "Protected original is already restored and verified.";
                return CancelUnadmittedSaveTest(sessionId);
            }
            _saveTestSession["phase"] = "restoring"; _saveTestSession["error"] = "";
            PersistSaveTestJournal();
            var character = _saveTestCharacter ?? Character ?? UnityEngine.Object.FindObjectOfType<Character>();
            if (character == null) throw new InvalidOperationException("Native Character is unavailable; protected recovery remains retained.");
            var owner = reference;
            if (!ReferenceEquals(owner, null) && !owner._unloaded)
                Loader.ReplaceSaveTestMain(owner, _saveTestOriginalReleased || _saveTestNativeMutation || !owner._started);
            if (_saveTestBundleReady && !_saveTestBundleTrusted)
            {
                ResumeSaveTestBundle();
                _saveTestBundleTrusted = true;
            }
            if (_saveTestOriginal == null && !string.IsNullOrEmpty(_saveTestSession["restoreTarget"]["sha256"].Value))
            {
                var original = SaveSafetyCatalog.Read(Path.Combine(_saveTestDirectory, "original.txt"), character.getVersion());
                if (original.Sha256 != _saveTestSession["restoreTarget"]["sha256"].Value)
                    throw new InvalidDataException("Protected original save hash changed.");
                _saveTestOriginal = original;
            }
            if (_saveTestNativeMutation && (!_saveTestBundleReady || _saveTestOriginal == null))
                throw new InvalidDataException("Protected original/bundle is unavailable; refusing an unverified restore.");
            if (_saveTestNativeMutation && character.bossController != null)
            {
                // These controller flags are transient, not part of the protected save.
                // Never cancel original activity: this path only discards a loaded fixture.
                character.bossController.isFighting = false;
                character.bossController.nukeBoss = false;
            }
            if (_saveTestOriginal != null && _saveTestNativeMutation)
            {
                var pinned = SaveSafetyCatalog.ReadSnapshot(_saveTestOriginal.Path, character.getVersion(), out var bytes);
                if (pinned.Sha256 != _saveTestOriginal.Sha256) throw new IOException("Protected original save changed.");
                // Native local writes are synchronous: restore their exact originals AFTER
                // the native baseline load, in this same Unity-thread call, before any new owner.
                LoadSaveTestNative(character, pinned, bytes);
            }
            if (_saveTestBundleReady) RecordSaveTestRestoration(SaveTestBundle.Restore(_saveTestDirectory, true));
            _saveTestSession["restoration"]["nativeVerified"] = true;
            _saveTestSession["restoration"]["filesVerified"] = true;
            _saveTestSession["restoration"]["verified"] = false;
            PersistSaveTestJournal();
            _saveTestRestoringStartup = true;
            _saveTestExpectedStartup = true;
            Loader.Init();
            return "Protected native baseline and original files restored; waiting for the fresh original Main to become ready.";
        }

        private static void WriteSaveTestImmutable(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (JsonFiles.ComputeRevision(File.ReadAllBytes(path)) != JsonFiles.ComputeRevision(bytes))
                throw new IOException("Immutable session file failed read-back verification.");
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }
    }
}
