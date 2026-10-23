using System;
using System.IO;
using System.Linq;
using SimpleJSON;
using UnityEngine;

namespace NGUInjector
{
    internal static class AdventureSettingsChecks
    {
        private static int _assertions;

        private static void Equal<T>(T expected, T actual, string name)
        {
            _assertions++;
            if (!Equals(expected, actual)) throw new Exception(name + ": expected " + expected + ", got " + actual);
        }

        private static void CheckValues(SavedSettings settings, int mode, int deaths, string name)
        {
            Equal(mode, settings.CustomAdventureMode, name + " mode");
            Equal(deaths, settings.CustomAdventureMaxItopodDeaths, name + " death limit");
        }

        private static void CheckManualCombatSettings(string parent)
        {
            var previousZones = Managers.ZoneHelpers.TitanZones;
            // This bridge fixture normally uses two dummy titans; these settings require real zone IDs.
            Managers.ZoneHelpers.TitanZones = new[] {6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42};
            try
            {
                var beforeAssertions = _assertions;
                var directory = Path.Combine(parent, "manual-combat-settings");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "settings.json");
                var defaults = JsonFiles.ParseObject("{\"_manualAdventureLoadout\":-1,\"_manualTitanEnabled\":false,\"_manualTitanZone\":16,\"_manualTitanRequireAT\":true,\"_manualTitanLoadout\":1,\"_manualTitanMaxDeaths\":3}");
                var fresh = new SavedSettings(null);
                foreach (var item in defaults.Linq)
                    Equal(item.Value.ToString(), JsonFiles.ParseObject(JsonUtility.ToJson(fresh))[item.Key].ToString(), "new manual combat default " + item.Key);
                File.WriteAllText(path, "{\"_manualAdventureEnabled\":false,\"Future\":{\"exact\":1.234567890123456789},"
                    + "\"_energyNguWeights\":[0,0,0,0,1,0,1,0,0],\"_magicNguWeights\":[1,0,0,0,0,0,0]}");
                var legacyBytes = File.ReadAllBytes(path);
                var settings = new SavedSettings(directory);
                Equal(true, settings.LoadSettings(), "legacy manual combat settings load");
                Equal(true, legacyBytes.SequenceEqual(File.ReadAllBytes(path)), "legacy manual defaults with persisted global NGU ratios do not rewrite disk");
                var copy = new SavedSettings(null);
                copy.MassUpdate(settings);
                foreach (var item in defaults.Linq)
                    Equal(item.Value.ToString(), JsonFiles.ParseObject(JsonUtility.ToJson(copy))[item.Key].ToString(), "missing Unity field default survives MassUpdate " + item.Key);
                settings.SaveSettings();
                Equal(true, new SavedSettings(directory).LoadSettings(), "manual combat defaults roundtrip");

                foreach (var zone in Managers.ZoneHelpers.TitanZones)
                foreach (var loadout in new[] {-1, 0, 1, 2})
                {
                    var patch = JsonFiles.ParseObject("{\"_manualAdventureLoadout\":" + loadout + ",\"_manualTitanEnabled\":true,\"_manualTitanZone\":" + zone
                        + ",\"_manualTitanRequireAT\":false,\"_manualTitanLoadout\":" + loadout + ",\"_manualTitanMaxDeaths\":" + (loadout == -1 ? 1 : 1000) + "}");
                    settings.ApplyPatch(patch);
                    var reloaded = new SavedSettings(directory);
                    Equal(true, reloaded.LoadSettings(), "manual combat patch reloads");
                    copy.MassUpdate(reloaded);
                    var actual = JsonFiles.ParseObject(JsonUtility.ToJson(copy));
                    foreach (var item in patch.Linq)
                        Equal(item.Value.ToString(), actual[item.Key].ToString(), "manual combat patch persists and mass-updates " + item.Key);
                    Equal(false, copy.ManualAdventureEnabled, "manual titan enable never enables Manual Adventure");
                }

                foreach (var field in defaults.Linq.Select(item => item.Key))
                foreach (var invalid in new[] {"null", "\"1\"", "[]", "{}", "1.5", "2147483648", "1.0000000000000001", "1e-400"}
                    .Concat(field == "_manualTitanEnabled" || field == "_manualTitanRequireAT" ? new[] {"0", "1"}
                        : field == "_manualTitanZone" ? new[] {"false", "true", "-1", "0", "7", "15", "20", "43", "1000"}
                        : field == "_manualTitanMaxDeaths" ? new[] {"false", "true", "0", "1001"} : new[] {"false", "true", "-2", "3"}))
                {
                    var beforeLive = JsonUtility.ToJson(settings);
                    var beforeBytes = File.ReadAllBytes(path);
                    var rejected = false;
                    try { settings.ApplyPatch(JsonFiles.ParseObject("{\"_customAdventureEnabled\":true,\"" + field + "\":" + invalid + "}")); }
                    catch (FormatException) { rejected = true; }
                    Equal(true, rejected, "manual combat rejects " + field + "=" + invalid);
                    Equal(beforeLive, JsonUtility.ToJson(settings), "invalid manual combat patch is atomic in memory");
                    Equal(true, beforeBytes.SequenceEqual(File.ReadAllBytes(path)), "invalid manual combat patch is atomic on disk");
                }

                settings.ManualAdventureLoadout = -1;
                settings.ManualTitanEnabled = false;
                settings.ManualTitanZone = 16;
                settings.ManualTitanRequireAT = true;
                settings.ManualTitanLoadout = 1;
                settings.ManualTitanMaxDeaths = 3;
                var native = new SavedSettings(directory);
                Equal(true, native.LoadSettings(), "manual combat property setters persist");
                Equal(-1, native.ManualAdventureLoadout, "native adventure loadout");
                Equal(false, native.ManualTitanEnabled, "native titan opt-in");
                Equal(16, native.ManualTitanZone, "native titan zone");
                Equal(true, native.ManualTitanRequireAT, "native AT gate");
                Equal(1, native.ManualTitanLoadout, "native titan loadout");
                Equal(3, native.ManualTitanMaxDeaths, "native death limit");
                foreach (var assign in new Action[] {
                    () => settings.ManualAdventureLoadout = -2, () => settings.ManualAdventureLoadout = 3,
                    () => settings.ManualTitanLoadout = -2, () => settings.ManualTitanLoadout = 3,
                    () => settings.ManualTitanZone = 7, () => settings.ManualTitanMaxDeaths = 0, () => settings.ManualTitanMaxDeaths = 1001})
                {
                    var rejected = false;
                    try { assign(); } catch (ArgumentOutOfRangeException) { rejected = true; }
                    Equal(true, rejected, "native manual combat setter rejects invalid range");
                }
                var saved = JsonFiles.ParseObject(File.ReadAllText(path));
                Equal("{\"exact\":1.234567890123456789}", saved["Future"].ToString(), "manual combat writes preserve exact unknown settings");
                Equal(true, saved["manualTitan"] == null, "manual titan runtime budget is not persisted");
                Console.WriteLine("PASS: manual combat settings: " + (_assertions - beforeAssertions) + " assertions.");
            }
            finally { Managers.ZoneHelpers.TitanZones = previousZones; }
        }

