using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SimpleJSON;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static int RunNativeSettingsPersistenceOnly(string property)
        {
            _dir = Path.Combine(Path.GetTempPath(), "opencode", "ngu-native-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            try
            {
                CheckNativeSettingsPersistence(property);
                Console.WriteLine("PASS: native settings persistence: " + _assertions + " assertions.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { Directory.Delete(_dir, true); }
        }

        private static void CheckNativeSettingsPersistence(string onlyProperty = null)
        {
            var previousSettings = NGUInjector.Main.Settings;
            var previousMain = NGUInjector.Main.reference;
            NGUInjector.Main.reference = previousMain ?? new NGUInjector.Main();
            try
            {
                var cases = new[] {
                    (Property: "UseCustomRebirthPlan", Field: "_useCustomRebirthPlan", Invalidates: true),
                    (Property: "AutoRebirth", Field: "_autoRebirth", Invalidates: true),
                    (Property: "CustomPlanFile", Field: "_customPlanFile", Invalidates: true),
                    (Property: "GlobalEnabled", Field: "_globalEnabled", Invalidates: false),
                    (Property: "CombatEnabled", Field: "_combatEnabled", Invalidates: false)
                }.Where(item => onlyProperty == null || item.Property == onlyProperty).ToArray();
                Assert(cases.Length > 0, "native settings selector names a tested property");
                foreach (var item in cases)
                foreach (var reverse in new[] {false, true})
                {
                    var property = typeof(SavedSettings).GetProperty(item.Property);
                    object initial = item.Property == "CustomPlanFile" ? (object) (reverse ? "after" : "before") : reverse;
                    object next = item.Property == "CustomPlanFile" ? (object) (reverse ? "before" : "after") : !reverse;
                    var directory = Path.Combine(_dir, "native-" + item.Property + "-" + reverse);
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "settings.json");
                    var settings = new SavedSettings(directory);
                    NGUInjector.Main.Settings = settings;
                    settings.SetSaveDisabled(true);
                    property.SetValue(settings, initial);
                    settings.SetSaveDisabled(false);
                    settings.SaveSettings();
                    var root = JsonFiles.ParseObject(File.ReadAllText(path));
                    root["Future"] = JsonFiles.ParseObject("{\"integer\":9007199254740993,\"decimal\":1.2300e+004}");
                    File.WriteAllText(path, root.ToString(2));
                    Assert(settings.LoadSettings(), "native settings fixture reloads: " + item.Property);
                    settings.QuickLoadout = new[] {40, 41};
                    var before = File.ReadAllBytes(path);
                    var invalidations = NGUInjector.Main.Invalidations;
                    using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var rejected = false;
                        try { property.SetValue(settings, next); }
                        catch (TargetInvocationException error) when (error.InnerException is IOException) { rejected = true; }
                        Assert(rejected, "blocked native write reports IO failure: " + item.Property);
                        Assert(Equals(property.GetValue(settings), initial), "failed native write retains live value: " + item.Property);
                        Assert(before.SequenceEqual(File.ReadAllBytes(path)), "failed native write retains disk bytes: " + item.Property);
                        Assert(NGUInjector.Main.Invalidations == invalidations, "failed native write does not notify: " + item.Property);
                        Assert(!settings.HasSettingsFileChanged(), "failed native write retains watcher baseline: " + item.Property);
                    }
                    property.SetValue(settings, next);
                    Assert(Equals(property.GetValue(settings), next), "same requested value can retry successfully: " + item.Property);
                    var saved = JsonFiles.ParseObject(File.ReadAllText(path));
                    Assert(next is bool flag ? saved[item.Field].IsBoolean && saved[item.Field].AsBool == flag
                        : saved[item.Field].IsString && saved[item.Field].Value == (string) next, "retry persists requested field: " + item.Property);
                    Assert(saved["Future"].ToString() == root["Future"].ToString(), "native commit preserves exact unknown numbers: " + item.Property);
                    Assert(saved["_quickLoadout"].ToString() == "[40,41]", "native commit retains unsaved live settings: " + item.Property);
                    Assert(NGUInjector.Main.Invalidations == invalidations + (item.Invalidates ? 1 : 0), "successful native commit notifies exactly once when required: " + item.Property);
                    Assert(!settings.HasSettingsFileChanged(), "successful native commit advances watcher baseline: " + item.Property);
                    before = File.ReadAllBytes(path);
                    using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        property.SetValue(settings, next);
                    Assert(before.SequenceEqual(File.ReadAllBytes(path)), "same-value native assignment performs no write: " + item.Property);
                    Assert(NGUInjector.Main.Invalidations == invalidations + (item.Invalidates ? 1 : 0), "same-value native assignment does not notify: " + item.Property);
                    Assert(Directory.GetFiles(directory, "*.tmp").Length == 0, "native write cleanup leaves no temporary files: " + item.Property);

                    var transient = new SavedSettings(null);
                    NGUInjector.Main.Settings = transient;
                    property.SetValue(transient, next);
                    Assert(Equals(property.GetValue(transient), next), "null-path native assignment remains available: " + item.Property);
                    var disabled = new SavedSettings(directory);
                    NGUInjector.Main.Settings = disabled;
                    disabled.SetSaveDisabled(true);
                    property.SetValue(disabled, next);
                    Assert(Equals(property.GetValue(disabled), next) && before.SequenceEqual(File.ReadAllBytes(path)), "save-disabled initialization changes memory only: " + item.Property);
                }
            }
            finally
            {
                NGUInjector.Main.Settings = previousSettings;
                NGUInjector.Main.reference = previousMain;
            }
        }
    }
}
