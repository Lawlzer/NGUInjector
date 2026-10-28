// Only unavailable game/Unity IO and lifecycle entry points are substituted.
// Save safety, metadata decoding, JSON, and filesystem operations are source-linked.
using System;
using System.Collections.Generic;
using SimpleJSON;

internal sealed class Character
{
    internal sealed class Stats { internal long rebirthNumber; }
    internal sealed class PlayerTime { internal double totalseconds; }

    internal Stats stats = new Stats();
    internal PlayerTime totalPlaytime = new PlayerTime();
    internal PlayerTime rebirthTime = new PlayerTime();
    internal string playerName;
    internal int lastTime;
    internal readonly ImportExport importExport = new ImportExport();
    internal readonly SaveLoad saveLoad = new SaveLoad();
    internal int getVersion() { return 1260; }
}

internal sealed class NativeSaveData
{
    internal readonly string Encoded;
    internal NativeSaveData(string encoded) { Encoded = encoded; }
}

internal sealed class ImportExport
{
    internal Func<string> Export;
    internal Func<string, NativeSaveData> Decode = encoded => new NativeSaveData(encoded);
    internal int ExportCalls, DecodeCalls;
    internal string LastDecoded;

    internal string getBase64Data()
    {
        ExportCalls++;
        return Export();
    }

    internal NativeSaveData getSaveDataFromString(string encoded)
    {
        DecodeCalls++;
        LastDecoded = encoded;
        return Decode(encoded);
    }
}

internal sealed class SaveLoad
{
    internal Action<NativeSaveData> Load = data => { throw new InvalidOperationException("Native load boundary was not configured."); };
    internal int LoadCalls, CloudCalls;

    internal void loadintoGame(NativeSaveData data)
    {
        LoadCalls++;
        Load(data);
    }

    internal void saveGamestateToSteamCloud()
    {
        CloudCalls++;
        throw new InvalidOperationException("Bridge checks must never publish to cloud.");
    }
}

internal static class Epoch
{
    internal const int Now = 1720000000;
    internal static int Current() { return Now; }
}

namespace UnityEngine
{
    internal static class Application
    {
        internal static string persistentDataPath;
    }
}

namespace NGUInjector
{
    internal partial class Main
    {
        internal static bool SaveTestActive => false;
        internal static bool SaveTestCaptureAllowed => true;
        internal static JSONObject GetSaveTestRuntime() => new JSONObject();
        internal static Main reference;
        internal enum ExecutionLifecycle { Running, Quiescing, LoadingSave }
        internal static Character Character;
        private readonly string _dir;
        private bool _protectedTimingLoadingSave;
        private readonly DateTime ExternalUiStartedAt = new DateTime(2026, 10, 23, 12, 34, 56, DateTimeKind.Utc);

        internal ExecutionLifecycle Lifecycle = ExecutionLifecycle.Running;
        internal readonly List<string> LifecycleEvents = new List<string>();
        internal Action<string> OnLifecycle = value => { };
        internal int RuntimeResets;

        internal Main(string directory, Character character)
        {
            _dir = directory;
            Character = character;
            InitializeSaveRecovery();
        }

        internal bool LoadingSave { get { return _protectedTimingLoadingSave; } }
        internal string StartedAt { get { return ExternalUiStartedAt.ToString("o"); } }
        internal JSONObject Runtime() { return GetSaveSafetyRuntime(); }
        internal void Tick() { PumpSaveSafety(); }
        internal void Scan(bool force) { BeginSaveScan(force); }
        internal SaveIdentity Capture() { return CaptureVerifiedLocalSave(); }
        internal void RequirePublication() { RequireSafeCloudSave(); }
        internal string Command(string command, JSONNode args, out JSONNode data)
        {
            return ExecuteSaveSafetyCommand(command, args, out data);
        }

        private void QuiesceExecution(string reason)
        {
            Lifecycle = ExecutionLifecycle.Quiescing;
            LifecycleEvents.Add("quiesce");
            OnLifecycle("quiesce");
        }

        private void SetExecutionLifecycle(ExecutionLifecycle lifecycle)
        {
            Lifecycle = lifecycle;
            var value = "lifecycle:" + lifecycle;
            LifecycleEvents.Add(value);
            OnLifecycle(value);
        }

        private void ResetCustomAutomationRuntime()
        {
            RuntimeResets++;
            LifecycleEvents.Add("reset");
            OnLifecycle("reset");
        }
    }
}
