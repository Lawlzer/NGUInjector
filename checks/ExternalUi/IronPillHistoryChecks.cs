using System;
using System.IO;
using System.Linq;
using SimpleJSON;

namespace NGUInjector
{
    internal static class IronPillHistoryChecks
    {
        private static int _assertions;
        private static void Equal<T>(T expected, T actual, string name)
        {
            _assertions++;
            if (!Equals(expected, actual)) throw new Exception(name + ": expected " + expected + ", got " + actual);
        }

        private static IronPillHistoryEntry Entry(int index)
        {
            return new IronPillHistoryEntry {
                CastAtUtc = "2026-10-14T12:00:" + index.ToString("00") + "Z",
                WaitSeconds = index * 60.25,
                Worth = index * 10.5,
                RebirthSeconds = 3600 + index
            };
        }

        private static string History(IronPillHistoryEntry[] entries)
        {
            return string.Join("|", entries.Select(entry => string.Join(",", entry.CastAtUtc, entry.WaitSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                entry.Worth.ToString("R", System.Globalization.CultureInfo.InvariantCulture), entry.RebirthSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
        }

        internal static int Run(string parent)
        {
            var previous = UnityEngine.JsonUtility.OmitCustomIronPillHistory;
            try
            {
                foreach (var omit in new[] {false, true})
                {
                    UnityEngine.JsonUtility.OmitCustomIronPillHistory = omit;
                    var directory = Path.Combine(parent, "iron-history-" + omit);
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "settings.json");
                    File.WriteAllText(path, "{\"futureSetting\":{\"precise\":9007199254740993,\"items\":[true,null,\"keep\"]}}");
                    var settings = new SavedSettings(directory);
                    Equal(true, settings.LoadSettings(), "legacy settings without history load, omit=" + omit);
                    Equal(0, settings.CustomIronPillHistory.Length, "missing history defaults empty");
                    for (var i = 1; i <= 12; i++) settings.AddIronPillHistory(Entry(i));
                    Equal(omit, !JsonFiles.ParseObject(UnityEngine.JsonUtility.ToJson(settings)).Linq.Any(item => item.Key == "_customIronPillHistory"), "optional fixture really omits the Unity field only when enabled");
                    var saved = JsonFiles.ParseObject(File.ReadAllText(path));
                    Equal(true, saved["_customIronPillHistory"].IsArray, "automatic history append explicitly writes an array, omit=" + omit);
                    Equal(10, saved["_customIronPillHistory"].Count, "automatic save caps history at ten");
                    Equal(126d, saved["_customIronPillHistory"][0]["Worth"].AsDouble, "automatic save keeps newest first");
                    Equal(31.5d, saved["_customIronPillHistory"][9]["Worth"].AsDouble, "automatic save discards oldest");
                    Equal("9007199254740993", saved["futureSetting"]["precise"].Value, "automatic save preserves exact unknown numeric token");
                    var expected = History(settings.CustomIronPillHistory);
                    var reloaded = new SavedSettings(directory);
                    Equal(true, reloaded.LoadSettings(), "raw history reload succeeds with Unity omission, omit=" + omit);
                    Equal(expected, History(reloaded.CustomIronPillHistory), "all history fields survive independent raw reload");
                    var copy = new SavedSettings(null);
                    copy.MassUpdate(reloaded);
                    Equal(expected, History(copy.CustomIronPillHistory), "MassUpdate retains backend history");
                    settings.CustomIronPillMinWorth = 42.5;
                    var afterNative = new SavedSettings(directory);
                    Equal(true, afterNative.LoadSettings(), "ordinary native setter remains loadable");
                    Equal(expected, History(afterNative.CustomIronPillHistory), "native SaveSettings retains explicit history");

                    var beforeSuppression = File.ReadAllText(path);
                    settings.SetSaveDisabled(true);
                    try { settings.AddIronPillHistory(Entry(13)); }
                    finally { settings.SetSaveDisabled(false); }
                    Equal(beforeSuppression, File.ReadAllText(path), "save-disabled guard still suppresses disk writes");
                    Equal(136.5d, settings.CustomIronPillHistory[0].Worth, "suppressed save retains newest live history");
                    expected = History(settings.CustomIronPillHistory);
                    settings.ApplyPatch(JsonFiles.ParseObject("{\"_customIronPillSaveSeconds\":1234}"));
                    Equal(expected, History(settings.CustomIronPillHistory), "patch candidate retains unsaved live history, not Unity or stale disk");
                    var afterPatch = new SavedSettings(directory);
                    Equal(true, afterPatch.LoadSettings(), "patched settings reload");
                    Equal(expected, History(afterPatch.CustomIronPillHistory), "patch durably preserves all live history fields");
                    Equal(1234, afterPatch.CustomIronPillSaveSeconds, "ordinary patch still updates requested setting");
                    saved = JsonFiles.ParseObject(File.ReadAllText(path));
                    Equal("{\"precise\":9007199254740993,\"items\":[true,null,\"keep\"]}", saved["futureSetting"].ToString(), "native save and patch retain nested unknown fields");
                    var beforeRejectedPatch = File.ReadAllText(path);
                    var rejected = false;
                    try { settings.ApplyPatch(JsonFiles.ParseObject("{\"_customIronPillHistory\":[]}")); }
                    catch (FormatException) { rejected = true; }
                    Equal(true, rejected, "history remains backend-owned");
                    Equal(beforeRejectedPatch, File.ReadAllText(path), "read-only history rejection never writes");
                    Equal(expected, History(settings.CustomIronPillHistory), "read-only history rejection preserves live records");
                    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        rejected = false;
                        try { settings.ApplyPatch(JsonFiles.ParseObject("{\"_customIronPillMinWorth\":999}")); }
                        catch (IOException) { rejected = true; }
                        Equal(true, rejected, "failed atomic patch reports persistence error");
                    }
                    Equal(42.5d, settings.CustomIronPillMinWorth, "failed patch does not publish candidate settings");
                    Equal(expected, History(settings.CustomIronPillHistory), "failed patch does not publish candidate history");
                    Equal(beforeRejectedPatch, File.ReadAllText(path), "failed patch preserves disk bytes");
                    Equal(false, settings.HasSettingsFileChanged(), "successful writes retain matching watcher baseline");
                    var inMemory = new SavedSettings(null);
                    inMemory.AddIronPillHistory(Entry(1));
                    Equal(1, inMemory.CustomIronPillHistory.Length, "null-path fixture retains history without disk persistence");
                    CheckValidation(settings, path);
                }
                Console.WriteLine("PASS: durable Iron Pill history: " + _assertions + " assertions.");
                return _assertions;
            }
            finally { UnityEngine.JsonUtility.OmitCustomIronPillHistory = previous; }
        }

        private static void CheckValidation(SavedSettings settings, string path)
        {
            var before = History(settings.CustomIronPillHistory);
            var bytes = File.ReadAllText(path);
            var invalid = new[] {
                new IronPillHistoryEntry {WaitSeconds = -1},
                new IronPillHistoryEntry {WaitSeconds = double.NaN},
                new IronPillHistoryEntry {WaitSeconds = double.PositiveInfinity},
                new IronPillHistoryEntry {WaitSeconds = double.NegativeInfinity},
                new IronPillHistoryEntry {Worth = -1},
                new IronPillHistoryEntry {Worth = 1e8 + 1},
                new IronPillHistoryEntry {Worth = double.NaN},
                new IronPillHistoryEntry {Worth = double.PositiveInfinity},
                new IronPillHistoryEntry {RebirthSeconds = -1},
                new IronPillHistoryEntry {RebirthSeconds = double.NaN},
                new IronPillHistoryEntry {RebirthSeconds = double.PositiveInfinity},
                null
            };
            foreach (var entry in invalid)
            {
                var rejected = false;
                try { settings.AddIronPillHistory(entry); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Equal(true, rejected, "invalid automatic history record rejected before mutation");
                Equal(before, History(settings.CustomIronPillHistory), "invalid automatic record leaves live history unchanged");
                Equal(bytes, File.ReadAllText(path), "invalid automatic record leaves disk bytes unchanged");
            }

            var boundsDirectory = Path.Combine(Path.GetDirectoryName(path), "bounds");
            Directory.CreateDirectory(boundsDirectory);
            var bounds = new SavedSettings(boundsDirectory);
            bounds.AddIronPillHistory(new IronPillHistoryEntry {CastAtUtc = "", WaitSeconds = 0, Worth = 0, RebirthSeconds = 0});
            bounds.AddIronPillHistory(new IronPillHistoryEntry {
                CastAtUtc = "2026-10-14T12:00:00+00:00", WaitSeconds = double.MaxValue, Worth = 1e8, RebirthSeconds = double.MaxValue
            });
            var boundReload = new SavedSettings(boundsDirectory);
            Equal(true, boundReload.LoadSettings(), "finite numeric boundary history reloads");
            Equal(History(bounds.CustomIronPillHistory), History(boundReload.CustomIronPillHistory), "inclusive zero/worth-cap/max-finite bounds round-trip exactly");

            var source = new SavedSettings(null);
            for (var i = 1; i <= 3; i++) source.AddIronPillHistory(Entry(i));
            source.CustomIronPillHistory[0] = null;
            source.CustomIronPillHistory[1].Worth = double.NaN;
            var copy = new SavedSettings(null);
            copy.MassUpdate(source);
            Equal(History(new[] {Entry(1)}), History(copy.CustomIronPillHistory), "MassUpdate filters invalid and null records before retention");

            var root = JsonFiles.ParseObject(bytes);
            var validRows = root["_customIronPillHistory"].Children.ToArray();
            var rows = new JSONArray();
            rows.Add(JSONNull.CreateOrGet());
            rows.Add(new JSONNumber(7));
            rows.Add(new JSONObject());
            foreach (var badField in new[] {"CastAtUtc", "WaitSeconds", "Worth", "RebirthSeconds"})
            {
                var row = JsonFiles.ParseObject(validRows[0].ToString());
                row[badField] = badField == "CastAtUtc" ? (JSONNode) new JSONNumber(7) : new JSONString("1");
                rows.Add(row);
            }
            foreach (var badField in new[] {"WaitSeconds", "Worth", "RebirthSeconds"})
            {
                var row = JsonFiles.ParseObject(validRows[0].ToString());
                row[badField] = -1;
                rows.Add(row);
            }
            var overCap = JsonFiles.ParseObject(validRows[0].ToString());
            overCap["Worth"] = 1e8 + 1;
            rows.Add(overCap);
            foreach (var row in validRows) rows.Add(row);
            root["_customIronPillHistory"] = rows;
            File.WriteAllText(path, root.ToString());
            var loaded = new SavedSettings(Path.GetDirectoryName(path));
            Equal(true, loaded.LoadSettings(), "raw loader skips malformed/out-of-range rows independently of Unity");
            Equal(before, History(loaded.CustomIronPillHistory), "row filtering retains ten valid newest records in stored order");
            loaded.SaveSettings();
            root = JsonFiles.ParseObject(File.ReadAllText(path));
            Equal(10, root["_customIronPillHistory"].Count, "explicit save removes invalid raw rows rather than resurrecting source JSON");
            loaded.AddIronPillHistory(Entry(14));
            var newer = new SavedSettings(Path.GetDirectoryName(path));
            Equal(true, newer.LoadSettings(), "append after raw history load reloads");
            Equal(147d, newer.CustomIronPillHistory[0].Worth, "new append supersedes stale source-JSON history");
            Equal(10, newer.CustomIronPillHistory.Length, "append after reload remains bounded at ten");
            var beforeBadLoad = History(loaded.CustomIronPillHistory);
            foreach (var value in new[] {"{}", "42", "\"bad\""})
            {
                File.WriteAllText(path, "{\"_customIronPillSaveSeconds\":99,\"_customIronPillHistory\":" + value + "}");
                Equal(false, loaded.LoadSettings(), "malformed history container refuses reload");
                Equal(beforeBadLoad, History(loaded.CustomIronPillHistory), "bad container does not replace live history");
                Equal(1234, loaded.CustomIronPillSaveSeconds, "bad container does not partially publish other settings");
            }
            File.WriteAllText(path, "{\"_customIronPillHistory\":[{\"Worth\":1e999}]}");
            Equal(false, loaded.LoadSettings(), "non-finite raw JSON refuses reload");
            Equal(beforeBadLoad, History(loaded.CustomIronPillHistory), "non-finite raw JSON leaves history unchanged");
            File.WriteAllText(path, "{\"_customIronPillHistory\":null}");
            Equal(true, loaded.LoadSettings(), "legacy null history loads as empty");
            Equal(0, loaded.CustomIronPillHistory.Length, "null history explicitly clears to an empty backend array");
        }
    }
}
