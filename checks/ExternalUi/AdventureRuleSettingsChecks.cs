using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleJSON;
using UnityEngine;

namespace NGUInjector
{
    internal static class AdventureRuleSettingsChecks
    {
        private const string Record = "{\"Enabled\":true,\"MinPower\":12.5,\"MinToughness\":25.25,\"MinRegen\":0.125,\"Mode\":4,\"Zone\":1000}";
        private const string Field = "_customAdventureRules";
        private static int _assertions;

        private static void Equal<T>(T expected, T actual, string name)
        {
            _assertions++;
            if (!Equals(expected, actual)) throw new Exception(name + ": expected " + expected + ", got " + actual);
        }

        private static void Reject(Action action, string name)
        {
            try { action(); }
            catch (FormatException) { _assertions++; return; }
            throw new Exception("Expected FormatException: " + name);
        }

        private static JSONNode Node(string text)
        {
            return JsonFiles.ParseObject("{\"value\":" + text + "}")["value"];
        }

        private static JSONArray Rows(JSONNode row)
        {
            var rows = new JSONArray();
            rows.Add(row);
            return rows;
        }

        private static string Rules(SavedSettings settings)
        {
            return AdventureRule.ToJson(settings.CustomAdventureRules).ToString();
        }

        private static JSONObject Patch(JSONNode rules)
        {
            var patch = new JSONObject();
            patch["_autoRebirth"] = true;
            patch[Field] = rules;
            return patch;
        }

