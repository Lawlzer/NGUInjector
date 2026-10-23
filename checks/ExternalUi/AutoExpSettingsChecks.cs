using System;
using System.IO;
using System.Linq;
using SimpleJSON;
using UnityEngine;

namespace NGUInjector
{
    internal static class AutoExpSettingsChecks
    {
        private static int _assertions;
        private static readonly string[] RatioFields = {
            "_customExpEnergyRatio", "_customExpMagicRatio",
            "_customExpEnergyPowerRatio", "_customExpEnergyCapRatio", "_customExpEnergyBarsRatio",
            "_customExpMagicPowerRatio", "_customExpMagicCapRatio", "_customExpMagicBarsRatio"
        };
        private static readonly int[] Defaults = {5, 1, 5, 160000, 4, 5, 160000, 4};
        private static readonly int[] PatchedRatios = {7, 3, 11, 1000000000, 13, 17, 230000, 1};

        private static void Equal<T>(T expected, T actual, string name)
        {
            _assertions++;
            if (!Equals(expected, actual)) throw new Exception(name + ": expected " + expected + ", got " + actual);
        }

        private static void CheckValues(SavedSettings settings, bool enabled, int[] ratios, string name)
        {
            Equal(enabled, settings.CustomAutoExpEnabled, name + " toggle");
            var actual = new[] {
                settings.CustomExpEnergyRatio, settings.CustomExpMagicRatio,
                settings.CustomExpEnergyPowerRatio, settings.CustomExpEnergyCapRatio, settings.CustomExpEnergyBarsRatio,
                settings.CustomExpMagicPowerRatio, settings.CustomExpMagicCapRatio, settings.CustomExpMagicBarsRatio
            };
            for (var i = 0; i < RatioFields.Length; i++)
                Equal(ratios[i], actual[i], name + " " + RatioFields[i]);
        }

