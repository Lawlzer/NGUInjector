using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class PresetIntegrationChecks
{
    private static int SettingsJsonReads => NativeSettingsJsonBoundary.Reads;

    private static void ConfigureSettingsJsonBoundary(AssemblyDefinition plugin)
    {
        NativeSettingsJsonBoundary.Configure(plugin);
        var write = plugin.MainModule.Types.Single(type => type.FullName == "NGUInjector.JsonFiles")
            .Methods.Single(method => method.Name == "WriteAtomic");
        var entry = write.Body.Instructions[0]; var il = write.Body.GetILProcessor();
        il.InsertBefore(entry, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(entry, Instruction.Create(OpCodes.Call, plugin.MainModule.ImportReference(
            typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveSettingsFileWrite), All))));
    }

    public static string SettingsToJson(object value) { return SettingsToJson(value, false); }
    public static string SettingsToJson(object value, bool pretty) => NativeSettingsJsonBoundary.ToJson(value, pretty);
    public static T SettingsFromJson<T>(string text) => NativeSettingsJsonBoundary.FromJson<T>(text);

    public static void ObserveSettingsFileWrite(string path)
    {
        if (MainType != null && Get(MainType, "Settings") is object settings && Equals(path, Get(settings, "_savePath")))
            PersistPresetSettings();
    }

    private static void CheckSettingsJsonBoundary()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport();
        var settings = Get(MainType, "Settings"); var type = settings.GetType();
        var fromJson = typeof(PresetIntegrationChecks).GetMethod(nameof(SettingsFromJson), All).MakeGenericMethod(type);
        var empty = fromJson.Invoke(null, new object[] { "{}" });
        Equal(false, Get(empty, "_autoRebirth"), "Unity boundary zeroes a missing initialized bool");
        Equal(0, Get(empty, "_customAdventureMaxItopodDeaths"), "Unity boundary zeroes a missing initialized int");
        Equal(null, Get(empty, "_energyNguWeights"), "Unity boundary zeroes a missing initialized array");
        var path = Path.Combine((string)Get(MainType, "_dir"), "json-boundary-settings.json");
        Set(settings, "_savePath", path); AllowPresetSettingsSave = true;
        Set(settings, "_sourceJson", JsonParse("{\"future\":{\"value\":\"retained\"}}"));
        var weights = ((int[])Property(settings, "EnergyNguWeights")).ToArray();
        var writes = PresetSettingsWrites; var reads = SettingsJsonReads;
        type.GetProperty("AutoRebirth").SetValue(settings, false);
        Equal(writes + 1, PresetSettingsWrites, "real ApplyPatch atomic write retains existing persistence recording");
        Check(SettingsJsonReads > reads, "transactional setter executes the managed FromJson boundary");
        Equal(false, Property(settings, "AutoRebirth"), "actual ApplyPatch publishes its validated candidate");
        var saved = File.ReadAllText(path); var root = JsonParse(saved);
        Equal("false", Json(root, "_autoRebirth").ToString(), "actual atomic persistence contains the new permission");
        Equal(saved, Get(settings, "_lastAppliedText"), "actual publication retains persisted settings text");
        Equal(root.ToString(), Get(settings, "_sourceJson").ToString(), "actual publication retains persisted source JSON");
        Equal("\"retained\"", Json(Json(root, "future"), "value").ToString(), "actual settings merge preserves unknown fields");
        Check(weights.SequenceEqual((int[])Property(settings, "EnergyNguWeights")), "serializer boundary preserves typed global weight arrays");
        Check(!root.ToString().Contains("_savePath"), "Unity serializer excludes private unmarked runtime fields");
        writes = PresetSettingsWrites;
        Throws(() => Invoke(settings, "ApplyPatch", JsonParse("{\"_energyNguWeights\":[-1,0,0,0,0,0,0,0,0]}")),
            "nonnegative", "actual ApplyPatch validation rejects invalid ratios before saving");
        Equal(writes, PresetSettingsWrites, "invalid patch never reaches the save recorder");
        Equal(saved, File.ReadAllText(path), "invalid patch preserves real persisted bytes");
        Equal(false, Property(settings, "AutoRebirth"), "invalid patch preserves live permissions");
        // Keep the real CAS path/revision unchanged. Deny only atomic replacement
        // after validation, rather than redirecting to a different settings file.
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Throws(() => type.GetProperty("AutoRebirth").SetValue(settings, true), "IOException", "actual atomic write failure propagates");
            Equal(false, Property(settings, "AutoRebirth"), "failed persistence cannot publish the candidate permission");
            Equal(saved, Get(settings, "_lastAppliedText"), "failed persistence preserves published settings text");
            Equal(root.ToString(), Get(settings, "_sourceJson").ToString(), "failed persistence preserves published source JSON");
        }
        var reloaded = Activator.CreateInstance(type, new object[] { null });
        Set(reloaded, "_savePath", path);
        reads = SettingsJsonReads;
        Equal(true, Invoke(reloaded, "LoadSettings"), "fresh settings reload executes without Unity internal calls");
        Check(SettingsJsonReads > reads, "actual reload reaches the managed generic FromJson boundary");
        Equal(false, Property(reloaded, "AutoRebirth"), "actual reload retains the persisted permission");
        Equal(saved, Get(reloaded, "_lastAppliedText"), "fresh reload retains the exact published settings bytes");
        Check(SettingsToJson(settings, true).Contains("_autoRebirth"), "pretty overload remains inside the serializer boundary");
        writes = PresetSettingsWrites;
        Invoke(settings, "SaveSettings");
        Equal(writes + 1, PresetSettingsWrites, "legacy SaveSettings recorder remains available independently of ApplyPatch");
    }
}

public static partial class Program
{
    private static void ConfigureSettingsJsonFixture(AssemblyDefinition plugin) => NativeSettingsJsonBoundary.Configure(plugin);
}