        internal static int RunOnly()
        {
            var directory = Path.Combine(Path.GetTempPath(), "opencode", "ngu-adventure-rules-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Run(directory);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
            finally { Directory.Delete(directory, true); }
        }

        internal static int Run(string parent)
        {
            var previousOmit = JsonUtility.OmitCustomAdventureRules;
            var previousMissing = JsonUtility.ZeroMissingSerializedFields;
            try
            {
                CheckModel();
                JsonUtility.ZeroMissingSerializedFields = true;
                foreach (var omit in new[] {false, true})
                {
                    JsonUtility.OmitCustomAdventureRules = omit;
                    CheckPersistence(parent, omit);
                }
                Console.WriteLine("PASS: adventure rule settings: " + _assertions + " assertions.");
                return _assertions;
            }
            finally
            {
                JsonUtility.OmitCustomAdventureRules = previousOmit;
                JsonUtility.ZeroMissingSerializedFields = previousMissing;
            }
        }

        private static void CheckModel()
        {
            Equal(0, AdventureRule.Parse(null).Length, "absent rule node defaults empty");
            Equal(0, AdventureRule.Parse(new JSONObject()[Field]).Length, "missing JSON field defaults empty");
            Equal(0, AdventureRule.Parse(JSONNull.CreateOrGet()).Length, "legacy null rule node defaults empty");
            Equal("[]", AdventureRule.ToJson(null).ToString(), "null backend array serializes empty");
            Equal("[]", AdventureRule.ToJson(new AdventureRule[0]).ToString(), "empty rules serialize as an array");
            var rules = AdventureRule.Parse(Rows(Node(Record)));
            Equal("[" + Record + "]", AdventureRule.ToJson(rules).ToString(), "exact six-field record round trip");
            var rule = rules[0];
            Equal(true, rule.Matches(12.5, 25.25, 0.125), "matching minimums are inclusive");
            Equal(true, rule.Matches(13, 26, 1), "all values above minimums match");
            Equal(false, rule.Matches(12, 100, 100), "power must satisfy its minimum");
            Equal(false, rule.Matches(100, 25, 100), "toughness must satisfy its minimum");
            Equal(false, rule.Matches(100, 100, 0.1), "regen must satisfy its minimum");
            Equal(false, rule.Matches(double.NaN, 100, 100), "NaN power cannot match");
            Equal(false, rule.Matches(100, double.NaN, 100), "NaN toughness cannot match");
            Equal(false, rule.Matches(100, 100, double.NaN), "NaN regen cannot match");
            rule.Enabled = false;
            Equal(false, rule.Matches(double.MaxValue, double.MaxValue, double.MaxValue), "disabled rule never matches");

            foreach (var mode in new[] {0, 1, 2, 4})
            foreach (var zone in Enumerable.Range(-1, 45).Concat(new[] {1000}))
            {
                var row = Node(Record);
                row["Mode"] = mode;
                row["Zone"] = zone;
                var parsed = AdventureRule.Parse(Rows(row))[0];
                Equal(mode, parsed.Mode, "every supported mode accepted");
                Equal(zone, parsed.Zone, "every zone in the specified inclusive range accepted");
            }
            foreach (var value in new[] {0d, double.Epsilon, double.MaxValue})
            {
                var row = Node(Record);
                row["MinPower"] = value;
                row["MinToughness"] = value;
                row["MinRegen"] = value;
                var parsed = AdventureRule.Parse(AdventureRule.ToJson(AdventureRule.Parse(Rows(row))))[0];
                Equal(value, parsed.MinPower, "finite power boundary round trip");
                Equal(value, parsed.MinToughness, "finite toughness boundary round trip");
                Equal(value, parsed.MinRegen, "finite regen boundary round trip");
            }
            foreach (var token in new[] {"4.0", "40e-1", "0.04e2"})
            {
                var row = Node(Record);
                row["Mode"] = Node(token);
                Equal(4, AdventureRule.Parse(Rows(row))[0].Mode, "mathematically integral JSON accepted: " + token);
            }
            foreach (var invalid in InvalidArrays())
                Reject(() => AdventureRule.Parse(invalid), "invalid parsed rules " + invalid);
            Reject(() => AdventureRule.ToJson(new AdventureRule[33]), "ToJson rejects more than 32 rules");
            Reject(() => AdventureRule.ToJson(new AdventureRule[] {null}), "ToJson rejects null records");
            foreach (var key in new[] {"MinPower", "MinToughness", "MinRegen", "Mode", "Zone"})
            {
                var invalid = AdventureRule.Parse(Rows(Node(Record)))[0];
                typeof(AdventureRule).GetField(key).SetValue(invalid,
                    key == "Mode" ? (object) 3 : key == "Zone" ? (object) 44 : double.NaN);
                Reject(() => AdventureRule.ToJson(new[] {invalid}), "ToJson validates live field " + key);
            }
        }

        private static IEnumerable<JSONNode> InvalidArrays()
        {
            foreach (var container in new[] {"{}", "true", "false", "42", "\"rules\""})
                yield return Node(container);
            foreach (var row in new[] {"null", "true", "1", "[]", "{}"})
                yield return Rows(Node(row));
            var tooMany = new JSONArray();
            for (var i = 0; i < 33; i++) tooMany.Add(Node(Record));
            yield return tooMany;
            var extra = Node(Record);
            extra["Extra"] = 1;
            yield return Rows(extra);
            foreach (var key in new[] {"Enabled", "MinPower", "MinToughness", "MinRegen", "Mode", "Zone"})
            {
                var missing = Node(Record);
                missing.Remove(key);
                yield return Rows(missing);
                var renamed = Node(Record);
                renamed.Remove(key);
                renamed[key.ToLowerInvariant()] = 1;
                yield return Rows(renamed);
                foreach (var wrongType in new[] {"null", "{}", "[]", "\"1\""}
                    .Concat(key == "Enabled" ? new[] {"0", "1", "1.5"} : new[] {"true", "false"}))
                {
                    var row = Node(Record);
                    row[key] = Node(wrongType);
                    yield return Rows(row);
                }
                if (key == "Enabled") continue;
                foreach (var value in new[] {double.NaN, double.PositiveInfinity, double.NegativeInfinity})
                {
                    var row = Node(Record);
                    row[key] = new JSONNumber(value);
                    yield return Rows(row);
                }
                var invalidTokens = key == "Mode"
                    ? new[] {"-1", "3", "5", "1.5", "2147483648", "4.00000000000000000001", "1e-999"}
                    : key == "Zone"
                        ? new[] {"-2", "44", "999", "1001", "1.5", "-2147483649", "1000.00000000000000000001", "1e-999"}
                        : new[] {"-1", "-0.001"};
                foreach (var token in invalidTokens)
                {
                    var row = Node(Record);
                    row[key] = Node(token);
                    yield return Rows(row);
                }
            }
            var mixed = Rows(Node(Record));
            mixed.Add(new JSONObject());
            yield return mixed;
        }

        private static void CheckPersistence(string parent, bool omit)
        {
            var directory = Path.Combine(parent, "adventure-rules-" + omit);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            var legacy = "{\"futureSetting\":{\"precise\":9223372036854775807,\"items\":[null,true,\"keep\"]},"
                + "\"_energyNguWeights\":[0,0,0,0,1,0,1,0,0],\"_magicNguWeights\":[1,0,0,0,0,0,0]}";
            File.WriteAllText(path, legacy);
            var settings = new SavedSettings(directory);
            Equal(0, settings.CustomAdventureRules.Length, "new settings have no example rules");
            Equal(0, JsonUtility.FromJson<SavedSettings>("{}").CustomAdventureRules.Length, "getter tolerates Unity null default");
            Equal(true, settings.LoadSettings(), "older settings without rules load");
            Equal("[]", Rules(settings), "older settings do not activate rules");
            Equal(legacy, File.ReadAllText(path), "legacy rule defaults with persisted global NGU ratios do not rewrite disk");
            settings.SaveSettings();
            Equal("[]", JsonFiles.ParseObject(File.ReadAllText(path))[Field].ToString(), "save explicitly writes an empty array");

            settings.QuickLoadout = new[] {40, 41};
            settings.ApplyPatch(Patch(Rows(Node(Record))));
            var expected = "[" + Record + "]";
            Equal(expected, Rules(settings), "structured patch publishes exact rules");
            Equal(omit, !JsonFiles.ParseObject(JsonUtility.ToJson(settings)).Linq.Any(item => item.Key == Field), "Unity omission fixture is active only when requested");
            var saved = JsonFiles.ParseObject(File.ReadAllText(path));
            Equal(expected, saved[Field].ToString(), "patch persists records independently of Unity");
            Equal("[40,41]", saved["_quickLoadout"].ToString(), "patch merges unsaved live fields");
            Equal(JsonFiles.ParseObject(legacy)["futureSetting"].ToString(), saved["futureSetting"].ToString(), "patch preserves unknown fields and exact numbers");
            var loaded = new SavedSettings(directory);
            Equal(true, loaded.LoadSettings(), "manual rules reload succeeds without Unity deserialization");
            Equal(expected, Rules(loaded), "all fields survive independent patch reload");
            Equal(true, loaded.CustomAdventureRules[0].Matches(12.5, 25.25, 0.125), "persisted rules use production matcher");

            var copy = new SavedSettings(null);
            copy.MassUpdate(loaded);
            Equal(expected, Rules(copy), "MassUpdate copies all rule fields");
            copy.CustomAdventureRules[0].MinPower = 999;
            Equal(expected, Rules(loaded), "MassUpdate does not alias mutable records");
            copy.CustomAdventureRules[0] = null;
            Equal(expected, Rules(loaded), "MassUpdate does not alias the array");

            // A loaded source JSON must not resurrect old records after newer patches or native saves.
            loaded.ApplyPatch(Patch(new JSONArray()));
            loaded.SaveSettings();
            Equal("[]", JsonFiles.ParseObject(File.ReadAllText(path))[Field].ToString(), "clearing rules overrides stale source JSON");
            loaded.MassUpdate(settings);
            loaded.CustomAdventureRules[0].MinPower = 77;
            expected = Rules(loaded);
            loaded.ApplyPatch(JsonFiles.ParseObject("{\"_combatEnabled\":true}"));
            Equal(expected, Rules(loaded), "unrelated patch retains unsaved live rules");
            Equal(expected, JsonFiles.ParseObject(File.ReadAllText(path))[Field].ToString(), "unrelated patch persists live rules, not stale disk or Unity");
            loaded.SaveSettings();
            var roundTrip = new SavedSettings(directory);
            Equal(true, roundTrip.LoadSettings(), "native SaveSettings round trip loads");
            Equal(expected, Rules(roundTrip), "native SaveSettings persists every field");

            var bounds = Node(Record);
            bounds["MinPower"] = double.MaxValue;
            bounds["MinToughness"] = double.Epsilon;
            bounds["MinRegen"] = 0;
            loaded.ApplyPatch(Patch(Rows(bounds)));
            roundTrip = new SavedSettings(directory);
            Equal(true, roundTrip.LoadSettings(), "numeric boundaries survive actual file serialization");
            Equal(double.MaxValue, roundTrip.CustomAdventureRules[0].MinPower, "maximum finite power persists exactly");
            Equal(double.Epsilon, roundTrip.CustomAdventureRules[0].MinToughness, "smallest positive toughness persists exactly");
            Equal(0d, roundTrip.CustomAdventureRules[0].MinRegen, "zero regen persists exactly");

            var allRules = new JSONArray();
            for (var i = 0; i < 32; i++)
            {
                var row = Node(Record);
                row["Enabled"] = i % 2 == 0;
                row["MinPower"] = i;
                row["Mode"] = new[] {0, 1, 2, 4}[i % 4];
                row["Zone"] = i - 1;
                allRules.Add(row);
            }
            loaded.ApplyPatch(Patch(allRules));
            roundTrip = new SavedSettings(directory);
            Equal(true, roundTrip.LoadSettings(), "32-rule limit is inclusive through persistence");
            Equal(allRules.ToString(), Rules(roundTrip), "all 32 records retain order and disabled flags");
            loaded.ApplyPatch(JsonFiles.ParseObject("{\"_autoRebirth\":false}"));
            var beforeLive = JsonUtility.ToJson(loaded);
            var beforeRules = Rules(loaded);
            var beforeArray = loaded.CustomAdventureRules;
            var beforeBytes = File.ReadAllBytes(path);
            foreach (var invalid in InvalidArrays().Concat(new[] {JSONNull.CreateOrGet()}))
            {
                Reject(() => loaded.ApplyPatch(Patch(invalid)), "invalid structured patch " + invalid);
                Equal(beforeLive, JsonUtility.ToJson(loaded), "invalid patch leaves ordinary live settings unchanged");
                Equal(beforeRules, Rules(loaded), "invalid patch leaves live rules unchanged");
                Equal(true, ReferenceEquals(beforeArray, loaded.CustomAdventureRules), "invalid patch does not replace live array");
                Equal(true, beforeBytes.SequenceEqual(File.ReadAllBytes(path)), "invalid patch preserves exact disk bytes");
                Equal(false, loaded.HasSettingsFileChanged(), "invalid patch retains watcher baseline");
            }
            Reject(() => loaded.ApplyPatch(JsonFiles.ParseObject("{\"_customAdventureRules\":[],\"_unknownField\":1}")), "valid rules followed by invalid ordinary field reject atomically");
            Equal(beforeRules, Rules(loaded), "later invalid field cannot partially clear rules");

            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var rejected = false;
                try { loaded.ApplyPatch(Patch(Rows(Node(Record)))); }
                catch (IOException) { rejected = true; }
                Equal(true, rejected, "failed atomic save reports IO error");
                Equal(beforeLive, JsonUtility.ToJson(loaded), "failed save never publishes ordinary candidate fields");
                Equal(beforeRules, Rules(loaded), "failed save never publishes candidate rules");
                Equal(true, ReferenceEquals(beforeArray, loaded.CustomAdventureRules), "failed save retains live array identity");
            }
            Equal(true, beforeBytes.SequenceEqual(File.ReadAllBytes(path)), "failed save preserves disk bytes");
            Equal(false, loaded.HasSettingsFileChanged(), "failed save retains watcher baseline");
            Equal(0, Directory.GetFiles(directory, "*.tmp").Length, "failed save cleans temporary file");
            loaded.ApplyPatch(Patch(Rows(Node(Record))));
            Equal("[" + Record + "]", Rules(loaded), "valid patch can retry after failed save");
            beforeLive = JsonUtility.ToJson(loaded);
            beforeRules = Rules(loaded);
            foreach (var invalid in InvalidArrays())
            {
                var raw = Patch(invalid);
                raw["_autoRebirth"] = false;
                File.WriteAllText(path, raw.ToString());
                Equal(false, loaded.LoadSettings(), "invalid raw rules reject reload " + invalid);
                Equal(beforeLive, JsonUtility.ToJson(loaded), "invalid reload leaves ordinary live settings unchanged");
                Equal(beforeRules, Rules(loaded), "invalid reload leaves live rules unchanged");
            }
            foreach (var oldSettings in new[] {legacy, "{\"_customAdventureRules\":null}"})
            {
                File.WriteAllText(path, oldSettings);
                Equal(true, loaded.LoadSettings(), "absent or legacy null array reloads");
                Equal("[]", Rules(loaded), "legacy reload clears previously populated rules");
            }
            loaded.MassUpdate(settings);
            loaded.MassUpdate(JsonUtility.FromJson<SavedSettings>("{\"_energyNguWeights\":[0,0,0,0,1,0,1,0,0],\"_magicNguWeights\":[1,0,0,0,0,0,0]}"));
            Equal("[]", Rules(loaded), "MassUpdate from missing Unity field clears existing rules");
        }
    }
}