        private static void CheckManualAdventureBonuses(string parent)
        {
            var directory = Path.Combine(parent, "manual-adventure-bonuses");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            var presetPath = Path.Combine(directory, "presets.json");
            const string preset = "{\"Presets\":[{\"Id\":\"base\",\"Weights\":[1,2,3],\"BeardPriority\":[5],\"DiggerWeights\":[1,2]}]}";
            File.WriteAllText(presetPath, preset);
            const string legacy = "{\"_energyNguWeights\":[0,0,0,0,1,0,1,0,0],\"_magicNguWeights\":[1,0,0,0,0,0,0],\"_manualAdventureEnabled\":false,\"_manualAdventureLoadout\":2,\"Future\":1.234567890123456789}";
            File.WriteAllText(path, legacy);
            var settings = new SavedSettings(directory);
            Equal(true, settings.LoadSettings(), "legacy bonuses load");
            Equal(legacy, File.ReadAllText(path), "bonus defaults do not rewrite legacy settings on read");
            var missing = JsonUtility.FromJson<SavedSettings>("{\"_energyNguWeights\":[0,0,0,0,1,0,1,0,0],\"_magicNguWeights\":[1,0,0,0,0,0,0]}");
            var copy = new SavedSettings(null);
            copy.MassUpdate(missing);
            foreach (var row in new[] {new {Key = "_manualAdventureGearWeights", Count = 5}, new {Key = "_manualAdventureBeardWeights", Count = 7}, new {Key = "_manualAdventureDiggerWeights", Count = 12}})
            {
                var property = typeof(SavedSettings).GetProperty(row.Key.Substring(1, 1).ToUpperInvariant() + row.Key.Substring(2));
                Equal(true, ((double[]) property.GetValue(copy)).SequenceEqual(new double[row.Count]), "missing bonus vector normalizes " + row.Key);
                var values = Enumerable.Repeat(0d, row.Count).ToArray();
                values[0] = 100;
                values[1] = 0.12345678901234566;
                values[row.Count - 1] = 1000000;
                property.SetValue(settings, values);
                values[0] = -1;
                var reloaded = new SavedSettings(directory);
                Equal(true, reloaded.LoadSettings(), "bonus vector roundtrip " + row.Key);
                var actual = (double[]) property.GetValue(reloaded);
                Equal(100d, actual[0], "setter clones bonus input " + row.Key);
                Equal(0.12345678901234566, actual[1], "bonus doubles preserve runtime precision " + row.Key);
                actual[0] = -1;
                Equal(100d, ((double[]) property.GetValue(reloaded))[0], "getter clones bonus output " + row.Key);
                copy.MassUpdate(reloaded);
                Equal(true, ((double[]) property.GetValue(copy)).SequenceEqual((double[]) property.GetValue(reloaded)), "MassUpdate retains bonus vector " + row.Key);
                foreach (var invalid in new[] {"null", "0", "{}", "[]", "[0]"}.Concat(new[] {"-1", "1000001", "1e400", "true", "null", "\"100\""}.Select(value => "[" + string.Join(",", Enumerable.Repeat(value, row.Count)) + "]")))
                {
                    var before = File.ReadAllText(path);
                    var beforeLive = JsonUtility.ToJson(settings);
                    var rejected = false;
                    try { settings.ApplyPatch(JsonFiles.ParseObject("{\"_manualAdventureEnabled\":true,\"" + row.Key + "\":" + invalid + "}")); }
                    catch (FormatException) { rejected = true; }
                    Equal(true, rejected, "reject invalid bonus vector " + row.Key + "=" + invalid);
                    Equal(before, File.ReadAllText(path), "invalid bonus patch preserves disk");
                    Equal(beforeLive, JsonUtility.ToJson(settings), "invalid bonus patch preserves live settings");
                }
            }
            var beforeUnrelated = JsonFiles.ParseObject(File.ReadAllText(path));
            settings.ApplyPatch(JsonFiles.ParseObject("{\"_manageEnergy\":true}"));
            var saved = JsonFiles.ParseObject(File.ReadAllText(path));
            foreach (var key in new[] {"_manualAdventureGearWeights", "_manualAdventureBeardWeights", "_manualAdventureDiggerWeights", "Future", "_manualAdventureLoadout", "_manualAdventureEnabled"})
                Equal(beforeUnrelated[key].ToString(), saved[key].ToString(), "unrelated edit retains bonuses and settings " + key);
            Equal(preset, File.ReadAllText(presetPath), "bonuses never rewrite base presets");
        }

