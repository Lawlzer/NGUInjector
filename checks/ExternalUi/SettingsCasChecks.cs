using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SimpleJSON;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static int RunSettingsCasOnly()
        {
            _dir = Path.Combine(Path.GetTempPath(), "opencode", "ngu-settings-cas-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            NGUInjector.Main main = null;
            var previousExternalUi = Environment.GetEnvironmentVariable("NGUINJECTOR_EXTERNAL_UI");
            try
            {
                main = new NGUInjector.Main(); main.Initialize(_dir, false); main.SetReady(true);
                Assert(main.BuildRuntime()["settingsCasVersion"].AsInt == 1, "connected runtime advertises revision-enforcing settings support");
                var settings = new SavedSettings(_dir); NGUInjector.Main.Settings = settings;
                settings.SaveSettings();
                var path = Path.Combine(_dir, "settings.json");
                var revision = JsonFiles.ComputeRevision(File.ReadAllBytes(path));
                settings.MoneyPitThreshold = 12;
                var before = File.ReadAllBytes(path);
                var args = JsonFiles.ParseObject("{\"patch\":{\"_moneyPitThreshold\":34}}");
                args["expectedRevision"] = revision;
                main.Process("stale-settings", Envelope("stale-settings", "applySettingsPatch", args));
                Assert(!Result("stale-settings")["success"].AsBool, "stale connected settings revision is rejected");
                Assert(settings.MoneyPitThreshold == 12 && before.SequenceEqual(File.ReadAllBytes(path)), "stale request changes neither live settings nor disk");
                var disk = JsonFiles.ParseObject(File.ReadAllText(path)); disk["_moneyPitThreshold"] = 56;
                File.WriteAllText(path, disk.ToString());
                revision = JsonFiles.ComputeRevision(File.ReadAllBytes(path));
                Reject(() => settings.ApplyExternalPatch(args["patch"], revision), "fresh disk revision cannot bypass stale loaded baseline");
                Reject(() => settings.MoneyPitThreshold = 78, "native setter cannot overwrite changed disk");
                Assert(settings.MoneyPitThreshold == 12, "rejected native setter does not publish");
                Assert(settings.LoadSettings() && settings.MoneyPitThreshold == 56, "explicit reload accepts changed disk");
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Reject(() => settings.MoneyPitThreshold = 78, "failed native persistence reports failure");
                    Assert(settings.MoneyPitThreshold == 56, "failed setter retains old live value");
                }
                settings.MoneyPitThreshold = 78;
                Assert(JsonFiles.ParseObject(File.ReadAllText(path))["_moneyPitThreshold"].AsDouble == 78, "same-value retry after failed persistence commits");
                settings.QuickLoadout = new[] {40};
                Reject(() => settings.ApplyExternalPatch(args["patch"], JsonFiles.ComputeRevision(File.ReadAllBytes(path))), "external edit refuses intentional unsaved native state");
                Assert(settings.LoadSettings(), "explicit same-byte reload discards unsaved state");
                settings.TitanMoneyDone = new bool[12];
                settings.TitanMoneyDone[0] = true;
                Reject(() => settings.ApplyExternalPatch(args["patch"], JsonFiles.ComputeRevision(File.ReadAllBytes(path))), "external edit refuses in-place native array mutation");
                settings.SaveSettings();
                var result = settings.ApplyExternalPatch(args["patch"], JsonFiles.ComputeRevision(File.ReadAllBytes(path)));
                Assert(result["revision"].Value == JsonFiles.ComputeRevision(File.ReadAllBytes(path)) && result["settings"]["_moneyPitThreshold"].AsDouble == 34, "commit returns exact revision and settings snapshot");
                var withoutBom = File.ReadAllBytes(path);
                File.WriteAllBytes(path, new byte[] {0xef, 0xbb, 0xbf}.Concat(withoutBom).ToArray());
                Assert(settings.HasSettingsFileChanged(), "same JSON text with a BOM has a different byte revision");
                Reject(() => settings.ApplyExternalPatch(args["patch"], result["revision"].Value), "BOM-only change conflicts");
                Assert(settings.LoadSettings(), "explicit reload accepts BOM settings");
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    Reject(() => settings.ApplyExternalPatch(args["patch"], ""), "unreadable settings are not absence");
                result = settings.ApplyExternalPatch(args["patch"], JsonFiles.ComputeRevision(File.ReadAllBytes(path)));
                main.Process("missing-revision", "{\"id\":\"missing-revision\",\"command\":\"applySettingsPatch\",\"args\":{\"patch\":{}}}");
                Assert(!Result("missing-revision")["success"].AsBool, "external revision cannot be omitted");
                args["expectedRevision"] = result["revision"];
                main.Process("lost-result", Envelope("lost-result", "applySettingsPatch", args));
                Assert(Result("lost-result")["success"].AsBool, "connected save succeeds with current revision");
                before = File.ReadAllBytes(path);
                File.Delete(Path.Combine(_dir, "command-results", "lost-result.json"));
                main.Process("lost-result", Envelope("lost-result", "applySettingsPatch", args));
                Assert(before.SequenceEqual(File.ReadAllBytes(path)) && !File.Exists(Path.Combine(_dir, "command-results", "lost-result.json")), "lost result does not replay a claimed settings command");
                File.WriteAllText(path, "{");
                Reject(() => settings.ApplyExternalPatch(args["patch"], ""), "corrupt file is not absence");
                Reject(() => settings.SaveSettings(), "native save does not replace corrupt file");
                File.Delete(path);
                Reject(() => settings.SaveSettings(), "deleted loaded file conflicts");
                settings = new SavedSettings(_dir);
                result = settings.ApplyExternalPatch(args["patch"], "");
                Reject(() => new SavedSettings(_dir).ApplyExternalPatch(args["patch"], ""), "concurrent creation conflicts");
                CheckSharedSettingsContract();
                CheckEveryPersistentSetter();
                Console.WriteLine("PASS: settings CAS: " + _assertions + " assertions.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                main?.StartupFailure();
                Environment.SetEnvironmentVariable("NGUINJECTOR_EXTERNAL_UI", previousExternalUi);
                Directory.Delete(_dir, true);
            }
        }

        private static void CheckSharedSettingsContract()
        {
            JSONObject fixture;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SettingsContract.json"))
            using (var reader = new StreamReader(stream)) fixture = JsonFiles.ParseObject(reader.ReadToEnd());
            var directory = Path.Combine(_dir, "shared"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            foreach (var row in fixture["patches"].Children)
            {
                File.WriteAllText(path, fixture["sourceJson"].Value);
                var settings = new SavedSettings(directory);
                Assert(settings.LoadSettings(), "shared source loads: " + row["name"]);
                var before = File.ReadAllBytes(path);
                var revision = JsonFiles.ComputeRevision(before);
                var patch = JsonFiles.ParseObject(row["json"].Value);
                if (!row["accept"].AsBool)
                {
                    Reject(() => settings.ApplyExternalPatch(patch, revision), "shared rejection: " + row["name"]);
                    Assert(before.SequenceEqual(File.ReadAllBytes(path)), "shared rejection preserves bytes: " + row["name"]);
                    continue;
                }
                var saved = settings.ApplyExternalPatch(patch, revision)["settings"];
                var source = JsonFiles.ParseObject(fixture["sourceJson"].Value);
                Assert(saved["Future"].ToString() == source["Future"].ToString(), "shared exact unknown tokens: " + row["name"]);
                Assert(saved["_customIronPillHistory"].ToString() == source["_customIronPillHistory"].ToString(), "shared backend provenance: " + row["name"]);
                foreach (var item in fixture["defaults"].Linq)
                    if (!patch.Linq.Any(entry => entry.Key == item.Key)) Assert(saved[item.Key].ToString() == item.Value.ToString(), "shared default " + item.Key);
            }
        }

        private static void CheckEveryPersistentSetter()
        {
            var previous = NGUInjector.Main.Settings;
            var properties = typeof(SavedSettings).GetProperties().Where(property => property.CanWrite
                && property.Name != "QuickLoadout" && property.Name != "QuickDiggers" && property.Name != "DebugAllocation").ToArray();
            string Value(object value) => value is Array array ? string.Join(",", array.Cast<object>()) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                foreach (var property in properties)
                {
                    var directory = Path.Combine(_dir, "setter-" + property.Name); Directory.CreateDirectory(directory);
                    var settings = new SavedSettings(directory); NGUInjector.Main.Settings = settings;
                    settings.SaveSettings();
                    var path = Path.Combine(directory, "settings.json");
                    var initial = property.GetValue(settings);
                    object next;
                    if (property.PropertyType == typeof(bool)) next = !(bool) initial;
                    else if (property.PropertyType == typeof(int)) next = property.Name == "ManualTitanZone" ? Managers.ZoneHelpers.TitanZones.First(zone => zone != (int) initial) : (int) initial + 1;
                    else if (property.PropertyType == typeof(double)) next = 1.25d;
                    else if (property.PropertyType == typeof(string)) next = "audit";
                    else if (property.PropertyType == typeof(int[])) next = initial is int[] integers ? integers.Select(value => value + 1).ToArray() : new[] {40, 41};
                    else if (property.PropertyType == typeof(bool[])) next = Enumerable.Repeat(true, 12).ToArray();
                    else if (property.PropertyType == typeof(double[])) next = ((double[]) initial).Select(value => value + 1).ToArray();
                    else throw new Exception("Unaudited persistent setter type: " + property.Name);
                    var before = File.ReadAllBytes(path);
                    using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var rejected = false;
                        try { property.SetValue(settings, next); }
                        catch (TargetInvocationException error) when (error.InnerException is IOException) { rejected = true; }
                        Assert(rejected, "every persistent setter reports failed persistence: " + property.Name);
                        Assert(Value(property.GetValue(settings)) == Value(initial), "failed setter retains live value: " + property.Name);
                        Assert(before.SequenceEqual(File.ReadAllBytes(path)), "failed setter retains exact bytes: " + property.Name);
                    }
                    property.SetValue(settings, next);
                    Assert(Value(property.GetValue(settings)) == Value(next), "same-value retry publishes after commit: " + property.Name);
                    Assert(!settings.HasSettingsFileChanged(), "setter advances exact-byte baseline: " + property.Name);
                    var restored = new SavedSettings(directory);
                    Assert(restored.LoadSettings() && Value(property.GetValue(restored)) == Value(next), "setter result is durable after restart: " + property.Name);
                }
                Console.WriteLine("Persistent setter audit: " + properties.Length + " public setters; 3 intentional unsaved setters excluded.");
            }
            finally { NGUInjector.Main.Settings = previous; }
        }
    }
}