        internal static int Run(string parent)
        {
            var previous = JsonUtility.ZeroMissingSerializedFields;
            try
            {
                JsonUtility.ZeroMissingSerializedFields = true;
                CheckValues(new SavedSettings(null), false, Defaults, "new settings defaults");
                var rawMissing = JsonFiles.ParseObject(JsonUtility.ToJson(JsonUtility.FromJson<SavedSettings>("{}")));
                Equal(false, rawMissing["_customAutoExpEnabled"].AsBool, "Unity fixture missing toggle is false");
                foreach (var field in RatioFields)
                    Equal(0, rawMissing[field].AsInt, "Unity fixture bypasses missing initializer for " + field);
                CheckValues(JsonUtility.FromJson<SavedSettings>("{}"), false, Defaults, "Unity missing-field public defaults");

                var directory = Path.Combine(parent, "auto-exp-settings");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "settings.json");
                var legacy = JsonFiles.ParseObject("{\"futureSetting\":{\"_customAutoExpEnabled\":true,\"_customExpEnergyRatio\":99,\"precise\":9223372036854775807,\"decimal\":1.234567890123456789,\"items\":[true,null,\"keep\"]},\"futureNullable\":null}");
                File.WriteAllText(path, legacy.ToString());
                var settings = new SavedSettings(directory);
                Equal(true, settings.LoadSettings(), "legacy settings without auto EXP load");
                CheckValues(settings, false, Defaults, "absent auto EXP settings ignore nested unknown keys");
                var defaultsCopy = new SavedSettings(null);
                defaultsCopy.MassUpdate(settings);
                CheckValues(defaultsCopy, false, Defaults, "MassUpdate missing-field defaults");

                // This field is deliberately unsaved, so patching must merge live state, not stale disk.
                settings.QuickLoadout = new[] {40, 41};
                var patch = new JSONObject();
                patch["_customAutoExpEnabled"] = true;
                for (var i = 0; i < RatioFields.Length; i++) patch[RatioFields[i]] = PatchedRatios[i];
                Equal(9, patch.Count, "patch includes toggle and all eight ratios");
                settings.ApplyPatch(patch);
                CheckValues(settings, true, PatchedRatios, "all-nine patch publishes live values");
                var persisted = JsonFiles.ParseObject(File.ReadAllText(path));
                Equal(true, persisted["_customAutoExpEnabled"].IsBoolean && persisted["_customAutoExpEnabled"].AsBool, "patch persists boolean toggle");
                for (var i = 0; i < RatioFields.Length; i++)
                {
                    Equal(true, persisted[RatioFields[i]].IsNumber, "patch persists numeric " + RatioFields[i]);
                    Equal(PatchedRatios[i], persisted[RatioFields[i]].AsInt, "patch persists exact " + RatioFields[i]);
                }
                Equal("[40,41]", persisted["_quickLoadout"].ToString(), "auto EXP patch preserves unsaved live settings");
                Equal(legacy["futureSetting"].ToString(), persisted["futureSetting"].ToString(), "auto EXP patch preserves nested unknown values and exact numeric tokens");
                Equal(true, persisted.Linq.Any(item => item.Key == "futureNullable" && item.Value.IsNull), "auto EXP patch preserves unknown null field");
                Equal(false, settings.HasSettingsFileChanged(), "auto EXP patch updates watcher baseline");

                var reloaded = new SavedSettings(directory);
                Equal(true, reloaded.LoadSettings(), "auto EXP patch independently reloads");
                CheckValues(reloaded, true, PatchedRatios, "all nine persisted values reload");
                var copy = new SavedSettings(null);
                copy.MassUpdate(reloaded);
                CheckValues(copy, true, PatchedRatios, "MassUpdate copies all nine values");
                settings.SaveSettings();
                var afterSave = new SavedSettings(directory);
                Equal(true, afterSave.LoadSettings(), "ordinary SaveSettings auto EXP reload");
                CheckValues(afterSave, true, PatchedRatios, "ordinary SaveSettings retains all nine values");
                persisted = JsonFiles.ParseObject(File.ReadAllText(path));
                Equal(legacy["futureSetting"].ToString(), persisted["futureSetting"].ToString(), "ordinary save retains unknown data after auto EXP patch");
                Equal(true, persisted.Linq.Any(item => item.Key == "futureNullable" && item.Value.IsNull), "ordinary save retains unknown null");

                foreach (var field in RatioFields)
                foreach (var invalid in new[] {"0", "-1", "1.5", "1000000001", "true", "false", "null", "\"5\"", "{}", "[]"})
                {
                    var badPatch = new JSONObject();
                    badPatch["_customAutoExpEnabled"] = false;
                    badPatch[field == RatioFields[0] ? RatioFields[1] : RatioFields[0]] = 23;
                    badPatch[field] = JsonFiles.ParseObject("{\"value\":" + invalid + "}")["value"];
                    CheckRejected(settings, path, badPatch, field + "=" + invalid);
                }
                foreach (var invalid in new[] {"0", "1", "-1", "1.5", "1000000001", "\"true\"", "null", "{}", "[]"})
                {
                    var badPatch = new JSONObject();
                    badPatch[RatioFields[0]] = 23;
                    badPatch["_customAutoExpEnabled"] = JsonFiles.ParseObject("{\"value\":" + invalid + "}")["value"];
                    CheckRejected(settings, path, badPatch, "toggle=" + invalid);
                }
                settings.ApplyPatch(JsonFiles.ParseObject("{\"_customAutoExpEnabled\":false}"));
                CheckValues(settings, false, PatchedRatios, "explicit false toggle preserves ratios");
                Equal(true, reloaded.LoadSettings(), "disabled auto EXP reloads");
                CheckValues(reloaded, false, PatchedRatios, "explicit false toggle persists");
                Console.WriteLine("PASS: auto EXP settings: " + _assertions + " assertions.");
                return _assertions;
            }
            finally { JsonUtility.ZeroMissingSerializedFields = previous; }
        }

        private static void CheckRejected(SavedSettings settings, string path, JSONObject patch, string name)
        {
            var beforeLive = JsonUtility.ToJson(settings);
            var beforeBytes = File.ReadAllBytes(path);
            var rejected = false;
            try { settings.ApplyPatch(patch); }
            catch (FormatException) { rejected = true; }
            Equal(true, rejected, "invalid auto EXP patch rejects with FormatException: " + name);
            Equal(beforeLive, JsonUtility.ToJson(settings), "invalid auto EXP patch leaves all live fields unchanged: " + name);
            Equal(true, beforeBytes.SequenceEqual(File.ReadAllBytes(path)), "invalid auto EXP patch leaves disk bytes unchanged: " + name);
            Equal(false, settings.HasSettingsFileChanged(), "invalid auto EXP patch leaves watcher baseline unchanged: " + name);
        }
    }
}