        internal static int Run(string parent, Main main)
        {
            var previous = JsonUtility.ZeroMissingSerializedFields;
            var previousSettings = Main.Settings;
            try
            {
                JsonUtility.ZeroMissingSerializedFields = true;
                CheckManualAdventureBonuses(parent);
                CheckManualCombatSettings(parent);
                CheckValues(new SavedSettings(null), 0, 3, "new settings defaults");
                Equal(3, JsonFiles.ParseObject(JsonUtility.ToJson(new SavedSettings(null)))["_customAdventureMaxItopodDeaths"].AsInt,
                    "new serialized death-limit field initializes to three");
                var missing = JsonUtility.FromJson<SavedSettings>("{}");
                Equal(0, JsonFiles.ParseObject(JsonUtility.ToJson(missing))["_customAdventureMaxItopodDeaths"].AsInt,
                    "Unity fixture bypasses missing death-limit initializer");
                CheckValues(missing, 0, 3, "Unity missing-field public defaults");
                foreach (var raw in new[] {int.MinValue, -1, 0, 1, 3, 1000, 1001, int.MaxValue})
                {
                    var source = JsonUtility.FromJson<SavedSettings>("{\"_customAdventureMaxItopodDeaths\":" + raw
                        + ",\"_energyNguWeights\":[0,0,0,0,1,0,1,0,0],\"_magicNguWeights\":[1,0,0,0,0,0,0]}");
                    var expected = raw <= 0 ? 3 : Math.Min(1000, raw);
                    Equal(expected, source.CustomAdventureMaxItopodDeaths, "legacy getter normalizes " + raw);
                    var copy = new SavedSettings(null) {CustomAdventureMaxItopodDeaths = 99};
                    copy.MassUpdate(source);
                    Equal(expected, copy.CustomAdventureMaxItopodDeaths, "MassUpdate normalizes " + raw);
                }

                var directory = Path.Combine(parent, "adventure-settings");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "settings.json");
                var legacy = JsonFiles.ParseObject("{\"futureSetting\":{\"_customAdventureMaxItopodDeaths\":99,\"_customAdventureMode\":3,\"precise\":9223372036854775807,\"decimal\":1.234567890123456789,\"items\":[true,null,\"keep\"]},\"futureNullable\":null}");
                legacy["_energyNguWeights"] = CustomPlans.CustomPlanStore.SerializeNguWeights(new SavedSettings(null).EnergyNguWeights, 9, "energy");
                legacy["_magicNguWeights"] = CustomPlans.CustomPlanStore.SerializeNguWeights(new SavedSettings(null).MagicNguWeights, 7, "magic");
                SavedSettings settings = null;
                for (var mode = 0; mode <= 2; mode++)
                {
                    legacy["_customAdventureMode"] = mode;
                    File.WriteAllText(path, legacy.ToString());
                    settings = new SavedSettings(directory);
                    Equal(true, settings.LoadSettings(), "legacy mode loads: " + mode);
                    CheckValues(settings, mode, 3, "legacy mode retains meaning and missing default");
                    Equal(legacy.ToString(), File.ReadAllText(path), "legacy load does not rewrite disk");
                    var copy = new SavedSettings(null) {CustomAdventureMode = 3, CustomAdventureMaxItopodDeaths = 99};
                    copy.MassUpdate(settings);
                    CheckValues(copy, mode, 3, "MassUpdate copies legacy mode and default");
                    settings.SaveSettings();
                    Equal(3, JsonFiles.ParseObject(File.ReadAllText(path))["_customAdventureMaxItopodDeaths"].AsInt,
                        "ordinary save explicitly persists missing-field default");
                    var reloaded = new SavedSettings(directory);
                    Equal(true, reloaded.LoadSettings(), "legacy default save reloads");
                    CheckValues(reloaded, mode, 3, "legacy default round trip");
                }

                Main.Settings = settings;
                var runtime = main.BuildRuntime()["settings"];
                Equal(true, runtime["customAdventureMaxItopodDeaths"].IsNumber, "runtime default death limit is numeric");
                Equal(3, runtime["customAdventureMaxItopodDeaths"].AsInt, "runtime projects default death limit");
                // QuickLoadout deliberately does not save, so patches must use live rather than stale disk state.
                settings.QuickLoadout = new[] {40, 41};
                var limits = new[] {1, 3, 17, 1000};
                for (var mode = 0; mode <= 3; mode++)
                {
                    settings.ApplyPatch(JsonFiles.ParseObject("{\"_customAdventureMode\":" + mode
                        + ",\"_customAdventureMaxItopodDeaths\":" + limits[mode] + "}"));
                    CheckValues(settings, mode, limits[mode], "patch publishes adventure settings");
                    var afterPatch = new SavedSettings(directory);
                    Equal(true, afterPatch.LoadSettings(), "patch independently persists before ordinary save");
                    CheckValues(afterPatch, mode, limits[mode], "patch-only round trip");
                    var patched = JsonFiles.ParseObject(File.ReadAllText(path));
                    Equal("[40,41]", patched["_quickLoadout"].ToString(), "patch persists unsaved live fields");
                    Equal(legacy["futureSetting"].ToString(), patched["futureSetting"].ToString(), "patch alone preserves exact unknown data");
                    Equal(true, patched.Linq.Any(item => item.Key == "futureNullable" && item.Value.IsNull), "patch alone preserves unknown null");
                    runtime = main.BuildRuntime()["settings"];
                    Equal(mode, runtime["customAdventureMode"].AsInt, "runtime projects patched mode");
                    Equal(true, runtime["customAdventureMaxItopodDeaths"].IsNumber, "runtime patched death limit is numeric");
                    Equal(limits[mode], runtime["customAdventureMaxItopodDeaths"].AsInt, "runtime projects patched death limit");
                    settings.SaveSettings();
                    var persisted = JsonFiles.ParseObject(File.ReadAllText(path));
                    Equal(true, persisted["_customAdventureMode"].IsNumber, "saved mode is numeric");
                    Equal(mode, persisted["_customAdventureMode"].AsInt, "saved mode is exact");
                    Equal(true, persisted["_customAdventureMaxItopodDeaths"].IsNumber, "saved death limit is numeric");
                    Equal(limits[mode], persisted["_customAdventureMaxItopodDeaths"].AsInt, "saved death limit is exact");
                    Equal("[40,41]", persisted["_quickLoadout"].ToString(), "patch preserves unsaved live fields");
                    Equal(legacy["futureSetting"].ToString(), persisted["futureSetting"].ToString(), "patch and save preserve exact unknown data");
                    Equal(true, persisted.Linq.Any(item => item.Key == "futureNullable" && item.Value.IsNull), "patch and save preserve unknown null");
                    var reloaded = new SavedSettings(directory);
                    Equal(true, reloaded.LoadSettings(), "patched adventure settings reload");
                    CheckValues(reloaded, mode, limits[mode], "patched save round trip");
                    var copy = new SavedSettings(null);
                    copy.MassUpdate(reloaded);
                    CheckValues(copy, mode, limits[mode], "MassUpdate copies patched adventure settings");
                    Equal(false, settings.HasSettingsFileChanged(), "successful adventure save updates watcher baseline");
                }

                foreach (var field in new[] {"_customAdventureMode", "_customAdventureMaxItopodDeaths"})
                foreach (var invalid in new[] {"-1", "1001", "1.5", "1000.5", "2147483648", "1e100", "true", "false", "null", "\"3\"", "{}", "[]"}
                    .Concat(field == "_customAdventureMode" ? new[] {"4"} : new[] {"0"}))
                {
                    var patch = new JSONObject();
                    patch["_customAdventureEnabled"] = true;
                    patch[field == "_customAdventureMode" ? "_customAdventureMaxItopodDeaths" : "_customAdventureMode"] =
                        field == "_customAdventureMode" ? 7 : 0;
                    patch[field] = JsonFiles.ParseObject("{\"value\":" + invalid + "}")["value"];
                    var beforeLive = JsonUtility.ToJson(settings);
                    var beforeBytes = File.ReadAllBytes(path);
                    var rejected = false;
                    try { settings.ApplyPatch(patch); }
                    catch (FormatException) { rejected = true; }
                    Equal(true, rejected, "invalid adventure patch rejects with FormatException: " + field + "=" + invalid);
                    Equal(beforeLive, JsonUtility.ToJson(settings), "invalid patch leaves all live fields unchanged");
                    Equal(true, beforeBytes.SequenceEqual(File.ReadAllBytes(path)), "invalid patch leaves all disk bytes including unknown data unchanged");
                    Equal(false, settings.HasSettingsFileChanged(), "invalid patch retains watcher baseline");
                }

                foreach (var deaths in new[] {1, 3, 1000})
                {
                    settings.CustomAdventureMaxItopodDeaths = deaths;
                    var reloaded = new SavedSettings(directory);
                    Equal(true, reloaded.LoadSettings(), "native death-limit setter saves");
                    CheckValues(reloaded, 3, deaths, "native death-limit bounds round trip");
                }
                foreach (var field in new[] {"mode", "deaths"})
                foreach (var invalid in field == "mode" ? new[] {int.MinValue, -1, 4, int.MaxValue} : new[] {int.MinValue, -1, 0, 1001, int.MaxValue})
                {
                    var beforeLive = JsonUtility.ToJson(settings);
                    var beforeBytes = File.ReadAllBytes(path);
                    var rejected = false;
                    try
                    {
                        if (field == "mode") settings.CustomAdventureMode = invalid;
                        else settings.CustomAdventureMaxItopodDeaths = invalid;
                    }
                    catch (ArgumentOutOfRangeException) { rejected = true; }
                    Equal(true, rejected, "invalid native adventure " + field + " rejects: " + invalid);
                    Equal(beforeLive, JsonUtility.ToJson(settings), "invalid native setter leaves live fields unchanged");
                    Equal(true, beforeBytes.SequenceEqual(File.ReadAllBytes(path)), "invalid native setter leaves disk unchanged");
                }

                var beforeFallback = JsonUtility.ToJson(settings);
                var beforeFallbackBytes = File.ReadAllBytes(path);
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var rejected = false;
                    try { settings.CustomAdventureMode = 0; }
                    catch (IOException) { rejected = true; }
                    Equal(true, rejected, "mode-zero fallback reports SaveSettings failure");
                    Equal(beforeFallback, JsonUtility.ToJson(settings), "failed fallback rolls mode back without changing any live fields");
                    CheckValues(settings, 3, 1000, "failed fallback retains push settings");
                }
                Equal(true, beforeFallbackBytes.SequenceEqual(File.ReadAllBytes(path)), "failed fallback preserves disk bytes including unknown data");
                Equal(false, settings.HasSettingsFileChanged(), "failed fallback preserves watcher baseline");
                var afterFailure = new SavedSettings(directory);
                Equal(true, afterFailure.LoadSettings(), "settings reload after failed fallback");
                CheckValues(afterFailure, 3, 1000, "failed fallback never persists mode zero");
                settings.CustomAdventureMode = 0;
                var afterRetry = new SavedSettings(directory);
                Equal(true, afterRetry.LoadSettings(), "fallback retry independently reloads");
                CheckValues(afterRetry, 0, 1000, "fallback retry durably saves mode zero");
                Equal(legacy["futureSetting"].ToString(), JsonFiles.ParseObject(File.ReadAllText(path))["futureSetting"].ToString(),
                    "native setters and fallback retain exact unknown data");
                Equal(false, settings.HasSettingsFileChanged(), "fallback retry updates watcher baseline");
                Console.WriteLine("PASS: adventure settings: " + _assertions + " assertions.");
                return _assertions;
            }
            finally
            {
                Main.Settings = previousSettings;
                JsonUtility.ZeroMissingSerializedFields = previous;
            }
        }
    }
}
