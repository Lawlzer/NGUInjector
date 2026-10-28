using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.ExceptionServices;
using System.Web.Script.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Executes the compiled coordinator, lifecycle and bundle. Only Unity engine externs
// (object identity, cancellation and persistentDataPath) are replaced in memory.
public static class SessionChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Assembly Plugin, Game, Core;
    private static Type Main, Loader, Bundle;
    private static object Owner;
    private static string Root, Managed;
    public static string NativeRoot;
    private static int Assertions;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    public static int MainEntry(string[] args)
    {
        try
        {
            Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "SessionChecks-" + Guid.NewGuid().ToString("N"));
            NativeRoot = Path.Combine(Root, "native"); Directory.CreateDirectory(NativeRoot);
            Managed = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            Core = LoadUnityBoundary(Path.Combine(Managed, "UnityEngine.CoreModule.dll"));
            Game = Assembly.Load(File.ReadAllBytes(Path.Combine(Managed, "Assembly-CSharp.dll")));
            Plugin = Assembly.Load(File.ReadAllBytes(args[0]));
            Main = Plugin.GetType("NGUInjector.Main", true); Loader = Plugin.GetType("NGUInjector.Loader", true);
            Bundle = Plugin.GetType("NGUInjector.SaveTestBundle", true);
            Owner = FormatterServices.GetUninitializedObject(Main);
            CheckAdmission(); CheckCommandSchema(); CheckGeneration(); CheckLoadedIdentity();
            CheckImmutableCapture(); CheckStaticResets(); CheckRestoredIdempotence();
            CheckCancellationBeforeStart(); CheckCancellationRejectsOtherActiveSession(); CheckCancellationProtectsExistingBundle(); CheckOriginalFightPreflight();
            CheckReadyRestoration(); CheckArchiveWarningsSurviveReadiness(); CheckFailedReadyRestoration(); CheckColdRecoveryHold(); CheckLeaseHandover(); CheckLoaderCleanupFailure();
            Console.WriteLine("PASS: " + Assertions + " compiled save-session assertions; no live commands, deployment, injection or game restart.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (Root != null && Directory.Exists(Root))
            {
                foreach (var file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Root, true);
            }
        }
    }

    private static Assembly Resolve(object sender, ResolveEventArgs request)
    {
        var name = new AssemblyName(request.Name).Name;
        if (name == "UnityEngine.CoreModule" && Core != null) return Core;
        if (name == "Assembly-CSharp" && Game != null) return Game;
        var path = Path.Combine(Managed, name + ".dll");
        return File.Exists(path) ? Assembly.Load(File.ReadAllBytes(path)) : null;
    }

    private static Assembly LoadUnityBoundary(string path)
    {
        using (var module = AssemblyDefinition.ReadAssembly(path))
        {
            foreach (var type in module.MainModule.Types)
            foreach (var method in type.Methods)
            {
                var equality = type.FullName == "UnityEngine.Object" && (method.Name == "op_Equality" || method.Name == "op_Inequality");
                var cancellation = type.FullName == "UnityEngine.MonoBehaviour" && (method.Name == "CancelInvoke" || method.Name == "StopAllCoroutines");
                var retention = type.FullName == "UnityEngine.Object" && (method.Name == "DontDestroyOnLoad" || method.Name == "Destroy");
                var sceneLookup = type.FullName == "UnityEngine.Object" && method.Name == "FindObjectOfType";
                var activation = type.FullName == "UnityEngine.GameObject" && method.Name == "SetActive";
                var nativePath = type.FullName == "UnityEngine.Application" && method.Name == "get_persistentDataPath";
                if (!equality && !cancellation && !retention && !sceneLookup && !activation && !nativePath) continue;
                method.ImplAttributes = Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed;
                method.Attributes &= ~Mono.Cecil.MethodAttributes.PInvokeImpl;
                method.Body = new Mono.Cecil.Cil.MethodBody(method);
                var il = method.Body.GetILProcessor();
                if (equality)
                {
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ceq);
                    if (method.Name == "op_Inequality") { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); }
                }
                if (nativePath) il.Emit(OpCodes.Ldsfld, module.MainModule.ImportReference(typeof(SessionChecks).GetField("NativeRoot", All)));
                if (sceneLookup) il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }
            using (var bytes = new MemoryStream()) { module.Write(bytes); return Assembly.Load(bytes.ToArray()); }
        }
    }

    private static object Parse(object value) => Call(Plugin.GetType("NGUInjector.JsonFiles"), "ParseObject", Json.Serialize(value));
    private static Dictionary<string, object> Runtime() => Json.Deserialize<Dictionary<string, object>>(Call(Main, "GetSaveTestRuntime").ToString());
    private static Dictionary<string, object> Fixture() => new Dictionary<string, object> {
        { "name", "fixture" }, { "path", Path.Combine(Root, "fixture.txt") }, { "sha256", new string('b', 64) },
        { "plan", "default" }, { "stageIndex", -1 }, { "scenario", "allocation" } };
    private static Dictionary<string, object> Args(string command)
    {
        var result = new Dictionary<string, object> { { "expectedStartedAt", ((DateTime)Get(Main, "ExternalUiStartedAt")).ToString("o") }, { "sessionId", new string('a', 32) } };
        if (command == "startSaveTest") { result["acceptNativeSideEffects"] = true; result["fixture"] = Fixture(); }
        if (command == "loadSaveTestFixture") { result["expectedFixtureGeneration"] = "7"; result["fixture"] = Fixture(); }
        if (command == "setSaveTestStage") { result["expectedFixtureGeneration"] = "7"; result["fixtureSha256"] = new string('b', 64); }
        return result;
    }
    private static void Session(string phase)
    {
        Set(Main, "_saveTestSession", Parse(new Dictionary<string, object> {
            { "version", 1 }, { "supported", true }, { "sessionId", new string('a', 32) }, { "phase", phase },
            { "fixtureGeneration", "7" }, { "fixture", Fixture() } }));
    }
    private static void Assert(bool value, string message) { Assertions++; if (!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is FormatException || error is InvalidOperationException || error is IOException || error is InvalidDataException)
        { Assertions++; return; }
        throw new Exception("Expected rejection: " + message);
    }

    private static void CheckAdmission()
    {
        Set(Main, "_saveTestSession", null);
        Assert((string)Runtime()["phase"] == "idle", "No session must report idle.");
        Session("running");
        foreach (var command in new[] { "status", "readPlan", "readPresetLibrary", "startSaveTest", "loadSaveTestFixture", "setSaveTestStage", "restoreSaveTest" })
            Assert((bool)Call(Main, "SaveTestCommandAllowed", command, null), "Dedicated/read command admission: " + command);
        foreach (var command in new[] { "applySettingsPatch", "setPlanStage", "unload", "quickSave", "captureLocalSave", "loadLongestSave", "safeProbe" })
            Assert(!(bool)Call(Main, "SaveTestCommandAllowed", command, null), "Active session must reject " + command);
        Assert(!(bool)Main.GetProperty("SaveTestCaptureAllowed", All).GetValue(null), "Fixture captures must not enter production backups.");
        var fence = DateTime.UtcNow; Set(Main, "_saveTestFenceUtc", fence);
        Session("restored");
        Assert(!(bool)Call(Main, "SaveTestCommandCreatedAtAllowed", "applySettingsPatch", fence.AddSeconds(-1)), "Restored sessions still fence old queued mutations.");
        Assert((bool)Call(Main, "SaveTestCommandCreatedAtAllowed", "applySettingsPatch", fence.AddSeconds(1)), "New post-restore mutations are not blocked.");
        Assert((bool)Call(Main, "SaveTestCommandCreatedAtAllowed", "restoreSaveTest", fence.AddSeconds(-1)), "Recovery remains callable across the fence.");
    }

    private static void CheckCommandSchema()
    {
        foreach (var command in new[] { "startSaveTest", "loadSaveTestFixture", "setSaveTestStage", "restoreSaveTest" })
        {
            Session("running");
            Call(Main, "ValidateSaveTestCommand", command, Parse(Args(command))); Assertions++;
            foreach (var key in Args(command).Keys)
            {
                var missing = Args(command); missing.Remove(key);
                Reject(() => Call(Main, "ValidateSaveTestCommand", command, Parse(missing)), command + " missing " + key);
            }
            var extra = Args(command); extra["unexpected"] = true;
            Reject(() => Call(Main, "ValidateSaveTestCommand", command, Parse(extra)), command + " extra argument");
        }
        foreach (var value in new object[] { false, "true", 1 })
        {
            var args = Args("startSaveTest"); args["acceptNativeSideEffects"] = value;
            Reject(() => Call(Main, "ValidateSaveTestCommand", "startSaveTest", Parse(args)), "Explicit boolean side-effect acceptance");
        }
        foreach (var index in new[] { -2d, 1024d, .5d })
        {
            var fixture = Fixture(); fixture["stageIndex"] = index;
            Reject(() => Call(Main, "ValidateSaveTestFixture", Parse(fixture)), "Invalid pinned stage");
        }
        foreach (var plan in new[] { ".", "..", "../default", "default/other", "" })
        {
            var fixture = Fixture(); fixture["plan"] = plan;
            Reject(() => Call(Main, "ValidateSaveTestFixture", Parse(fixture)), "Unsafe plan name");
        }
        foreach (var path in new[] { "fixture.txt", "C:fixture.txt", "\\fixture.txt", "\\\\.\\PhysicalDrive0" })
        {
            var fixture = Fixture(); fixture["path"] = path;
            Reject(() => Call(Main, "ValidateSaveTestFixture", Parse(fixture)), "Fixture path must be fully qualified and non-device");
        }
        var slashPath = Fixture(); slashPath["path"] = ((string)slashPath["path"]).Replace('\\', '/');
        Call(Main, "ValidateSaveTestFixture", Parse(slashPath)); Assertions++;
    }

    private static void CheckGeneration()
    {
        Session("running");
        foreach (var command in new[] { "loadSaveTestFixture", "setSaveTestStage" })
        {
            var args = Args(command); args["expectedFixtureGeneration"] = "6";
            Reject(() => Call(Main, "ValidateSaveTestCommand", command, Parse(args)), "Old fixture generation");
            args = Args(command); args["sessionId"] = new string('c', 32);
            Reject(() => Call(Main, "ValidateSaveTestCommand", command, Parse(args)), "Different session");
        }
        var stage = Args("setSaveTestStage"); stage["fixtureSha256"] = new string('c', 64);
        Reject(() => Call(Main, "ValidateSaveTestCommand", "setSaveTestStage", Parse(stage)), "Different pinned fixture hash");
        var duplicateStart = new object[] { "startSaveTest", Parse(Args("startSaveTest")), null };
        Reject(() => Call(Owner, "ExecuteSaveTestCommand", duplicateStart), "Duplicate start during a running session");
        Assert((string)Runtime()["phase"] == "running", "Rejected duplicate start must not invalidate the healthy current fixture.");
    }

    private static object Identity(double total, double elapsed, string player = "player", long rebirth = 3, int version = 1260)
        => Activator.CreateInstance(Plugin.GetType("NGUInjector.SaveIdentity"), All, null,
            new object[] { "original.txt", new string('b', 64), total, rebirth, elapsed, version, 1, player }, null);

    private static void CheckLoadedIdentity()
    {
        var pinned = Identity(100, 10);
        Call(Main, "VerifySaveTestLoadedIdentity", pinned, Identity(200, 110)); Assertions++;
        foreach (var wrong in new[] { Identity(99, 10), Identity(100, 9), Identity(200, 20), Identity(200, 110, "foreign"),
            Identity(200, 110, rebirth: 4), Identity(200, 110, version: 1259), Identity(double.NaN, 0), Identity(double.PositiveInfinity, 0), Identity(1, -1) })
            Reject(() => Call(Main, "VerifySaveTestLoadedIdentity", pinned, wrong), "Regressed/foreign/invalid native progression");
    }

    private static void CheckImmutableCapture()
    {
        var path = Path.Combine(Root, "immutable", "original.txt"); var bytes = new byte[] { 1, 2, 3, 4 };
        Call(Main, "WriteSaveTestImmutable", path, bytes);
        Assert(File.ReadAllBytes(path).SequenceEqual(bytes), "Protective file bytes must read back exactly.");
        Assert((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0, "Protective file must be immutable.");
        Reject(() => Call(Main, "WriteSaveTestImmutable", path, new byte[] { 5 }), "Never replace an existing protected original");
        Assert(File.ReadAllBytes(path).SequenceEqual(bytes), "Failed protective overwrite must retain the original.");
    }

    private static void CheckStaticResets()
    {
        var allocator = Plugin.GetType("NGUInjector.Managers.CustomStageAllocator");
        var receipt = allocator.GetNestedType("TemporaryBloodAllocation", All);
        Set(allocator, "_temporaryBloodAllocation", FormatterServices.GetUninitializedObject(receipt));
        Call(allocator, "ResetResourceTargets");
        Assert(Get(allocator, "_temporaryBloodAllocation") != null, "Normal target reset must retain temporary blood cleanup evidence.");
        Call(allocator, "ResetAfterVerifiedSaveTestLoad");
        Assert(Get(allocator, "_temporaryBloodAllocation") == null, "Verified-load reset must clear obsolete blood allocation receipts.");
        var bars = Plugin.GetType("NGUInjector.Managers.BlackBarAllocator");
        ((IDictionary)Get(bars, "NguBufferedCaps")).Add("E0", 100L);
        Set(bars, "AppliedEnergyNguWeights", new[] { 1 }); Set(bars, "AppliedMagicNguWeights", new[] { 1 });
        Call(bars, "ResetAfterVerifiedSaveTestLoad");
        Assert(((IDictionary)Get(bars, "NguBufferedCaps")).Count == 0, "Verified-load reset must clear all buffered caps.");
        Assert(Get(bars, "AppliedEnergyNguWeights") == null && Get(bars, "AppliedMagicNguWeights") == null, "Verified-load reset must clear both applied weight vectors.");
    }

    private static void CheckRestoredIdempotence()
    {
        Session("restored");
        var call = new object[] { "restoreSaveTest", Parse(Args("restoreSaveTest")), null };
        var message = (string)Call(Owner, "ExecuteSaveTestCommand", call);
        Assert(message.Contains("already restored"), "Restore must be idempotent after verified readiness.");
        Assert((string)Runtime()["phase"] == "restored", "Idempotent restore must not change phase.");
    }

    private static string PrepareCancellationOwner(string name)
    {
        var runtime = Path.Combine(Root, name); Directory.CreateDirectory(runtime);
        File.WriteAllText(Path.Combine(runtime, "settings.json"), "{\"original\":true}");
        File.WriteAllText(Path.Combine(NativeRoot, "save.txt"), "untouched native");
        Owner = FormatterServices.GetUninitializedObject(Main);
        Set(Main, "reference", Owner); Set(Owner, "_started", true);
        Set(Main, "_dir", runtime); Set(Main, "Character", CreateCharacter()); Set(Main, "Settings", null);
        Set(Main, "_saveTestSession", null); Set(Main, "_saveTestOriginal", null); Set(Main, "_saveTestCharacter", null);
        Set(Main, "_saveTestDirectory", null); Set(Main, "_saveTestManifestHash", null); Set(Main, "_saveTestFenceUtc", default(DateTime));
        foreach (var field in new[] { "_saveTestBundleReady", "_saveTestBundleTrusted", "_saveTestNativeMutation", "_saveTestOriginalReleased",
            "_saveTestExpectedStartup", "_saveTestRestoringStartup", "_saveTestInternalRestart" }) Set(Main, field, false);
        return runtime;
    }

    private static object Execute(string command, Dictionary<string, object> args)
    {
        var call = new object[] { command, Parse(args), null };
        Call(Owner, "ExecuteSaveTestCommand", call);
        return call[2];
    }

    private static void CheckCancellationBeforeStart()
    {
        var runtime = PrepareCancellationOwner("cancel-before-start");
        var start = Args("startSaveTest");
        Reject(() => Execute("startSaveTest", start), "Unadmitted original preflight rejection");
        Assert((string)Runtime()["phase"] == "idle", "A knowingly rejected start must leave the original idle, not recoveryRequired.");
        var character = Get(Main, "Character");
        using (var lease = new FileStream(Path.Combine(runtime, ".injector.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            Set(Owner, "_injectorLease", lease);
            var data = Execute("restoreSaveTest", Args("restoreSaveTest"));
            var report = Json.Deserialize<Dictionary<string, object>>(data.ToString());
            var session = (Dictionary<string, object>)report["saveTestSession"];
            var restoration = (Dictionary<string, object>)session["restoration"];
            Assert((string)session["phase"] == "restored" && (string)session["sessionId"] == new string('a', 32), "Unknown first-ack outcome must acknowledge the client-known ID as cancelled/restored.");
            Assert((bool)restoration["verified"] && (bool)restoration["nativeVerified"] && (bool)restoration["filesVerified"]
                && (bool)restoration["cancelledBeforeStart"] && !(bool)restoration["nativeLoadPerformed"], "Cancellation must explicitly report no native rollback.");
            Assert(Json.Serialize(session["restoreTarget"]) == Json.Serialize(session["observed"]), "Cancellation must describe the current native identity, not a fictional restore baseline.");
            var path = Path.Combine(runtime, "save-safety", "tests", new string('a', 32), "cancelled.json");
            var marker = File.ReadAllBytes(path);
            Assert(marker.Length != 0 && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0, "Cancellation tombstone must be read-back verified and immutable before acknowledgement.");
            Assert(Directory.GetFileSystemEntries(Path.GetDirectoryName(path)).Length == 1, "Cancellation must not create a protected bundle or fixture files.");
            try { Execute("startSaveTest", start); throw new Exception("Cancelled queued start unexpectedly ran."); }
            catch (IOException error) { Assert(error.Message.Contains("already been used or cancelled"), "Queued start must fail at the directory/tombstone fence, before preflight or native decode."); }
            Assert((bool)Get(Owner, "_started") && !(bool)Get(Owner, "_unloaded") && ReferenceEquals(Get(Owner, "_injectorLease"), lease), "Cancelling an unadmitted ID must retain the running original owner and lease.");
            Assert(Get(Main, "Character") == character && File.ReadAllText(Path.Combine(runtime, "settings.json")) == "{\"original\":true}"
                && File.ReadAllText(Path.Combine(NativeRoot, "save.txt")) == "untouched native", "Cancellation/replayed start must not change native or original domain files.");
            Set(Get(character, "stats"), "rebirthNumber", 4L);
            Set(Get(character, "totalPlaytime"), "totalseconds", 220d); Set(Get(character, "rebirthTime"), "totalseconds", 5d);
            Execute("restoreSaveTest", Args("restoreSaveTest"));
            Assert(File.ReadAllBytes(path).SequenceEqual(marker), "Repeated cancellation must not overwrite its durable tombstone.");
            Assert((string)((Dictionary<string, object>)Runtime()["observed"])["rebirth"] == "4", "Idempotent cancellation must allow natural native rebirth progression without loading the old identity.");
            var journal = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(runtime, "save-safety", "test-session.json")));
            var saved = (Dictionary<string, object>)journal["session"];
            Assert((string)saved["phase"] == "restored" && (bool)((Dictionary<string, object>)saved["restoration"])["cancelledBeforeStart"], "Cold journal must distinguish explicit pre-start cancellation from native rollback.");
            Set(Main, "_saveTestSession", null);
            try { Execute("startSaveTest", start); throw new Exception("Uncached replay of cancelled start unexpectedly ran."); }
            catch (IOException error) { Assert(error.Message.Contains("already been used or cancelled"), "The durable directory must reject replay even without any in-memory session context."); }
            Assert((bool)Call(Owner, "InitializeSaveTestStartup"), "A durable cancellation journal must not hold an otherwise normal cold startup.");
            Assert((string)Runtime()["phase"] == "restored" && (bool)((Dictionary<string, object>)Runtime()["restoration"])["cancelledBeforeStart"], "Cold startup must retain explicit cancellation context.");
            try { Execute("startSaveTest", start); throw new Exception("Cold replay of cancelled start unexpectedly ran."); }
            catch (IOException error) { Assert(error.Message.Contains("already been used or cancelled"), "Cold replay must be rejected before decoding the still-missing fixture."); }
            Assert((long)Get(Get(character, "stats"), "rebirthNumber") == 4L && (double)Get(Get(character, "totalPlaytime"), "totalseconds") == 220d,
                "Cancellation must never regress native progression across repeat/cold requests.");
            Set(Owner, "_injectorLease", null);
        }
        Set(Main, "reference", null);
    }

    private static void CheckCancellationRejectsOtherActiveSession()
    {
        var runtime = PrepareCancellationOwner("cancel-other-active"); Session("running");
        var before = Call(Main, "GetSaveTestRuntime").ToString();
        var args = Args("restoreSaveTest"); args["sessionId"] = new string('c', 32);
        Reject(() => Execute("restoreSaveTest", args), "A different active session cannot be cancelled");
        Assert(Call(Main, "GetSaveTestRuntime").ToString() == before, "Mismatched restore must not invalidate or rewrite another active session.");
        Assert(!Directory.Exists(Path.Combine(runtime, "save-safety")), "Mismatched cancellation must not write a tombstone or journal.");
        Set(Main, "reference", null);
    }

    private static void CheckCancellationProtectsExistingBundle()
    {
        var runtime = PrepareCancellationOwner("cancel-existing-bundle");
        var directory = Path.Combine(runtime, "save-safety", "tests", new string('a', 32));
        Call(Bundle, "Capture", runtime, NativeRoot, directory);
        var manifest = Path.Combine(directory, "bundle", "manifest.json"); var original = File.ReadAllBytes(manifest);
        Reject(() => Execute("restoreSaveTest", Args("restoreSaveTest")), "Unknown existing protected bundle requires review, not cancellation");
        Assert(File.ReadAllBytes(manifest).SequenceEqual(original) && !File.Exists(Path.Combine(directory, "cancelled.json")), "Cancellation must never overwrite or disguise protected originals.");
        Assert((string)Runtime()["phase"] == "idle" && !File.Exists(Path.Combine(runtime, "save-safety", "test-session.json")), "Rejected cancellation of an unknown bundle must leave normal state and journal untouched.");
        Session("restored");
        var different = Args("restoreSaveTest"); different["sessionId"] = new string('d', 32);
        Execute("restoreSaveTest", different);
        Assert((string)Runtime()["sessionId"] == new string('d', 32) && (bool)((Dictionary<string, object>)Runtime()["restoration"])["cancelledBeforeStart"],
            "An inactive previous session must not prevent cancellation of a fresh different ID.");
        Assert(File.ReadAllBytes(manifest).SequenceEqual(original), "Cancelling a fresh ID must leave a different protected bundle untouched.");
        Set(Main, "reference", null);
    }

    private static void CheckOriginalFightPreflight()
    {
        var runtime = PrepareCancellationOwner("original-fight-preflight"); var character = Get(Main, "Character");
        var controllerField = character.GetType().GetField("inventoryController", All);
        Set(Main, "Controller", FormatterServices.GetUninitializedObject(controllerField.FieldType));
        var bossField = character.GetType().GetField("bossController", All);
        var boss = FormatterServices.GetUninitializedObject(bossField.FieldType); bossField.SetValue(character, boss); Set(boss, "isFighting", true);
        using (var lease = new FileStream(Path.Combine(runtime, ".injector.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            Set(Owner, "_injectorLease", lease);
            try { Execute("startSaveTest", Args("startSaveTest")); throw new Exception("Original native fight unexpectedly admitted."); }
            catch (InvalidOperationException error) { Assert(error.Message.Contains("original native boss fight/nuke"), "Original fight must be rejected clearly before quiesce or capture."); }
            Assert((string)Runtime()["phase"] == "idle" && !Directory.Exists(Path.Combine(runtime, "save-safety")), "Original fight preflight rejection must not create recovery intent or half-quiesce.");
            Execute("restoreSaveTest", Args("restoreSaveTest"));
            Assert((bool)Get(boss, "isFighting") && (bool)Get(Owner, "_started") && ReferenceEquals(Get(Owner, "_injectorLease"), lease), "Cancelling a rejected test must not cancel original native fighting or unload its owner.");
            Set(boss, "isFighting", false); Set(boss, "nukeBoss", true);
            var nukeStart = Args("startSaveTest"); nukeStart["sessionId"] = new string('e', 32);
            try { Execute("startSaveTest", nukeStart); throw new Exception("Original native nuke unexpectedly admitted."); }
            catch (InvalidOperationException error) { Assert(error.Message.Contains("original native boss fight/nuke"), "Original nuke must also reject before original-owner quiesce."); }
            var nukeCancel = Args("restoreSaveTest"); nukeCancel["sessionId"] = new string('e', 32);
            Execute("restoreSaveTest", nukeCancel);
            Assert((bool)Get(boss, "nukeBoss") && (bool)Get(Owner, "_started"), "Cancelling an unadmitted ID must preserve the original native nuke flag.");
            Set(Owner, "_injectorLease", null);
        }
        Set(Main, "Controller", null); Set(Main, "reference", null);
    }

    private static void PrepareBundle(string name)
    {
        var runtime = Path.Combine(Root, name); Directory.CreateDirectory(runtime);
        Directory.CreateDirectory(Path.Combine(runtime, "custom-plans"));
        File.WriteAllText(Path.Combine(runtime, "settings.json"), "{\"original\":true}");
        File.WriteAllText(Path.Combine(runtime, "stage-progress-original.json"), "original stage");
        File.WriteAllText(Path.Combine(NativeRoot, "save.txt"), "original native");
        var directory = Path.Combine(runtime, "save-safety", "tests", new string('a', 32));
        Call(Bundle, "Capture", runtime, NativeRoot, directory);
        File.WriteAllText(Path.Combine(runtime, "settings.json"), "fixture settings");
        File.WriteAllText(Path.Combine(runtime, "stage-progress-fixture.json"), "fixture stage");
        File.WriteAllText(Path.Combine(NativeRoot, "save.txt"), "fixture native");
        Set(Main, "_dir", runtime); Set(Main, "_saveTestDirectory", directory);
        Set(Main, "_saveTestBundleReady", true); Set(Main, "_saveTestBundleTrusted", true);
        Set(Main, "_saveTestRestoringStartup", true); Set(Main, "_saveTestOriginal", null);
        Session("restoring");
        Set(Main, "Character", CreateCharacter());
    }

    private static object CreateCharacter()
    {
        var character = FormatterServices.GetUninitializedObject(Game.GetType("Character"));
        foreach (var field in new[] { "stats", "totalPlaytime", "rebirthTime" })
        {
            var info = character.GetType().GetField(field, All);
            info.SetValue(character, FormatterServices.GetUninitializedObject(info.FieldType));
        }
        Set(Get(character, "stats"), "rebirthNumber", 3L);
        Set(Get(character, "totalPlaytime"), "totalseconds", 200d); Set(Get(character, "rebirthTime"), "totalseconds", 110d);
        Set(character, "playerName", "player");
        return character;
    }

    private static void CheckReadyRestoration()
    {
        PrepareBundle("ready"); Call(Owner, "CompleteSaveTestStartup");
        var runtime = (string)Get(Main, "_dir");
        Assert(File.ReadAllText(Path.Combine(runtime, "settings.json")) == "{\"original\":true}", "Fresh startup normalization must not replace protected settings.");
        Assert(File.ReadAllText(Path.Combine(NativeRoot, "save.txt")) == "original native", "Fresh readiness must verify native .txt originals too.");
        Assert(!File.Exists(Path.Combine(runtime, "stage-progress-fixture.json")), "Disposable fixture checkpoint must be archived/removed.");
        Assert((string)Runtime()["phase"] == "restored", "Only verified fresh readiness may publish restored.");
        var restoration = (Dictionary<string, object>)Runtime()["restoration"];
        Assert((bool)restoration["verified"] && (bool)restoration["filesVerified"] && (bool)restoration["nativeVerified"], "All restoration evidence must be true at readiness.");
        Assert(Directory.GetFiles(Path.Combine((string)Get(Main, "_saveTestDirectory"), "test-outputs"), "*", SearchOption.AllDirectories).Length >= 4,
            "Fixture/startup outputs must be retained in the bundle archive.");
        Assert(File.Exists(Path.Combine(runtime, "save-safety", "test-session.json")), "Restored readiness must be durable.");
    }

    private static void CheckFailedReadyRestoration()
    {
        PrepareBundle("failed-ready");
        var manifest = Path.Combine((string)Get(Main, "_saveTestDirectory"), "bundle", "manifest.json");
        File.SetAttributes(manifest, FileAttributes.Normal); File.AppendAllText(manifest, " ");
        Reject(() => Call(Owner, "CompleteSaveTestStartup"), "Changed protected bundle");
        Assert((string)Runtime()["phase"] != "restored", "A failed bundle verification must never publish restored.");
    }

    private static void CheckArchiveWarningsSurviveReadiness()
    {
        PrepareBundle("restore-archive-warnings");
        var directory = (string)Get(Main, "_saveTestDirectory");
        var obstruction = Path.Combine(directory, "test-outputs"); File.WriteAllText(obstruction, "archive unavailable");
        var first = Call(Bundle, "Restore", directory, true);
        var initial = Json.Deserialize<Dictionary<string, object>>(first.ToString());
        Assert((bool)initial["verified"] && !(bool)initial["archiveComplete"], "Real archive I/O failure must still permit protected-original restoration.");
        Assert(((IEnumerable)initial["archiveErrors"]).Cast<object>().Count() > 0 && ((IEnumerable)initial["filesLost"]).Cast<object>().Count() > 0, "The first restore must produce concrete archive errors and lost fixture-output records.");
        Call(Main, "RecordSaveTestRestoration", first);
        File.Delete(obstruction);
        File.WriteAllText(Path.Combine((string)Get(Main, "_dir"), "settings.json"), "fresh startup normalization");
        Call(Owner, "CompleteSaveTestStartup");
        var final = (Dictionary<string, object>)Runtime()["restoration"];
        Assert((string)Runtime()["phase"] == "restored" && (bool)final["verified"] && !(bool)final["archiveComplete"], "A successful readiness pass must retain prior archive-warning status.");
        Assert(Json.Serialize(final["archiveErrors"]) == Json.Serialize(initial["archiveErrors"]), "Successful second restore must not erase first-pass archive errors.");
        Assert(Json.Serialize(final["filesLost"]) == Json.Serialize(initial["filesLost"]), "Successful second restore must not erase lost fixture-output evidence.");
        var journal = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine((string)Get(Main, "_dir"), "save-safety", "test-session.json")));
        var durable = (Dictionary<string, object>)((Dictionary<string, object>)journal["session"])["restoration"];
        Assert(Json.Serialize(durable["archiveErrors"]) == Json.Serialize(initial["archiveErrors"]), "Retained archive warnings must also survive in the durable restored journal.");
    }

    private static void CheckColdRecoveryHold()
    {
        Session("running"); Set(Main, "_saveTestManifestHash", new string('c', 64));
        Set(Main, "_saveTestNativeMutation", true); Set(Main, "_saveTestOriginalReleased", true);
        Call(Main, "PersistSaveTestJournal");
        Set(Main, "_saveTestSession", null); Set(Main, "_saveTestBundleTrusted", false); Set(Main, "_saveTestExpectedStartup", false);
        Owner = FormatterServices.GetUninitializedObject(Main);
        var host = FormatterServices.GetUninitializedObject(Plugin.GetType("NGUInjector.SaveTestHost")); Set(Loader, "_saveTestHost", host);
        var leasePath = Path.Combine(Root, "cold-injector.lock");
        using (var lease = new FileStream(leasePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            Set(Owner, "_injectorLease", lease);
            Assert(!(bool)Call(Owner, "InitializeSaveTestStartup"), "Interrupted test must hold before domain loading/writes.");
            Assert((string)Runtime()["phase"] == "recoveryRequired", "Unknown startup must never replay a fixture.");
            Assert(ReferenceEquals(Get(host, "Lease"), lease) && Get(Owner, "_injectorLease") == null, "Cold recovery host must retain the uninterrupted startup lease.");
            Assert((bool)Call(Owner, "IsSaveTestRecoveryAdapter", "status"), "Status must remain admitted during held startup.");
            Assert((bool)Call(Owner, "IsSaveTestRecoveryAdapter", "restoreSaveTest"), "Explicit restore must remain admitted during held startup.");
            Assert(!(bool)Call(Owner, "IsSaveTestRecoveryAdapter", "applySettingsPatch"), "Recovery adapter must not become a general command executor.");
            var directory = (string)Get(Main, "_dir");
            Assert(File.ReadAllText(Path.Combine(directory, "settings.json")) == "fixture settings", "Held startup must not normalize or replay fixture settings.");
            var restore = new object[] { "restoreSaveTest", Parse(Args("restoreSaveTest")), null };
            Reject(() => Call(Owner, "ExecuteSaveTestCommand", restore), "Unavailable/unverified disk bundle must fail closed");
            Assert((string)Runtime()["phase"] == "recoveryRequired" && ReferenceEquals(Get(host, "Lease"), lease), "Failed explicit restore must retain recovery and its lease for retry.");
            var journal = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(directory, "save-safety", "test-session.json")));
            Assert((string)((Dictionary<string, object>)journal["session"])["phase"] == "recoveryRequired", "A failed explicit restore must durably retire its restoring phase to recoveryRequired.");
            Set(host, "Lease", null);
        }
    }

    private sealed class LeaseWriter : StreamWriter
    {
        internal bool DisposedWithLease;
        private readonly string lease;
        internal LeaseWriter(string lease) : base(new MemoryStream()) { this.lease = lease; }
        protected override void Dispose(bool disposing)
        {
            try { using (new FileStream(lease, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
            catch (IOException) { DisposedWithLease = true; }
            base.Dispose(disposing);
        }
    }

    private static void CheckLeaseHandover()
    {
        Session("recoveryRequired");
        var hostType = Plugin.GetType("NGUInjector.SaveTestHost");
        var host = FormatterServices.GetUninitializedObject(hostType); Set(Loader, "_saveTestHost", host);
        Owner = FormatterServices.GetUninitializedObject(Main); Set(Main, "reference", Owner);
        Set(Main, "_dir", "");
        var character = Get(Main, "Character"); var bossField = character.GetType().GetField("bossController", All);
        var boss = FormatterServices.GetUninitializedObject(bossField.FieldType); bossField.SetValue(character, boss); Set(boss, "isFighting", true);
        var leasePath = Path.Combine(Root, "injector.lock");
        using (var lease = new FileStream(leasePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            Set(Owner, "_injectorLease", lease); var writer = new LeaseWriter(leasePath); Set(Main, "OutputWriter", writer);
            Reject(() => Call(Owner, "UnloadForSaveTest", false), "Original must retain strict native quiesce");
            Assert(!(bool)Get(Owner, "_unloaded") && !writer.DisposedWithLease, "Refused original quiesce must retain its owner, writers and lease.");
            Call(Owner, "UnloadForSaveTest", true);
            Assert(writer.DisposedWithLease, "Old writers must finish disposal before lease handover.");
            Assert(ReferenceEquals(Get(host, "Lease"), lease), "Recovery host must retain the same uninterrupted lock lease.");
            Assert((bool)Get(Owner, "_unloaded") && Get(Main, "reference") == null, "Disposable fixture must stop despite unresolved native fighting.");
            var adopted = Call(Loader, "AdoptSaveTestLease");
            Assert(ReferenceEquals(adopted, lease) && Get(host, "Lease") == null, "Fresh Main must adopt, not reacquire, the same lease.");
        }
    }

    private sealed class FailingWriter : StreamWriter
    {
        internal FailingWriter() : base(new MemoryStream()) { }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); throw new IOException("Test writer-disposal failure"); }
    }

    private static void CheckLoaderCleanupFailure()
    {
        PrepareBundle("cleanup-failure");
        var character = Get(Main, "Character");
        var bossField = character.GetType().GetField("bossController", All);
        bossField.SetValue(character, FormatterServices.GetUninitializedObject(bossField.FieldType));
        Owner = FormatterServices.GetUninitializedObject(Main); Set(Main, "reference", Owner); Set(Main, "_dir", "");
        var host = FormatterServices.GetUninitializedObject(Plugin.GetType("NGUInjector.SaveTestHost")); Set(Loader, "_saveTestHost", host);
        Set(Loader, "_load", FormatterServices.GetUninitializedObject(Core.GetType("UnityEngine.GameObject")));
        var leasePath = Path.Combine(Root, "cleanup-injector.lock");
        using (var lease = new FileStream(leasePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            Set(Owner, "_injectorLease", lease); Set(Main, "OutputWriter", new FailingWriter());
            Reject(() => Call(Loader, "ReplaceSaveTestMain", Owner, false), "Original cleanup failure must refuse fixture preparation");
            Assert((bool)Get(Owner, "_unloaded"), "Cleanup failure after quiesce must still stop the old owner.");
            Assert(Get(Loader, "_load") == null && Get(Main, "reference") == null, "A stopped failed-cleanup object must not block recovery Init.");
            Assert(ReferenceEquals(Get(host, "Lease"), lease), "Cleanup failure must retain the lease in the recovery host.");
            Set(host, "Lease", null);
        }
    }

    private static object Get(object target, string name) => (target as Type ?? target.GetType()).GetField(name, All).GetValue(target is Type ? null : target);
    private static void Set(object target, string name, object value) => (target as Type ?? target.GetType()).GetField(name, All).SetValue(target is Type ? null : target, value);
    private static object Call(object target, string name, params object[] args)
    {
        try { return (target as Type ?? target.GetType()).InvokeMember(name, All | BindingFlags.InvokeMethod, null, target is Type ? null : target, args); }
        catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw(); throw; }
    }
}

internal static class SessionChecksEntry
{
    public static int Main(string[] args) => SessionChecks.MainEntry(args);
}
