using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    internal static class Program
    {
        private static int _assertions;
        private static string _root;
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL: " + message);
            _assertions++;
        }
        private static void Reject<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { _assertions++; return; }
            throw new Exception("FAIL (accepted): " + message);
        }
        private static string DirectoryFor(string name)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            return path;
        }
        private static string Revision(JSONObject root) { return root[CustomPlanStore.ExternalUiRevisionField].Value; }
        private static string ImportedStageId(JSONObject library, string source)
        {
            return library["MigrationReceipts"].Children.Single(row => row["Source"].Value == source && row["Kind"].Value == "stage")["PresetId"].Value;
        }
        private static CustomPlan Plan(int goal = 4)
        {
            return new CustomPlan
            {
                GoalPresets = new[] {new CustomGoalLoadoutPreset {Name = "Chosen", GearGoal = goal}},
                Stages = new[] {new CustomPlanStage {Name = "NGU", GearGoal = 4, EnergyFocus = 5, MagicFocus = 6}},
                StageLoopStartIndex = -1
            };
        }
        private static JSONObject Legacy(int goal = 4)
        {
            return JsonFiles.ParseObject("{\"Version\":17,\"EnergyNguWeights\":[0,0,0,0,7,0,3,0,0],\"MagicNguWeights\":[0,2,0,0,0,0,5],"
                + "\"GoalPresets\":[{\"Name\":\"Legacy\",\"GearGoal\":" + goal + ",\"futureGoal\":9007199254740993}],"
                + "\"Stages\":[{\"Name\":\"NGU\",\"EnergyFocus\":5,\"MagicFocus\":6,\"GearGoal\":4,\"futureStage\":1.234567890123456789}],"
                + "\"LoadoutPresets\":[{\"Id\":\"same\",\"Name\":\"Source\",\"Weights\":[1,1,0,0,0,0],\"futurePreset\":1.2300e+004}],"
                + "\"futureRoot\":9223372036854775807}");
        }
        private static JSONArray Without(JSONNode rows, string id)
        {
            var result = new JSONArray();
            foreach (var row in rows.Children.Where(row => row["Id"].Value != id)) result.Add(row);
            return result;
        }

        private static bool SamePreset(JSONNode actual, JSONNode expected)
        {
            return actual.Count == expected.Count && expected.Linq.All(field => actual[field.Key].ToString() == field.Value.ToString());
        }

        private static JSONObject LibraryRoot(int count)
        {
            var root = new JSONObject(); root["Version"] = 1; root["Presets"] = new JSONArray();
            for (var i = 0; i < count; i++) root["Presets"].Add(new CustomLoadoutPreset {Id = "limit_" + i, Name = "Preset"}.ToJson());
            return root;
        }

        private static void RejectLimit(Action action, string limit, string label)
        {
            try { action(); }
            catch (FormatException error)
            {
                Assert(error.Message.Contains(limit) && error.Message.Contains("not modified"), label + " reports the limit and preserves data");
                return;
            }
            throw new Exception("FAIL (accepted): " + label);
        }

        private static void SeedOnce()
        {
            var runtime = DirectoryFor("seed");
            var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            var library = new PresetLibraryStore(runtime, plans);
            Assert(!File.Exists(Path.Combine(runtime, "presets.json")), "library construction is lazy");
            var rows = library.List();
            Assert(rows.Length == 17, "seed contains eleven fixed specialists, adventure-dynamic and five composites");
            foreach (var i in new[] {0, 1, 2, 6, 7, 8, 9, 10, 11, 12, 13})
            {
                var row = library.Get("builtin_" + PresetDefinitions.ObjectiveKeys[i]);
                Assert(row.Weights.Length == 14 && row.Weights[i] == 1 && row.Weights.Sum() == 1, "one-hot objective " + i);
            }
            var adventure = library.Get("builtin_adventureDynamic");
            Assert(adventure.Name == "adventure-dynamic" && adventure.AdventureDynamicWeight == 1 && adventure.Weights.SequenceEqual(new double[14]), "dynamic seed round trips through the library");
            Assert(library.Get("builtin_adventurePower").Weights[10] == 1 && library.Get("builtin_adventureToughness").Weights[11] == 1, "adventure specialist IDs use appended objective indices");
            Assert(library.Get("builtin_adventureStats").Weights.SequenceEqual(new double[] {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0}), "Adventure Stats weights only power and toughness");
            Assert(library.Get("builtin_balanced") == null, "fresh libraries no longer seed Balanced");
            Assert(library.Get("builtin_atAdventure").AdvancedTrainingWeights.SequenceEqual(new[] {1, 1, 0, 0, 0}), "AT Adventure targets adventure pair");
            Assert(library.Get("builtin_atWandoos").AdvancedTrainingWeights.SequenceEqual(new[] {0, 0, 0, 1, 1}), "AT Wandoos targets Wandoos pair");
            Assert(rows.All(row => row.AutoUpdate && !row.ToJson().Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")), "seed uses ratio-free schema defaults");
            Assert(rows.All(row => row.SupportMode == (row.Id == "builtin_energyCap" || row.Id == "builtin_magicCap" ? "current" : "planned")), "only cap seeds keep current support");
            var read = library.ReadExternalLibrary();
            Assert(read["CapPresetsAdded"].IsBoolean && read["CapPresetsAdded"].AsBool, "fresh seeds durably mark the cap addition applied");
            var definitions = PresetDefinitions.SeedRows();
            for (var i = 0; i < rows.Length; i++)
                foreach (var field in definitions[i])
                    Assert(read["Presets"][i][field.Key].ToString() == field.Value.ToString(), "persisted seed matches shared definition " + i + "/" + field.Key);
            read["Presets"][0]["SupportMode"] = "current";
            read["Presets"][0]["BeardPriority"] = new JSONArray();
            read["Presets"][0].Remove("BeardRotation");
            library.SaveExternalLibrary(read, Revision(read));
            Assert(new PresetLibraryStore(runtime, plans).List()[0].SupportMode == "current", "restart never replaces a saved current choice on a built-in");
            var restarted = new PresetLibraryStore(runtime, plans).ReadExternalLibrary()["Presets"][0];
            Assert(restarted["BeardPriority"].Count == 0 && !restarted.Linq.Any(field => field.Key == "BeardRotation"), "existing seed IDs never reacquire new beard defaults after restart");
            read = library.ReadExternalLibrary(); read["Presets"] = new JSONArray(); read.Remove("CapPresetsAdded");
            library.SaveExternalLibrary(read, Revision(read));
            var bytes = File.ReadAllBytes(Path.Combine(runtime, "presets.json"));
            Assert(new PresetLibraryStore(runtime, plans).List().Length == 0, "legitimate empty library never reseeded");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(runtime, "presets.json"))), "empty read does not rewrite file");
        }

        private static void LosslessCas()
        {
            var runtime = DirectoryFor("cas"); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            var path = Path.Combine(runtime, "presets.json");
            File.WriteAllText(path, "{\"Version\":1,\"Presets\":[{\"Id\":\"custom\",\"Name\":\"Custom\",\"future\":{\"integer\":9223372036854775807,\"decimal\":1.234567890123456789,\"exponent\":1.2300e+004}}],\"extension\":9007199254740993}", new UTF8Encoding(true));
            var library = new PresetLibraryStore(runtime, plans);
            var bytes = File.ReadAllBytes(path); var read = library.ReadExternalLibrary();
            Assert(Revision(read) == JsonFiles.ComputeRevision(bytes), "revision hashes exact BOM-containing file bytes");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "read does not write normalized library");
            Assert(read["Presets"][0]["SupportMode"].Value == "planned", "missing library mode normalizes to optimized support without writing defaults");
            var future = read["Presets"][0]["future"].ToString();
            read["Presets"][0]["Name"] = "Edited";
            var revision = library.SaveExternalLibrary(read, Revision(read));
            Assert(revision == JsonFiles.ComputeRevision(File.ReadAllBytes(path)), "save returns exact disk revision");
            var saved = JsonFiles.ParseObject(File.ReadAllText(path));
            Assert(saved["Presets"][0]["future"].ToString() == future && saved["extension"].Value == "9007199254740993", "unknown nested numeric tokens survive save");
            Assert(!saved.Linq.Any(row => row.Key == CustomPlanStore.ExternalUiRevisionField), "transport revision is never persisted");
            Assert(saved["Presets"][0]["Weights"].Count == 14, "global serializer pads fourteen objectives");
            Assert(!saved["Presets"][0].Linq.Any(row => row.Key == "HorizonSeconds"), "default horizon is never persisted");
            Assert(saved["Presets"][0]["SupportMode"].Value == "planned", "saving a missing-mode library persists optimized support");
            Reject<InvalidOperationException>(() => library.SaveExternalLibrary(read, Revision(read)), "stale CAS rejected");
            read = library.ReadExternalLibrary(); bytes = File.ReadAllBytes(path); File.AppendAllText(path, "\n");
            Reject<InvalidOperationException>(() => library.SaveExternalLibrary(read, Revision(read)), "whitespace-only external change conflicts");
            Assert(File.ReadAllBytes(path).Length == bytes.Length + 1, "conflict leaves external changes intact");
            foreach (var token in new[] {null, "", "short", new string('g', 64)})
                Reject<FormatException>(() => library.SaveExternalLibrary(read, token), "invalid revision rejected");
            foreach (var invalid in new[] {"{\"Version\":2,\"Presets\":[]}", "{\"Version\":1,\"Presets\":null}", "{\"Version\":1,\"Presets\":[{\"Id\":\"x\",\"Name\":\"x\"},{\"Id\":\"x\",\"Name\":\"x\"}]}"})
                Reject<FormatException>(() => library.SaveExternalLibrary(JsonFiles.ParseObject(invalid), Revision(library.ReadExternalLibrary())), "invalid library rejected");
        }

        private static void CapCatalogAddition()
        {
            foreach (var existingCaps in new[] {0, 1, 2})
            {
                var runtime = DirectoryFor("cap-addition-" + existingCaps); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var root = LibraryRoot(1); root["MigrationReceipts"] = JsonFiles.ParseObject("{\"historical\":{\"PresetId\":\"deleted\",\"future\":9007199254740993}}");
                root["futureRoot"] = JsonFiles.ParseObject("{\"value\":1.2300e+004}")["value"];
                var seeds = PresetDefinitions.SeedRows();
                foreach (var seed in seeds.Children.Take(15 + existingCaps))
                {
                    var row = JsonFiles.ParseObject(seed.ToString());
                    foreach (var field in new[] {"ResourcePrioritiesReviewed", "EnergyPriorities", "MagicPriorities"}) row.Remove(field);
                    if (row["Weights"].Children.Skip(12).All(weight => weight.AsDouble == 0))
                    {
                        var weights = new JSONArray(); foreach (var weight in row["Weights"].Children.Take(12)) weights.Add(weight);
                        row["Weights"] = weights;
                    }
                    row["Name"] = "Edited " + row["Name"].Value; row["AutoUpdate"] = false; row["SupportMode"] = "planned";
                    row["BeardPriority"] = new JSONArray(); row.Remove("BeardRotation");
                    row["futurePreset"] = JsonFiles.ParseObject("{\"value\":9223372036854775807}")["value"];
                    root["Presets"].Add(row);
                }
                var expected = root["Presets"].Children.Select(row => CustomLoadoutPreset.Parse(row).ToJson()).ToArray();
                var receipts = root["MigrationReceipts"].ToString();
                var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, root.ToString(4));
                var library = new PresetLibraryStore(runtime, plans); var rows = library.List(); var read = library.ReadExternalLibrary();
                Assert(rows.Length == 18 && rows.Select(row => row.Id).Distinct().Count() == rows.Length, "cap addition appends only missing IDs: " + existingCaps);
                Assert(expected.All(row => SamePreset(read["Presets"].Children.Single(saved => saved["Id"].Value == row["Id"].Value), row)),
                    "cap addition preserves every existing row, ID, edit and unknown token: " + existingCaps);
                Assert(read["CapPresetsAdded"].AsBool && read["MigrationReceipts"].ToString() == receipts && read["futureRoot"].Value == "1.2300e+004",
                    "addition preserves receipts and root extensions and records durable application");
                foreach (var seed in seeds.Children.Skip(15 + existingCaps))
                    Assert(SamePreset(read["Presets"].Children.Single(row => row["Id"].Value == seed["Id"].Value), CustomLoadoutPreset.Parse(seed).ToJson()),
                        "missing cap receives the shared current-support auto-update definition");
                var bytes = File.ReadAllBytes(path);
                Assert(new PresetLibraryStore(runtime, plans).List().Length == 18 && bytes.SequenceEqual(File.ReadAllBytes(path)), "upgrade restart performs no write");

                read["Presets"] = Without(read["Presets"], "builtin_magicCap"); read.Remove("CapPresetsAdded"); read.Remove("MigrationReceipts");
                var energy = read["Presets"].Children.Single(row => row["Id"].Value == "builtin_energyCap");
                energy["Name"] = "My energy cap"; energy["Weights"][12] = 7; energy["AutoUpdate"] = false; energy["SupportMode"] = "planned";
                library.SaveExternalLibrary(read, Revision(read)); bytes = File.ReadAllBytes(path);
                library = new PresetLibraryStore(runtime, plans); read = library.ReadExternalLibrary();
                Assert(library.Get("builtin_magicCap") == null && SamePreset(library.Get("builtin_energyCap").ToJson(), energy)
                    && bytes.SequenceEqual(File.ReadAllBytes(path)), "cap edits and deletion survive read and restart without rewriting");
                Assert(read["CapPresetsAdded"].AsBool && read["MigrationReceipts"].ToString() == receipts, "external omission cannot remove injector-owned migration state");

                read["Presets"] = Without(read["Presets"], "builtin_energyCap"); read["CapPresetsAdded"] = false;
                library.SaveExternalLibrary(read, Revision(read)); bytes = File.ReadAllBytes(path);
                library = new PresetLibraryStore(runtime, plans);
                Assert(library.List().Length == 16 && library.Get("builtin_energyCap") == null && library.Get("builtin_magicCap") == null
                    && bytes.SequenceEqual(File.ReadAllBytes(path)), "deleting both caps while old built-ins remain never re-adds them");
                read = library.ReadExternalLibrary(); read["Presets"] = new JSONArray(); read["CapPresetsAdded"] = new JSONObject();
                library.SaveExternalLibrary(read, Revision(read)); bytes = File.ReadAllBytes(path);
                Assert(new PresetLibraryStore(runtime, plans).List().Length == 0 && bytes.SequenceEqual(File.ReadAllBytes(path)), "deleting the entire upgraded catalog stays empty despite marker tampering");
            }

            foreach (var count in new[] {0, 1})
            {
                var runtime = DirectoryFor("cap-ineligible-" + count); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var root = LibraryRoot(count);
                if (count != 0) root["Presets"][0]["Id"] = "builtin_user_defined";
                var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, root.ToString()); var bytes = File.ReadAllBytes(path);
                var library = new PresetLibraryStore(runtime, plans); var read = library.ReadExternalLibrary();
                Assert(library.List().Length == count && !read.Linq.Any(field => field.Key == "CapPresetsAdded") && bytes.SequenceEqual(File.ReadAllBytes(path)),
                    "unmarked empty and custom-only libraries remain unchanged, including unknown builtin-prefixed IDs");
                read["CapPresetsAdded"] = true; library.SaveExternalLibrary(read, Revision(read));
                Assert(!library.ReadExternalLibrary().Linq.Any(field => field.Key == "CapPresetsAdded"), "external saves cannot forge a catalog-addition marker");
            }
        }

        private static void ResourcePriorityCatalogUpgrade()
        {
            var priorityFields = new[] {"ResourcePrioritiesReviewed", "EnergyPriorities", "MagicPriorities"};
            foreach (var full in new[] {false, true})
            foreach (var explicitBeards in new[] {false, true})
            foreach (var width in new[] {6, 10, 12, 14})
            {
                var runtime = DirectoryFor("priority-upgrade-" + full + "-" + explicitBeards + "-" + width);
                var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var root = LibraryRoot(0); root["CapPresetsAdded"] = true;
                root["MigrationReceipts"] = JsonFiles.ParseObject("{\"historical-hash\":{\"PresetId\":\"deleted_import\",\"Source\":\"legacy\",\"Kind\":\"loadout:old\",\"future\":9007199254740993}}");
                root["futureRoot"] = JsonFiles.ParseObject("{\"decimal\":1.234567890123456789,\"exponent\":1.2300e+004}");
                foreach (var row in PresetDefinitionChecks.BeforeResourcePriorityRows(full, explicitBeards).Children)
                {
                    if (row["Weights"].Children.Skip(width).Any(weight => weight.AsDouble > 0)) continue;
                    var weights = new JSONArray(); foreach (var weight in row["Weights"].Children.Take(width)) weights.Add(weight);
                    while (weights.Count < width) weights.Add(0);
                    row["Weights"] = weights; root["Presets"].Add(row);
                }
                var expected = root["Presets"].Children.Select(row => CustomLoadoutPreset.Parse(row).ToJson()).ToArray();
                var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, root.ToString(4));
                var library = new PresetLibraryStore(runtime, plans); var read = library.ReadExternalLibrary();
                Assert(read["Presets"].Children.Select(row => row["Id"].Value).SequenceEqual(expected.Select(row => row["Id"].Value)),
                    "priority upgrade iterates saved rows only, preserving order and deleted IDs");
                foreach (var prior in expected)
                {
                    var row = read["Presets"].Children.Single(saved => saved["Id"].Value == prior["Id"].Value);
                    var current = PresetDefinitions.SeedRows().Children.Single(seed => seed["Id"].Value == prior["Id"].Value);
                    Assert(row.Count == prior.Count + 3 && priorityFields.All(field => row[field].ToString() == current[field].ToString()),
                        "eligible old definition gains exactly three reviewed priority fields: " + prior["Id"].Value);
                    Assert(prior.Linq.All(field => row[field.Key].ToString() == field.Value.ToString()),
                        "priority upgrade retains every normalized prior setup field: " + prior["Id"].Value);
                }
                var persisted = JsonFiles.ParseObject(File.ReadAllText(path));
                Assert(persisted.Count == root.Count && persisted["MigrationReceipts"].ToString() == root["MigrationReceipts"].ToString()
                    && persisted["futureRoot"].ToString() == root["futureRoot"].ToString(), "priority upgrade preserves receipt identities and root tokens without a new marker");
                var bytes = File.ReadAllBytes(path);
                Assert(new PresetLibraryStore(runtime, plans).List().Length == expected.Length && bytes.SequenceEqual(File.ReadAllBytes(path)),
                    "reviewed priority upgrade is idempotent and restart does not write");
                library.SaveExternalLibrary(read, Revision(read)); bytes = File.ReadAllBytes(path);
                read = new PresetLibraryStore(runtime, plans).ReadExternalLibrary();
                Assert(read["MigrationReceipts"].ToString() == root["MigrationReceipts"].ToString() && bytes.SequenceEqual(File.ReadAllBytes(path)),
                    "priority upgrade save and restart leave historical receipts unchanged");
            }
        }

        private static void ResourcePriorityEditsPreserved()
        {
            void CheckUnchanged(JSONArray rows, string label)
            {
                var runtime = DirectoryFor("priority-preserve-" + Guid.NewGuid().ToString("N"));
                var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var root = LibraryRoot(0); root["CapPresetsAdded"] = true; root["Presets"] = rows;
                root["MigrationReceipts"] = JsonFiles.ParseObject("{\"old\":{\"PresetId\":\"deleted\",\"exact\":1.2300e+004}}");
                var expected = rows.Children.Select(row => CustomLoadoutPreset.Parse(row).ToJson()).ToArray();
                var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, root.ToString(4)); var bytes = File.ReadAllBytes(path);
                var library = new PresetLibraryStore(runtime, plans); var read = library.ReadExternalLibrary();
                Assert(read["Presets"].Count == expected.Length && read["Presets"].Children.Zip(expected, SamePreset).All(equal => equal),
                    "migration preserves every normalized row for " + label);
                Assert(read["MigrationReceipts"].ToString() == root["MigrationReceipts"].ToString() && bytes.SequenceEqual(File.ReadAllBytes(path)),
                    "nonmatching or explicit priorities cause no write: " + label);
                Assert(new PresetLibraryStore(runtime, plans).List().Length == expected.Length && bytes.SequenceEqual(File.ReadAllBytes(path)),
                    "nonmatching or explicit priorities remain unchanged after restart: " + label);
            }

            foreach (var full in new[] {false, true})
            foreach (var explicitBeards in new[] {false, true})
            foreach (var field in new[] {"Id", "Name", "Weights", "AdventureDynamicWeight", "AutoUpdate", "AdvancedTrainingWeights", "MagicBloodPercent", "SupportMode",
                "DiggerBudgetPercent", "Zone", "MinimumPower", "MinimumToughness", "SwapSeconds", "BeardPriority", "BeardRotation", "DiggerWeights", "Unknown", "UnknownNull", "energyPriorities"})
            {
                var rows = PresetDefinitionChecks.BeforeResourcePriorityRows(full, explicitBeards);
                foreach (var row in rows.Children)
                    switch (field)
                    {
                        case "Id": row[field] = "copy_" + row[field].Value; break;
                        case "Name": row[field] = "Edited " + row[field].Value; break;
                        case "Weights": row[field][0] = row[field][0].AsDouble + 2; break;
                        case "AdventureDynamicWeight": row[field] = 2.5; break;
                        case "AutoUpdate": row[field] = false; break;
                        case "AdvancedTrainingWeights": row[field] = JSON.Parse("[1,2,3,4,5]"); break;
                        case "MagicBloodPercent": row[field] = 50; break;
                        case "SupportMode": row[field] = row["Id"].Value.EndsWith("Cap") ? "planned" : "current"; break;
                        case "DiggerBudgetPercent": row[field] = 80; break;
                        case "Zone": row[field] = 7; break;
                        case "MinimumPower": case "MinimumToughness": row[field] = 123.5; break;
                        case "SwapSeconds": row[field] = 6; break;
                        case "BeardPriority": row[field] = JSON.Parse(explicitBeards ? "[3,5,6]" : "[1]"); break;
                        case "BeardRotation": row[field] = JSON.Parse(explicitBeards ? "[0,2]" : "[]"); break;
                        case "DiggerWeights": row[field] = JSON.Parse("[1,0,0,0,0,0,0,0,0,0,0,0]"); break;
                        case "Unknown": row[field] = JsonFiles.ParseObject("{\"integer\":9223372036854775807,\"decimal\":1.234567890123456789,\"exponent\":1.2300e+004}"); break;
                        case "UnknownNull": row[field] = JSONNull.CreateOrGet(); break;
                        case "energyPriorities": row[field] = JSON.Parse("[\"NGU\"]"); break;
                    }
                CheckUnchanged(rows, field + "/full=" + full + "/explicitBeards=" + explicitBeards);
            }
            foreach (var field in new[] {"Weights", "AdvancedTrainingWeights"})
            for (var lane = 0; lane < (field == "Weights" ? 14 : 5); lane++)
            {
                var rows = PresetDefinitionChecks.BeforeResourcePriorityRows(true, true);
                foreach (var row in rows.Children) row[field][lane] = row[field][lane].AsDouble + 1;
                CheckUnchanged(rows, field + " lane " + lane);
            }
            foreach (var fields in new[] {
                "{\"ResourcePrioritiesReviewed\":false}",
                "{\"EnergyPriorities\":[],\"MagicPriorities\":[]}",
                "{\"EnergyPriorities\":[],\"MagicPriorities\":[],\"ResourcePrioritiesReviewed\":false}",
                "{\"EnergyPriorities\":[],\"MagicPriorities\":[],\"ResourcePrioritiesReviewed\":true}",
                "{\"EnergyPriorities\":[\"TM\",\"NGU\"],\"MagicPriorities\":[\"RIT\"]}",
                "{\"EnergyPriorities\":[\"TM\",\"NGU\"],\"MagicPriorities\":[\"RIT\"],\"ResourcePrioritiesReviewed\":false}",
                "{\"EnergyPriorities\":[\"TM\",\"NGU\"],\"MagicPriorities\":[\"RIT\"],\"ResourcePrioritiesReviewed\":true}"})
            foreach (var explicitBeards in new[] {false, true})
            {
                var rows = PresetDefinitionChecks.BeforeResourcePriorityRows(true, explicitBeards);
                foreach (var row in rows.Children)
                    foreach (var field in JsonFiles.ParseObject(fields)) row[field.Key] = field.Value;
                CheckUnchanged(rows, "explicit priorities " + fields);
            }
            foreach (var reviewed in new[] {"absent", "false", "true"})
            {
                var rows = PresetDefinitions.SeedRows();
                foreach (var row in rows.Children)
                    if (reviewed == "absent") row.Remove("ResourcePrioritiesReviewed");
                    else row["ResourcePrioritiesReviewed"] = reviewed == "true";
                CheckUnchanged(rows, "explicit catalog-matching arrays with review=" + reviewed);
            }
            var custom = PresetDefinitionChecks.BeforeResourcePriorityRows(true, true);
            foreach (var row in custom.Children) row["Id"] = "builtin_unknown_" + row["Id"].Value;
            CheckUnchanged(custom, "unknown builtin-prefixed IDs");
            custom = PresetDefinitionChecks.BeforeResourcePriorityRows(true, true);
            foreach (var row in custom.Children) row.Remove("BeardRotation");
            CheckUnchanged(custom, "core beards without explicit rotation are not a historical variant");
            foreach (var field in new[] {"BeardPriority", "BeardRotation"})
            {
                custom = PresetDefinitionChecks.BeforeResourcePriorityRows(true, true);
                foreach (var row in custom.Children) row[field] = JSON.Parse(field == "BeardPriority" ? "[3,1,5,6]" : "[4,2,0]");
                CheckUnchanged(custom, "reordered " + field + " is a custom beard choice");
            }

            var runtime = DirectoryFor("priority-deletions"); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            var root = LibraryRoot(0); root["CapPresetsAdded"] = true;
            root["Presets"].Add(PresetDefinitionChecks.BeforeResourcePriorityRows(false, false)[0]);
            root["MigrationReceipts"] = JsonFiles.ParseObject("{\"old\":{\"PresetId\":\"builtin_allNgu\"},\"balanced\":{\"PresetId\":\"builtin_balanced\"}}");
            var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, root.ToString(4));
            var library = new PresetLibraryStore(runtime, plans); var read = library.ReadExternalLibrary();
            Assert(library.List().Length == 1 && library.Get("builtin_allNgu") == null && library.Get("builtin_balanced") == null,
                "priority upgrade never resurrects deleted catalog rows, including Balanced");
            read["Presets"] = new JSONArray(); library.SaveExternalLibrary(read, Revision(read)); var bytes = File.ReadAllBytes(path);
            read = new PresetLibraryStore(runtime, plans).ReadExternalLibrary();
            Assert(read["Presets"].Count == 0 && read["MigrationReceipts"].ToString() == root["MigrationReceipts"].ToString() && bytes.SequenceEqual(File.ReadAllBytes(path)),
                "deleting all upgraded rows preserves deletion and receipt identities without a migration marker");
        }

        private static void SavedBalancedAncestry()
        {
            var runtime = DirectoryFor("saved-balanced"); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            var root = LibraryRoot(0);
            var balanced = JsonFiles.ParseObject("{\"Id\":\"builtin_balanced\",\"Name\":\"Balanced\",\"Weights\":[1,1,1,1,1,1,1,1,1,1,1,1]}");
            root["Presets"].Add(balanced); root["MigrationReceipts"] = JsonFiles.ParseObject("{\"old\":{\"PresetId\":\"deleted\",\"future\":9007199254740993}}");
            var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, root.ToString(4));
            var raw = Legacy(99); raw["ActivePresetId"] = "builtin_balanced"; raw["Stages"][0]["PresetId"] = "@active"; raw["LoadoutPresets"] = new JSONArray();
            var planPath = Path.Combine(plans, "balanced.json"); File.WriteAllText(planPath, raw.ToString(4)); var planBytes = File.ReadAllBytes(planPath);
            var library = new PresetLibraryStore(runtime, plans); var read = library.ReadExternalLibrary();
            Assert(library.List().Select(row => row.Id).SequenceEqual(new[] {"builtin_balanced", "builtin_energyCap", "builtin_magicCap"}) && read["CapPresetsAdded"].AsBool,
                "Balanced-only libraries retain historical cap-add ancestry without reseeding the catalog");
            Assert(library.Get("builtin_balanced").ToJson().ToString() == CustomLoadoutPreset.Parse(balanced).ToJson().ToString()
                && read["MigrationReceipts"].ToString() == root["MigrationReceipts"].ToString(), "saved Balanced remains unchanged, including old goals and no new priority policy");
            var plan = CustomPlanStore.ReadForPresetMigration(raw.ToString(), "balanced"); library.MigratePlan(plan, "balanced"); library.ValidateReferences(plan);
            Assert(plan.ActivePresetId == "builtin_balanced" && plan.Stages[0].PresetId == "@active" && planBytes.SequenceEqual(File.ReadAllBytes(planPath)),
                "saved active and inherited Balanced references stay valid without rewriting the plan");
            read["Presets"] = Without(read["Presets"], "builtin_balanced");
            Reject<InvalidOperationException>(() => library.SaveExternalLibrary(read, Revision(read)), "referenced saved Balanced cannot be deleted");
            var bytes = File.ReadAllBytes(path);
            Assert(new PresetLibraryStore(runtime, plans).List().Length == 3 && bytes.SequenceEqual(File.ReadAllBytes(path)), "saved Balanced and cap ancestry upgrade are idempotent");
        }

        private static void CapAdditionBounds()
        {
            foreach (var count in new[] {1022, 1023, 1024})
            {
                var runtime = DirectoryFor("cap-count-" + count); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var root = LibraryRoot(count); root["Presets"][0]["Id"] = "builtin_energyNgu";
                var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, root.ToString(4)); var bytes = File.ReadAllBytes(path);
                var library = new PresetLibraryStore(runtime, plans);
                if (count == 1022)
                    Assert(library.List().Length == 1024 && library.Get("builtin_magicCap") != null && library.ReadExternalLibrary()["CapPresetsAdded"].AsBool,
                        "cap addition succeeds at the inclusive count limit");
                else
                {
                    foreach (var action in new Action[] {() => library.List(), () => library.ReadExternalLibrary(), () => new PresetLibraryStore(runtime, plans).List()})
                    {
                        RejectLimit(action, "1024", "cap addition exceeding count limit");
                        Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "count failure persists neither partial caps nor an applied marker");
                    }
                    root["Presets"].Remove(count - 1); if (count == 1024) root["Presets"].Remove(count - 2);
                    File.WriteAllText(path, root.ToString(4));
                    Assert(library.List().Length == 1024 && library.Get("builtin_energyCap") != null && library.Get("builtin_magicCap") != null,
                        "same store retries the complete cap addition after space is freed");
                }
                Assert(Directory.GetFiles(runtime, "*.tmp").Length == 0, "count-bound addition leaves no temporary files");
            }
            foreach (var existingCaps in new[] {false, true})
            {
                var runtime = DirectoryFor("cap-bytes-" + existingCaps); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var root = LibraryRoot(0);
                foreach (var seed in PresetDefinitions.SeedRows().Children.Where(row => existingCaps
                    ? row["Id"].Value == "builtin_energyCap" || row["Id"].Value == "builtin_magicCap"
                    : row["Id"].Value == "builtin_energyNgu")) root["Presets"].Add(CustomLoadoutPreset.Parse(seed).ToJson());
                root["futureText"] = ""; root["futureText"] = new string('x', PresetLibraryStore.MaximumFileBytes - Encoding.UTF8.GetByteCount(root.ToString(4)));
                var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, root.ToString(4)); var bytes = File.ReadAllBytes(path);
                var library = new PresetLibraryStore(runtime, plans);
                foreach (var action in new Action[] {() => library.ReadExternalLibrary(), () => new PresetLibraryStore(runtime, plans).List()})
                {
                    RejectLimit(action, "2 MiB", "cap or marker addition exceeding byte limit");
                    Assert(bytes.Length == PresetLibraryStore.MaximumFileBytes && bytes.SequenceEqual(File.ReadAllBytes(path)), "byte failure preserves exact boundary bytes without a partial marker");
                }
                root["futureText"] = root["futureText"].Value.Substring(10000); File.WriteAllText(path, root.ToString(4));
                Assert(library.List().Length == (existingCaps ? 2 : 3) && library.ReadExternalLibrary()["CapPresetsAdded"].AsBool, "cap addition retries successfully after byte capacity is freed");
                Assert(Directory.GetFiles(runtime, "*.tmp").Length == 0, "byte-bound addition leaves no temporary files");
            }
            if (OperatingSystem.IsWindows())
            {
                var runtime = DirectoryFor("cap-write-retry"); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var root = LibraryRoot(1); root["Presets"][0]["Id"] = "builtin_energyNgu";
                var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, root.ToString(4)); var bytes = File.ReadAllBytes(path);
                var library = new PresetLibraryStore(runtime, plans);
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Reject<IOException>(() => library.List(), "blocked cap addition fails atomically");
                Assert(bytes.SequenceEqual(File.ReadAllBytes(path)) && Directory.GetFiles(runtime, "*.tmp").Length == 0, "failed cap replace preserves bytes and cleans temporary files");
                Assert(library.List().Length == 3 && library.ReadExternalLibrary()["CapPresetsAdded"].AsBool, "failed cap replace can retry on the same store");
            }
        }

        private static void SchemaPersistence()
        {
            foreach (var count in new[] {6, 10, 12, 14})
            foreach (var horizon in new[] {"3600", "0", "-1", "604801", "1.5", "null", "\"invalid\"", "true", "[]", "{\"old\":true}"})
            {
                var runtime = DirectoryFor("schema-" + count + "-" + Guid.NewGuid().ToString("N"));
                var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var path = Path.Combine(runtime, "presets.json");
                var weights = Enumerable.Range(1, count).Select(value => (double) value).ToArray();
                var original = JsonFiles.ParseObject("{\"Version\":1,\"Presets\":[{\"Id\":\"legacy\",\"Name\":\"Legacy\",\"Weights\":["
                    + string.Join(",", weights) + "],\"HorizonSeconds\":" + horizon + ",\"future\":{\"keep\":true}}]}");
                File.WriteAllText(path, original.ToString()); var before = File.ReadAllBytes(path);
                var library = new PresetLibraryStore(runtime, plans); var read = library.ReadExternalLibrary();
                var expected = weights.Concat(Enumerable.Repeat(0d, 14 - count)).ToArray();
                Assert(library.Get("legacy").Weights.SequenceEqual(expected), "library normalizes legacy weights without changing existing lanes " + count);
                Assert(!read["Presets"][0].Linq.Any(row => row.Key == "HorizonSeconds"), "invalid legacy horizon ignored on read " + horizon);
                Assert(before.SequenceEqual(File.ReadAllBytes(path)), "schema normalization on read leaves original file intact");
                read["Presets"][0]["HorizonSeconds"] = original["Presets"][0]["HorizonSeconds"];
                library.SaveExternalLibrary(read, Revision(read));
                var saved = JsonFiles.ParseObject(File.ReadAllText(path));
                Assert(saved["Presets"][0]["Weights"].Children.Select(row => row.AsDouble).SequenceEqual(expected), "saved weights padded to fourteen " + count);
                Assert(!saved["Presets"][0].Linq.Any(row => row.Key == "HorizonSeconds") && saved["Presets"][0]["future"]["keep"].AsBool,
                    "save drops reintroduced legacy horizon and preserves unknown data " + horizon);
                Assert(new PresetLibraryStore(runtime, plans).Get("legacy").Weights.SequenceEqual(expected), "normalized schema survives restart");
            }
        }

        private static void MigrationReadOnly()
        {
            var runtime = DirectoryFor("migration"); var store = new CustomPlanStore(Path.Combine(runtime, "plans"));
            var path = Path.Combine(runtime, "plans", "legacy.json");
            var raw = Legacy(); raw["LoadoutPresets"][0]["HorizonSeconds"] = "invalid legacy horizon";
            File.WriteAllText(path, raw.ToString(4)); var bytes = File.ReadAllBytes(path);
            var plan = store.Load("legacy"); var external = store.ReadExternalPlan("legacy");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "load and external projection never rewrite user plan");
            Assert(plan.ActivePresetId.StartsWith("import_") && plan.Stages[0].PresetId == "", "only the active goal gains a reference; persisted stages remain inline");
            Assert(plan.PresetMigrationIssues.Any(issue => issue.StartsWith("Stages[0].PresetId:") && issue.Contains("explicit conversion required")), "standard legacy stage reports required explicit conversion");
            Assert(external["Version"].AsInt == 18 && external["ActivePresetId"].Value == plan.ActivePresetId, "migration is additive at version 18");
            var library = store.PresetLibrary.ReadExternalLibrary();
            var imported = library["Presets"].Children.Single(row => row["Id"].Value == ImportedStageId(library, "legacy"));
            Assert(!imported.Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights"), "stage definition has no NGU ratio owner storage");
            var localImport = library["Presets"].Children.Single(row => row["Name"].Value == "Source");
            Assert(!localImport.Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights") && localImport["futurePreset"].Value == "1.2300e+004", "per-plan import drops ratios but preserves unknown tokens");
            Assert(localImport["Weights"].Count == 14 && !localImport.Linq.Any(row => row.Key == "HorizonSeconds"), "imported preset normalizes weights and discards invalid legacy horizon");
            Assert(localImport["SupportMode"].Value == "planned" && imported["SupportMode"].Value == "planned"
                && store.PresetLibrary.Get(plan.ActivePresetId).SupportMode == "planned", "new imports without historical receipts use planned support");
            var libraryBytes = File.ReadAllBytes(Path.Combine(runtime, "presets.json"));
            Assert(new CustomPlanStore(Path.Combine(runtime, "plans")).Load("legacy").Stages[0].PresetId == plan.Stages[0].PresetId, "restart uses stable migration identity");
            Assert(libraryBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(runtime, "presets.json"))), "idempotent migration does not rewrite library");
            store.SaveExternalPlan("legacy", external, Revision(external));
            var persisted = JsonFiles.ParseObject(File.ReadAllText(path));
            Assert(persisted["LoadoutPresets"].ToString() == raw["LoadoutPresets"].ToString(), "historical per-plan definitions are retained exactly");
            Assert(persisted["futureRoot"].Value == "9223372036854775807" && persisted["Stages"][0]["futureStage"].Value == "1.234567890123456789"
                && persisted["GoalPresets"][0]["futureGoal"].Value == "9007199254740993", "unknown plan, stage and goal tokens survive save");
            Assert(store.Load("legacy").ActivePresetId == plan.ActivePresetId, "persisted references survive reload");
            Assert(store.Load("legacy").Stages[0].PresetId == "", "saving a normalized legacy plan does not implicitly convert stages");
            Assert(libraryBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(runtime, "presets.json"))), "saving provenance does not create different imports");
            external = store.ReadExternalPlan("legacy"); external["LoadoutPresets"][0]["Name"] = "Not writable";
            Reject<InvalidOperationException>(() => store.SaveExternalPlan("legacy", external, Revision(external)), "historical arrays are not externally writable");
        }

        private static void StableCollisionIdentity()
        {
            var runtime = DirectoryFor("collision"); var store = new CustomPlanStore(Path.Combine(runtime, "plans"));
            var first = Legacy(); var second = Legacy(); second["EnergyNguWeights"][4] = 99;
            File.WriteAllText(Path.Combine(runtime, "plans", "a.json"), first.ToString());
            File.WriteAllText(Path.Combine(runtime, "plans", "b.json"), second.ToString());
            var a = store.Load("a"); var b = store.Load("b");
            Assert(a.ActivePresetId != b.ActivePresetId, "same source ID in separate plans cannot collide");
            var rows = store.PresetLibrary.List().Where(row => row.Name == "Source").ToArray();
            Assert(rows.Length == 2 && rows.Select(row => row.Id).Distinct().Count() == 2, "colliding local IDs yield deterministic distinct globals");
            first["futureUnrelated"] = "irrelevant";
            File.WriteAllText(Path.Combine(runtime, "plans", "a.json"), first.ToString(4));
            Assert(store.Load("a").ActivePresetId == a.ActivePresetId, "unrelated source content and whitespace do not change effective definition hash");
            first["EnergyNguWeights"][4] = 42;
            File.WriteAllText(Path.Combine(runtime, "plans", "a.json"), first.ToString());
            Assert(store.Load("a").ActivePresetId == a.ActivePresetId, "retired NGU ratios cannot create a new definition");
        }

        private static JSONObject ReceiptDefinition(string name, int horizon = 3600, bool previousSchema = true, bool ratioFree = false)
        {
            // Explicit historical output, independent of the current preset serializer and defaults.
            var row = JsonFiles.ParseObject("{\"Id\":\"migration\",\"Name\":\"\",\"Weights\":[1,1,0,0,0,0,0,0,0,0],\"AutoUpdate\":true,"
                + "\"EnergyNguWeights\":[0,0,0,0,7,0,3,0,0],\"MagicNguWeights\":[0,2,0,0,0,0,5],\"AdvancedTrainingWeights\":[1,1,0,0,0],"
                + "\"MagicBloodPercent\":-1,\"SupportMode\":\"current\",\"DiggerBudgetPercent\":75,\"Zone\":-1,\"MinimumPower\":0,"
                + "\"MinimumToughness\":0,\"SwapSeconds\":5,\"BeardPriority\":[],\"DiggerWeights\":[]}");
            row["Name"] = name; row["HorizonSeconds"] = horizon;
            if (name == "Source") row["futurePreset"] = JsonFiles.ParseObject("{\"value\":1.2300e+004}")["value"];
            if (!previousSchema)
            {
                row.Remove("HorizonSeconds");
                row["Weights"].Add(0); row["Weights"].Add(0);
            }
            if (ratioFree)
            {
                row.Remove("EnergyNguWeights"); row.Remove("MagicNguWeights");
            }
            return row;
        }

        private static string AddReceipt(JSONObject library, JSONObject definition, string kind)
        {
            string Canonical(JSONNode node)
            {
                if (node.IsObject) return "{" + string.Join(",", node.Linq.OrderBy(row => row.Key, StringComparer.Ordinal)
                    .Select(row => new JSONString(row.Key).ToString() + ":" + Canonical(row.Value))) + "}";
                if (node.IsArray) return "[" + string.Join(",", node.Children.Select(Canonical)) + "]";
                return node.ToString();
            }
            definition.Remove("Id");
            var hash = JsonFiles.ComputeRevision(Encoding.UTF8.GetBytes("legacy\n" + kind + "\n" + Canonical(definition)));
            var id = "import_" + hash.Substring(0, 56); definition["Id"] = id;
            var receipt = new JSONObject(); receipt["PresetId"] = id; receipt["Source"] = "legacy"; receipt["Kind"] = kind;
            library["MigrationReceipts"][hash] = receipt;
            return id;
        }

        private static void PersistedReceiptUpgrade()
        {
            foreach (var schema in new[] {"ratios10", "ratios12", "ratio-free12"})
            foreach (var count in schema == "ratios10" ? new[] {6, 10} : new[] {6, 10, 12})
            foreach (var horizon in new[] {null, "7200", "7.200e3", "1e0", "604800"})
            foreach (var supportMode in new[] {null, "current", "planned"})
            foreach (var historicalMode in supportMode == null ? new[] {"current", "planned"} : new[] {supportMode})
            foreach (var deleted in new[] {false, true})
            {
                var previousSchema = schema == "ratios10"; var ratioFree = schema == "ratio-free12";
                var runtime = DirectoryFor("upgrade-" + Guid.NewGuid().ToString("N")); var plans = Path.Combine(runtime, "plans");
                var store = new CustomPlanStore(plans); var library = store.PresetLibrary.ReadExternalLibrary();
                var raw = Legacy(); raw["LoadoutPresets"][0]["Weights"] = JsonFiles.ParseObject("{\"value\":[1,1,0,0,0,0"
                    + (count >= 10 ? ",0,0,0,0" : "") + (count == 12 ? ",0,0" : "") + "]}")["value"];
                if (horizon != null) raw["LoadoutPresets"][0]["HorizonSeconds"] = JsonFiles.ParseObject("{\"value\":" + horizon + "}")["value"];
                if (supportMode != null) raw["LoadoutPresets"][0]["SupportMode"] = supportMode;
                var local = ReceiptDefinition("Source", horizon == null ? 3600 : (int) double.Parse(horizon, System.Globalization.CultureInfo.InvariantCulture), previousSchema, ratioFree);
                var active = ReceiptDefinition("Legacy", previousSchema: previousSchema, ratioFree: ratioFree);
                var stage = ReceiptDefinition("All NGU", previousSchema: previousSchema, ratioFree: ratioFree);
                local["SupportMode"] = historicalMode;
                // Active and stage support is always implicit in the source plan.
                active["SupportMode"] = stage["SupportMode"] = historicalMode;
                var localId = AddReceipt(library, local, "loadout:same");
                var activeId = AddReceipt(library, active, "active");
                var stageId = AddReceipt(library, stage, "stage");
                var savedMode = supportMode ?? "planned";
                if (!deleted)
                    foreach (var row in new[] {local, active, stage})
                    {
                        row["Name"] = "User edited " + row["Name"].Value; row["Weights"][0] = 8; row["AutoUpdate"] = false;
                        row["SupportMode"] = savedMode;
                        row["futureEdit"] = "keep"; library["Presets"].Add(row);
                    }
                library.Remove(CustomPlanStore.ExternalUiRevisionField);
                var libraryPath = Path.Combine(runtime, "presets.json"); File.WriteAllText(libraryPath, library.ToString(4));
                var sourcePath = Path.Combine(plans, "legacy.json"); File.WriteAllText(sourcePath, raw.ToString());
                var bytes = File.ReadAllBytes(libraryPath); var sourceBytes = File.ReadAllBytes(sourcePath);
                var upgraded = new PresetLibraryStore(runtime, plans); var rows = upgraded.List();
                Assert(rows.Length == library["Presets"].Count, "upgrade initialization creates no duplicate imports or resurrected tombstones");
                var aliased = JsonFiles.ParseObject(File.ReadAllText(libraryPath));
                Assert(aliased["MigrationReceipts"].Count == library["MigrationReceipts"].Count + 3
                    && library["MigrationReceipts"].Linq.All(item => aliased["MigrationReceipts"][item.Key].ToString() == item.Value.ToString()),
                    "old receipts survive alongside three fourteen-lane aliases before provenance can be stripped: " + schema);
                bytes = File.ReadAllBytes(libraryPath);
                var plan = store.Load("legacy"); var external = store.ReadExternalPlan("legacy");
                Assert(plan.ActivePresetId == (deleted ? "" : activeId) && external["ActivePresetId"].Value == plan.ActivePresetId,
                    "plan reads retain the old active binding or its deletion");
                Assert(plan.Stages[0].PresetId == "" && plan.PresetMigrationIssues.Any(issue => issue.Contains(stageId)), "legacy stage reports the previous imported identity without implicit conversion");
                if (deleted)
                    Assert(new[] {localId, activeId, stageId}.All(id => plan.PresetMigrationIssues.Any(issue => issue.Contains(id) && issue.Contains("was deleted"))),
                        "all three historical tombstones remain authoritative");
                else
                    Assert(new[] {localId, activeId, stageId}.All(id => rows.Single(row => row.Id == id).Name.StartsWith("User edited ")
                        && rows.Single(row => row.Id == id).Weights[0] == 8 && !rows.Single(row => row.Id == id).AutoUpdate
                        && rows.Single(row => row.Id == id).SupportMode == savedMode), "edited imported rows keep prior IDs and user changes, including explicit support mode");
                Assert(bytes.SequenceEqual(File.ReadAllBytes(libraryPath)) && sourceBytes.SequenceEqual(File.ReadAllBytes(sourcePath)), "plan reads preserve both original files");
                var read = upgraded.ReadExternalLibrary(); upgraded.SaveExternalLibrary(read, Revision(read));
                read = new PresetLibraryStore(runtime, plans).ReadExternalLibrary();
                Assert(read["Presets"].Count == library["Presets"].Count && read["MigrationReceipts"].ToString() == aliased["MigrationReceipts"].ToString(),
                    "save and restart preserve historical receipts and stable ratio-free aliases without duplicate presets");
                Assert(read["Presets"].Children.All(row => row["Weights"].Count == 14 && !row.Linq.Any(field => field.Key == "HorizonSeconds" || field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")), "upgrade saves only the ratio-free current schema");
                if (!deleted)
                    Assert(new[] {localId, activeId, stageId}.All(id => read["Presets"].Children.Single(row => row["Id"].Value == id)["SupportMode"].Value == savedMode),
                        "save and restart preserve explicit support choices on historical imports");
                store.SaveExternalPlan("legacy", external, Revision(external));
                var restarted = new CustomPlanStore(plans);
                Assert(restarted.Load("legacy").ActivePresetId == plan.ActivePresetId
                    && restarted.PresetLibrary.ReadExternalLibrary()["MigrationReceipts"].ToString() == aliased["MigrationReceipts"].ToString()
                    && restarted.PresetLibrary.List().Length == rows.Length, "stripping plan ratios cannot change IDs, receipts or resurrect deleted definitions");
            }
        }

        private static void ExplicitSupportReceiptIsolation()
        {
            foreach (var schema in new[] {"ratios10", "ratios12", "ratio-free12"})
            foreach (var mode in new[] {"planned", "current"})
            foreach (var deleted in new[] {false, true})
            {
                var runtime = DirectoryFor("support-isolation-" + Guid.NewGuid().ToString("N"));
                var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var library = LibraryRoot(0); library["MigrationReceipts"] = new JSONObject();
                var old = ReceiptDefinition("Source", previousSchema: schema == "ratios10", ratioFree: schema == "ratio-free12");
                old["SupportMode"] = mode == "planned" ? "current" : "planned";
                var oldId = AddReceipt(library, old, "loadout:same");
                var receipt = library["MigrationReceipts"].ToString();
                if (!deleted) library["Presets"].Add(old);
                var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, library.ToString());
                var raw = Legacy(99); raw["Stages"][0]["GearGoal"] = 999; raw["LoadoutPresets"][0]["SupportMode"] = mode;
                var sourcePath = Path.Combine(plans, "legacy.json"); File.WriteAllText(sourcePath, raw.ToString());
                var sourceBytes = File.ReadAllBytes(sourcePath);
                var store = new PresetLibraryStore(runtime, plans);
                var rows = store.List(); var added = rows.Single(row => row.Id != oldId);
                Assert(rows.Length == (deleted ? 1 : 2) && added.SupportMode == mode, "explicit support creates a distinct import from the opposite mode: " + mode);
                Assert(deleted ? store.Get(oldId) == null : store.Get(oldId).ToJson().ToString() == CustomLoadoutPreset.Parse(old).ToJson().ToString(),
                    "explicit support neither resurrects nor overwrites the historical import");
                var read = store.ReadExternalLibrary();
                Assert(read["MigrationReceipts"].Count == 2 && JsonFiles.ParseObject(receipt).Linq.All(item => read["MigrationReceipts"][item.Key].ToString() == item.Value.ToString()),
                    "explicit support keeps historical receipts alongside its distinct receipt");
                var plan = CustomPlanStore.ReadForPresetMigration(raw.ToString(), "legacy"); store.MigratePlan(plan, "legacy");
                Assert(!plan.PresetMigrationIssues.Any(issue => issue.Contains("was deleted")), "explicit mode does not inherit an opposite-mode tombstone");
                if (mode == "planned")
                {
                    var implicitSource = JsonFiles.ParseObject(raw.ToString()); implicitSource["LoadoutPresets"][0].Remove("SupportMode");
                    var implicitPlan = CustomPlanStore.ReadForPresetMigration(implicitSource.ToString(), "legacy"); store.MigratePlan(implicitPlan, "legacy");
                    Assert(!implicitPlan.PresetMigrationIssues.Any(issue => issue.Contains("was deleted")) && store.List().Length == rows.Length,
                        "an existing planned receipt takes precedence over old current compatibility, including tombstones");
                }
                store.SaveExternalLibrary(read, Revision(read));
                var bytes = File.ReadAllBytes(path);
                Assert(new PresetLibraryStore(runtime, plans).Get(added.Id).SupportMode == mode
                    && bytes.SequenceEqual(File.ReadAllBytes(path)) && sourceBytes.SequenceEqual(File.ReadAllBytes(sourcePath)),
                    "explicit support identity is stable across migration, save and restart without changing provenance");
            }
        }

        private static void RatioFreeTwelveReceiptIdentity()
        {
            foreach (var change in new[] {"adventure", "dynamic", "retired ratios", "cap"})
            foreach (var historicalMode in new[] {"planned", "current"})
            foreach (var deleted in new[] {false, true})
            {
                var runtime = DirectoryFor("ratio-free-twelve-" + Guid.NewGuid().ToString("N")); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var root = LibraryRoot(0); root["MigrationReceipts"] = new JSONObject();
                var old = ReceiptDefinition("Source", previousSchema: false, ratioFree: true);
                old["SupportMode"] = historicalMode; old["Weights"][10] = 7; old["Weights"][11] = 3;
                if (change == "dynamic") old["AdventureDynamicWeight"] = 2.5;
                var raw = Legacy(99); raw["Stages"][0]["GearGoal"] = 999;
                var source = raw["LoadoutPresets"][0]; source["Weights"] = JSON.Parse(old["Weights"].ToString());
                source["HorizonSeconds"] = "ignored";
                if (change == "dynamic") source["AdventureDynamicWeight"] = 2.5;
                if (change == "retired ratios") source["EnergyNguWeights"] = "invalid retired data";
                if (change == "cap") { source["Weights"].Add(1); source["Weights"].Add(0); }
                var oldId = AddReceipt(root, old, "loadout:same"); var receipt = root["MigrationReceipts"].ToString();
                if (!deleted) { old["Name"] = "Edited import"; old["Weights"][10] = 42; root["Presets"].Add(old); }
                var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, root.ToString(4));
                var sourcePath = Path.Combine(plans, "legacy.json"); File.WriteAllText(sourcePath, raw.ToString()); var sourceBytes = File.ReadAllBytes(sourcePath);
                var store = new PresetLibraryStore(runtime, plans); var read = store.ReadExternalLibrary();
                var plan = CustomPlanStore.ReadForPresetMigration(raw.ToString(), "legacy"); store.MigratePlan(plan, "legacy");
                Assert(read["MigrationReceipts"].Count == 2 && JsonFiles.ParseObject(receipt).Linq.All(item => read["MigrationReceipts"][item.Key].ToString() == item.Value.ToString()),
                    "ratio-free twelve-lane receipt is preserved alongside one current receipt: " + change);
                if (change == "cap")
                    Assert(store.List().Length == (deleted ? 1 : 2) && store.List().Any(row => row.Id != oldId && row.Weights[12] == 1)
                        && !plan.PresetMigrationIssues.Any(issue => issue.Contains("was deleted")), "a positive cap is a distinct definition, not an old twelve-lane alias or tombstone");
                else
                    Assert(store.List().Length == (deleted ? 0 : 1) && (deleted
                        ? plan.PresetMigrationIssues.Any(issue => issue.Contains(oldId) && issue.Contains("was deleted"))
                        : store.Get(oldId).Name == "Edited import" && store.Get(oldId).Weights[10] == 42),
                        "ratio-free aliases retain nonzero adventure/dynamic identities, edits and tombstones despite retired fields: " + change);
                store.SaveExternalLibrary(read, Revision(read)); var bytes = File.ReadAllBytes(path);
                Assert(new PresetLibraryStore(runtime, plans).List().Length == read["Presets"].Count
                    && bytes.SequenceEqual(File.ReadAllBytes(path)) && sourceBytes.SequenceEqual(File.ReadAllBytes(sourcePath)),
                    "ratio-free twelve-lane identity survives save and read-only restart without touching the source");
            }
        }

        private static void PreviousSchemaIdentityIsolation()
        {
            foreach (var change in new[] {"unknown", "weights", "ngu", "twelve", "adventure", "invalid horizon", "rounded horizon", "invalid weights"})
            {
                var runtime = DirectoryFor("upgrade-isolation-" + Guid.NewGuid().ToString("N")); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var library = LibraryRoot(0); library["MigrationReceipts"] = new JSONObject();
                var previous = ReceiptDefinition("Source"); var oldId = AddReceipt(library, previous, "loadout:same");
                library["Presets"].Add(previous);
                var path = Path.Combine(runtime, "presets.json"); File.WriteAllText(path, library.ToString());
                var raw = Legacy(99); raw["Stages"][0]["GearGoal"] = 999;
                var row = raw["LoadoutPresets"][0];
                switch (change)
                {
                    case "unknown": row["futurePreset"] = 12301; break;
                    case "weights": row["Weights"][0] = 3; break;
                    case "ngu": row["EnergyNguWeights"] = raw["EnergyNguWeights"]; row["EnergyNguWeights"][4] = 42; break;
                    case "twelve": case "adventure":
                        row["Weights"] = JsonFiles.ParseObject("{\"value\":[1,1,0,0,0,0,0,0,0,0," + (change == "adventure" ? "1" : "0") + ",0]}")["value"]; break;
                    case "invalid horizon": row["HorizonSeconds"] = "ignored"; break;
                    case "rounded horizon": row["HorizonSeconds"] = JsonFiles.ParseObject("{\"value\":3600.0000000000000000001}")["value"]; break;
                    case "invalid weights": row["Weights"][0] = -1; break;
                }
                var store = new PresetLibraryStore(runtime, plans);
                if (change == "invalid weights")
                {
                    Reject<FormatException>(() => CustomPlanStore.ReadForPresetMigration(raw.ToString(), "legacy"), "receipt compatibility never bypasses current validation");
                    continue;
                }
                var plan = CustomPlanStore.ReadForPresetMigration(raw.ToString(), "legacy"); store.MigratePlan(plan, "legacy");
                Assert(store.List().Length == 2 && store.Get(oldId) != null, "previous receipt does not swallow a changed or previously unrepresentable definition: " + change);
                Assert(store.ReadExternalLibrary()["MigrationReceipts"].Count == 2, "changed definition receives its own current-schema receipt: " + change);
            }
        }

        private static void PreserveLegacySemantics()
        {
            var runtime = DirectoryFor("legacy-semantics"); var store = new CustomPlanStore(Path.Combine(runtime, "plans"));
            var raw = Legacy(99); raw["LoadoutPresets"] = new JSONArray();
            var stages = new JSONArray();
            foreach (var fields in new[] {
                "\"GearGoal\":4,\"EnergyPriorities\":[\"NGU-4\",\"TM\"]",
                "\"GearGoal\":0", "\"GearGoal\":9,\"FightOnEntry\":true", "\"GearGoal\":4,\"BlackBarLookaheadSeconds\":30",
                "\"GearGoal\":4,\"ExecutionMode\":1", "\"GearGoal\":999"})
                stages.Add(JsonFiles.ParseObject("{\"Name\":\"Keep\",\"EnergyFocus\":5,\"MagicFocus\":6," + fields + "}"));
            raw["Stages"] = stages;
            var path = Path.Combine(runtime, "plans", "custom.json"); File.WriteAllText(path, raw.ToString());
            var plan = store.Load("custom");
            Assert(plan.ActivePresetId == "" && plan.Stages.All(stage => stage.PresetId == ""), "unsupported semantics do not receive fallback presets");
            Assert(plan.GearGoal == 99 && plan.Stages[5].GearGoal == 999 && plan.Stages[1].GearGoal == 0 && plan.Stages[2].FightSchedule.Id == "legacy-entry-v1-2", "unknown, cap and migrated fight gear goals remain inline unchanged");
            Assert(plan.Stages[0].EnergyPriorities.SequenceEqual(new[] {"NGU-4", "TM"}) && plan.Stages[3].BlackBarLookaheadSeconds == 30, "custom priorities and cap horizon remain intact");
            Assert(plan.PresetMigrationIssues.Length == 7 && plan.PresetMigrationIssues.Any(issue => issue.StartsWith("ActivePresetId:")), "every unrepresentable setting has an explicit migration issue");
            var scheduled = new CustomPlan {Stages = new[] {
                new CustomPlanStage {EnergyFocus = 5, MagicFocus = 6, GearGoal = 2, FightSchedule = new CustomFightSchedule {Id = "scheduled", Anchor = 1, DelaySeconds = 10, IntervalSeconds = 20}},
                new CustomPlanStage {EnergyFocus = 5, MagicFocus = 6, GearGoal = 2}
            }};
            store.Save("scheduled-semantics", scheduled);
            var reloaded = store.Load("scheduled-semantics");
            Assert(reloaded.Stages[0].PresetId == "" && reloaded.Stages[1].PresetId != "",
                "schedule excludes otherwise representable stages from automatic preset conversion");
            Assert(reloaded.Stages[0].FightSchedule.Id == "scheduled" && reloaded.Stages[0].FightSchedule.Anchor == 1
                && reloaded.Stages[0].FightSchedule.DelaySeconds == 10 && reloaded.Stages[0].FightSchedule.IntervalSeconds == 20,
                "preset migration preserves the complete schedule");
        }

        private static void ActivityTemplates()
        {
            var runtime = DirectoryFor("templates"); var store = new CustomPlanStore(Path.Combine(runtime, "plans"));
            JSONObject policy;
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("NGUInjector.ActivityPolicies.json"))
            using (var reader = new StreamReader(stream)) policy = JsonFiles.ParseObject(reader.ReadToEnd());
            var plan = Plan();
            plan.Stages = policy["stagePresets"].Children.Select(row => new CustomPlanStage
            {
                Name = row["label"].Value, GearGoal = row["GearGoal"].AsInt, EnergyFocus = row["EnergyFocus"].AsInt, MagicFocus = row["MagicFocus"].AsInt,
                EnergyPriorities = policy["prioritySets"][row["EnergyPriorities"].Value].Children.Select(value => value.Value).ToArray(),
                MagicPriorities = policy["prioritySets"][row["MagicPriorities"].Value].Children.Select(value => value.Value).ToArray()
            }).ToArray();
            store.Save("templates", plan);
            Assert(plan.Stages.All(stage => stage.PresetId.Length > 0) && plan.PresetMigrationIssues.Length == 0, "all eight policy templates are representable");
            var atAdventure = store.PresetLibrary.Get(plan.Stages[6].PresetId); var atWandoos = store.PresetLibrary.Get(plan.Stages[7].PresetId);
            Assert(atAdventure.AdvancedTrainingWeights.SequenceEqual(new[] {1, 1, 0, 0, 0}), "imported AT Adventure retains adventure targets");
            Assert(atWandoos.AdvancedTrainingWeights.SequenceEqual(new[] {0, 0, 0, 1, 1}) && atAdventure.Id != atWandoos.Id, "AT Wandoos definition has distinct targets and identity");
            Assert(store.PresetLibrary.Get(plan.Stages[1].PresetId).Weights[2] == 1, "Augments + Blood includes Blood objective");
            var newName = store.CreatePlan("new"); var created = store.Load(newName);
            Assert(created.Stages.Where(stage => stage.GearGoal == 5 || stage.GearGoal == 10).All(stage => stage.PresetId.Length > 0), "new plans seed references for representable default stages");
            var legacy = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(runtime, "plans", "templates.json")));
            legacy["StageDurationSeconds"] = 7200; legacy["StageLoopStartIndex"] = 3;
            for (var i = 0; i < legacy["Stages"].Count; i++)
            {
                legacy["Stages"][i].Remove("PresetId");
                legacy["Stages"][i]["Percent"] = i + 1;
                legacy["Stages"][i]["DurationSeconds"] = i % 2 == 0 ? 60 : 0;
            }
            var path = Path.Combine(runtime, "plans", "inline.json"); File.WriteAllText(path, legacy.ToString()); var bytes = File.ReadAllBytes(path);
            var inline = store.Load("inline");
            Assert(inline.Stages.All(stage => stage.PresetId == ""), "all eight persisted policy templates stay on the legacy allocator");
            Assert(inline.PresetMigrationIssues.Count(issue => issue.Contains("explicit conversion required")) == 8, "all standard legacy templates require explicit conversion");
            var library = store.PresetLibrary.ReadExternalLibrary();
            Assert(library["MigrationReceipts"].Children.Count(row => row["Source"].Value == "inline" && row["Kind"].Value == "stage") == 8, "legacy template globals are still imported");
            var projected = store.ReadExternalPlan("inline");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "legacy template projection never rewrites persisted schedules");
            Assert(projected["StageDurationSeconds"].AsInt == 7200 && projected["StageLoopStartIndex"].AsInt == 3
                && projected["Stages"].Children.Select(row => row["DurationSeconds"].AsInt).SequenceEqual(new[] {60, 0, 60, 0, 60, 0, 60, 0})
                && projected["Stages"].Children.Select(row => row["Percent"].AsInt).SequenceEqual(Enumerable.Range(1, 8)), "upgrade preserves fixed durations, shares, horizon and loop schedule");
            projected["Stages"][0]["PresetId"] = "builtin_timeMachine";
            store.SaveExternalPlan("inline", projected, Revision(projected));
            Assert(store.Load("inline").Stages[0].PresetId == "builtin_timeMachine" && store.Load("inline").Stages.Skip(1).All(stage => stage.PresetId == ""), "only an explicit user selection converts a legacy stage");
        }

        private static void ReferenceProtection()
        {
            var runtime = DirectoryFor("references"); var store = new CustomPlanStore(Path.Combine(runtime, "plans"));
            var path = Path.Combine(runtime, "plans", "unwritten.json"); File.WriteAllText(path, Legacy().ToString());
            var plan = store.Load("unwritten"); var root = store.PresetLibrary.ReadExternalLibrary();
            root["Presets"] = Without(root["Presets"], plan.ActivePresetId);
            Reject<InvalidOperationException>(() => store.PresetLibrary.SaveExternalLibrary(root, Revision(root)), "derived unsaved active reference blocks deletion");
            root = store.PresetLibrary.ReadExternalLibrary(); var stageImport = ImportedStageId(root, "unwritten");
            root["Presets"] = Without(root["Presets"], stageImport);
            store.PresetLibrary.SaveExternalLibrary(root, Revision(root));
            Assert(store.PresetLibrary.Get(stageImport) == null && store.Load("unwritten").Stages[0].PresetId == "", "unselected conversion candidate is removable without changing legacy execution");
            var explicitPlan = Plan(0); explicitPlan.ActivePresetId = "builtin_adventureDynamic"; explicitPlan.Stages[0].PresetId = "builtin_adventurePower";
            store.Save("explicit", explicitPlan);
            root = store.PresetLibrary.ReadExternalLibrary(); root["Presets"] = Without(root["Presets"], "builtin_adventureDynamic");
            Reject<InvalidOperationException>(() => store.PresetLibrary.SaveExternalLibrary(root, Revision(root)), "persisted active reference blocks deletion");
            root = store.PresetLibrary.ReadExternalLibrary(); root["Presets"] = Without(root["Presets"], "builtin_adventurePower");
            Reject<InvalidOperationException>(() => store.PresetLibrary.SaveExternalLibrary(root, Revision(root)), "persisted stage reference blocks deletion");
            root = store.PresetLibrary.ReadExternalLibrary(); var localId = root["Presets"].Children.Single(row => row["Name"].Value == "Source")["Id"].Value;
            root["Presets"] = Without(root["Presets"], localId); root.Remove("MigrationReceipts");
            store.PresetLibrary.SaveExternalLibrary(root, Revision(root));
            Assert(new CustomPlanStore(Path.Combine(runtime, "plans")).PresetLibrary.Get(localId) == null, "deleted unreferenced import is not resurrected by provenance");
            Assert(store.Load("unwritten").PresetMigrationIssues.Any(issue => issue.Contains("was deleted")), "deleted historical import produces explicit issue");
            var diskBefore = File.ReadAllBytes(Path.Combine(runtime, "plans", "explicit.json"));
            explicitPlan.ActivePresetId = "missing";
            Reject<FormatException>(() => store.Save("explicit", explicitPlan), "native dangling reference rejected");
            Assert(diskBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(runtime, "plans", "explicit.json"))), "failed reference validation leaves plan untouched");
            var external = store.ReadExternalPlan("explicit"); external["Stages"][0]["PresetId"] = "missing";
            Reject<FormatException>(() => store.SaveExternalPlan("explicit", external, Revision(external)), "external dangling stage rejected");
            external["Stages"][0]["PresetId"] = 123;
            Reject<FormatException>(() => store.SaveExternalPlan("explicit", external, Revision(external)), "reference JSON type is strict");
            external["Stages"][0]["PresetId"] = "";
            external["ActivePresetId"] = "";
            store.SaveExternalPlan("explicit", external, Revision(external));
            Assert(store.Load("explicit").Stages[0].PresetId == "" && store.Load("explicit").ActivePresetId == "", "explicit cleared references stay empty instead of being remigrated");
            root = store.PresetLibrary.ReadExternalLibrary(); root["Presets"] = Without(root["Presets"], "builtin_adventurePower");
            store.PresetLibrary.SaveExternalLibrary(root, Revision(root));
            Assert(store.PresetLibrary.Get("builtin_adventurePower") == null, "clearing the last stage reference permits deletion");
            var reloaded = new CustomPlanStore(Path.Combine(runtime, "plans"));
            var cleared = reloaded.ReadExternalPlan("explicit");
            reloaded.SaveExternalPlan("explicit", cleared, Revision(cleared));
            Assert(reloaded.Load("explicit").ActivePresetId == "" && reloaded.Load("explicit").Stages[0].PresetId == "", "empty clears remain empty across restart, projection and another save");
        }

        private static void ActiveInheritance()
        {
            void RejectInherited(Action action, string label)
            {
                try { action(); }
                catch (FormatException error)
                {
                    Assert(error.Message.Contains("@active") && error.Message.Contains("active", StringComparison.OrdinalIgnoreCase), label + " identifies the unresolved inherited selection");
                    return;
                }
                throw new Exception("FAIL (accepted): " + label);
            }
            var runtime = DirectoryFor("inheritance"); var plans = Path.Combine(runtime, "plans"); var store = new CustomPlanStore(plans);
            var plan = Plan(); plan.ActivePresetId = "builtin_adventureDynamic"; plan.Stages[0].PresetId = "@active";
            store.Save("inherited", plan);
            var external = store.ReadExternalPlan("inherited");
            Assert(external["Stages"][0]["PresetId"].Value == "@active", "stage inheritance sentinel survives parse and serialization");
            var library = store.PresetLibrary.ReadExternalLibrary(); library["Presets"] = Without(library["Presets"], "builtin_adventureDynamic");
            Reject<InvalidOperationException>(() => store.PresetLibrary.SaveExternalLibrary(library, Revision(library)), "inherited active reference blocks global deletion");
            external["ActivePresetId"] = "builtin_adventurePower"; store.SaveExternalPlan("inherited", external, Revision(external));
            Assert(store.Load("inherited").ActivePresetId == "builtin_adventurePower" && store.Load("inherited").Stages[0].PresetId == "@active", "changing active selection preserves stage inheritance");
            library = store.PresetLibrary.ReadExternalLibrary(); library["Presets"] = Without(library["Presets"], "builtin_adventureDynamic");
            store.PresetLibrary.SaveExternalLibrary(library, Revision(library));
            Assert(store.PresetLibrary.Get("builtin_adventureDynamic") == null, "previous inherited active ID is removable after switching selection");
            foreach (var missing in new[] {"", "absent"})
            {
                external = store.ReadExternalPlan("inherited"); external["ActivePresetId"] = missing;
                var bytes = File.ReadAllBytes(Path.Combine(plans, "inherited.json"));
                RejectInherited(() => store.SaveExternalPlan("inherited", external, Revision(external)), "unresolved inherited active selection rejected: " + missing);
                Assert(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(plans, "inherited.json"))), "rejected inheritance edit preserves persisted plan");
                plan.ActivePresetId = missing;
                RejectInherited(() => store.Save("native-invalid", plan), "native unresolved inheritance rejected: " + missing);
            }
            external = store.ReadExternalPlan("inherited"); external["ActivePresetId"] = "@active";
            Reject<FormatException>(() => store.SaveExternalPlan("inherited", external, Revision(external)), "inheritance sentinel is not a plan ActivePresetId");
            foreach (var missing in new[] {"", "absent", null})
            {
                var raw = Legacy(); raw["Stages"][0]["PresetId"] = "@active";
                if (missing != null) raw["ActivePresetId"] = missing;
                var path = Path.Combine(plans, "invalid.json"); File.WriteAllText(path, raw.ToString());
                RejectInherited(() => store.Load("invalid"), "persisted unresolved inheritance rejected without automatic active substitution");
                File.Delete(path);
            }
            external = store.ReadExternalPlan("inherited"); external["Stages"][0]["PresetId"] = ""; external["ActivePresetId"] = "";
            store.SaveExternalPlan("inherited", external, Revision(external));
            var cleared = new CustomPlanStore(plans).Load("inherited");
            Assert(cleared.Stages[0].PresetId == "" && cleared.ActivePresetId == "", "cleared inheritance and active selection stay empty after restart");
            library = store.PresetLibrary.ReadExternalLibrary(); library["Presets"] = Without(library["Presets"], "builtin_adventurePower");
            store.PresetLibrary.SaveExternalLibrary(library, Revision(library));
            Assert(store.PresetLibrary.Get("builtin_adventurePower") == null, "clearing inheritance and active selection releases the global ID");
        }

        private static void TwelveObjectiveBindings()
        {
            var runtime = DirectoryFor("twelve-bindings"); var plans = Path.Combine(runtime, "plans"); var store = new CustomPlanStore(plans);
            var root = store.PresetLibrary.ReadExternalLibrary();
            var preset = JsonFiles.ParseObject("{\"Id\":\"adventure_custom\",\"Name\":\"Adventure custom\",\"Weights\":[0,0,0,0,0,0,0,0,0,0,7,3],"
                + "\"AutoUpdate\":false,\"EnergyNguWeights\":[9,8,7,6,5,4,3,2,1],\"MagicNguWeights\":[7,6,5,4,3,2,1],\"AdvancedTrainingWeights\":[0,0,0,2,3],"
                + "\"HorizonSeconds\":\"retired\",\"future\":{\"number\":9007199254740993,\"decimal\":1.234567890123456789}}");
            var future = preset["future"].ToString(); root["Presets"].Add(preset);
            store.PresetLibrary.SaveExternalLibrary(root, Revision(root));
            var raw = Legacy(); raw["Stages"].Add(JsonFiles.ParseObject(raw["Stages"][0].ToString())); raw["Stages"][1]["Name"] = "Keep inline";
            var path = Path.Combine(plans, "bound.json"); File.WriteAllText(path, raw.ToString());
            var external = store.ReadExternalPlan("bound");
            Assert(external["Stages"].Children.All(stage => stage["PresetId"].Value == ""), "twelve-objective integration leaves existing stages inline before selection");
            external["ActivePresetId"] = "adventure_custom"; external["Stages"][0]["PresetId"] = "@active";
            store.SaveExternalPlan("bound", external, Revision(external));
            store = new CustomPlanStore(plans);
            var plan = store.Load("bound"); var selected = store.PresetLibrary.Get(plan.ActivePresetId);
            Assert(plan.ActivePresetId == "adventure_custom" && plan.Stages[0].PresetId == "@active" && plan.Stages[1].PresetId == "", "restart preserves explicit inheritance without converting the other inline stage");
            Assert(selected.Weights.SequenceEqual(new double[] {0,0,0,0,0,0,0,0,0,0,7,3,0,0}) && !selected.AutoUpdate, "inherited custom preset retains adventure objectives and manual update semantics");
            Assert(!selected.ToJson().Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")
                && selected.AdvancedTrainingWeights.SequenceEqual(new[] {0,0,0,2,3}), "preset NGU ratios retired while AT weights remain intact");
            Assert(selected.ToJson()["future"].ToString() == future && !selected.ToJson().Linq.Any(field => field.Key == "HorizonSeconds"), "twelve-objective binding preserves unknown data without restoring a retired horizon");
            Assert(!JsonFiles.ParseObject(File.ReadAllText(path)).Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights"), "saving a bound plan strips inline NGU ratios");
            root = store.PresetLibrary.ReadExternalLibrary(); root["Presets"] = Without(root["Presets"], "adventure_custom");
            Reject<InvalidOperationException>(() => store.PresetLibrary.SaveExternalLibrary(root, Revision(root)), "inherited twelve-objective preset is protected from deletion");
            root = store.PresetLibrary.ReadExternalLibrary(); var edited = root["Presets"].Children.Single(row => row["Id"].Value == "adventure_custom");
            edited["Weights"][10] = 11; edited["Weights"][11] = 5; edited["Name"] = "Edited adventure";
            store.PresetLibrary.SaveExternalLibrary(root, Revision(root));
            store = new CustomPlanStore(plans); plan = store.Load("bound"); selected = store.PresetLibrary.Get(plan.ActivePresetId);
            Assert(selected.Weights[10] == 11 && selected.Weights[11] == 5 && selected.Name == "Edited adventure" && selected.ToJson()["future"].ToString() == future,
                "migration and restart never overwrite edited adventure weights or unknown fields");
            Assert(plan.Stages[1].PresetId == "" && plan.PresetMigrationIssues.Any(issue => issue.StartsWith("Stages[1].PresetId:") && issue.Contains("explicit conversion required")),
                "unselected legacy stage still requires conversion after global edits");
            external = store.ReadExternalPlan("bound"); external["ActivePresetId"] = ""; external["Stages"][0]["PresetId"] = "";
            store.SaveExternalPlan("bound", external, Revision(external)); plan = new CustomPlanStore(plans).Load("bound");
            Assert(plan.ActivePresetId == "" && plan.Stages.All(stage => stage.PresetId == ""), "clearing twelve-objective inheritance stays cleared across restart");
        }

        private static void BuiltInRepairSafety()
        {
            var runtime = DirectoryFor("repair"); var store = new CustomPlanStore(Path.Combine(runtime, "plans"));
            var path = Path.Combine(runtime, "plans", "1h-3m-rebirth.json");
            var raw = Legacy(99); raw["ActivePresetId"] = "builtin_adventureDynamic"; raw["Stages"][0]["PresetId"] = "builtin_adventurePower";
            File.WriteAllText(path, raw.ToString()); var bytes = File.ReadAllBytes(path);
            store = new CustomPlanStore(Path.Combine(runtime, "plans")); store.ListPlanNames();
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "old built-in repair never rewrites new references");
            Assert(store.Load("1h-3m-rebirth").ActivePresetId == "builtin_adventureDynamic" && store.Load("1h-3m-rebirth").GearGoal == 99, "built-in preserves new preset selection and unsupported provenance");
            raw.Remove("ActivePresetId"); raw["Stages"][0].Remove("PresetId");
            File.WriteAllText(path, raw.ToString()); bytes = File.ReadAllBytes(path);
            store = new CustomPlanStore(Path.Combine(runtime, "plans"));
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "custom legacy built-in gear goal is not silently repaired");
            Assert(store.Load("1h-3m-rebirth").ActivePresetId == "" && store.Load("1h-3m-rebirth").PresetMigrationIssues.Any(issue => issue.Contains("99")), "unsupported built-in goal remains explicit migration issue");
        }

        private static void AtomicLibraryFirst()
        {
            var runtime = DirectoryFor("write-failure"); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            Directory.CreateDirectory(Path.Combine(runtime, "presets.json"));
            Reject<IOException>(() => new CustomPlanStore(plans), "library failure aborts plan initialization");
            Assert(Directory.GetFiles(plans).Length == 0, "plan references never persist before library write succeeds");
            Assert(Directory.GetFiles(runtime, "*.tmp").Length == 0, "failed atomic library write cleans temporary file");
            if (OperatingSystem.IsWindows())
            {
                runtime = DirectoryFor("retry"); plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
                var library = new PresetLibraryStore(runtime, plans); library.List();
                var path = Path.Combine(runtime, "presets.json"); var bytes = File.ReadAllBytes(path);
                var plan = Plan();
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Reject<IOException>(() => library.MigratePlan(plan, "retry"), "blocked atomic replace rejects migration");
                Assert(plan.ActivePresetId == "" && plan.Stages[0].PresetId == "", "failed migration rolls back in-memory references");
                Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "blocked migration leaves original library bytes intact");
                library.MigratePlan(plan, "retry");
                Assert(library.Get(plan.ActivePresetId) != null && library.Get(plan.Stages[0].PresetId) != null, "same model can retry migration after IO failure");
            }
        }

        private static void ConcurrentCas()
        {
            var runtime = DirectoryFor("concurrent"); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            var a = new PresetLibraryStore(runtime, plans); var b = new PresetLibraryStore(runtime, plans);
            var first = a.ReadExternalLibrary(); var second = b.ReadExternalLibrary();
            first["writer"] = "a"; second["writer"] = "b";
            var successes = 0; var conflicts = 0;
            Parallel.Invoke(() => Save(a, first), () => Save(b, second));
            void Save(PresetLibraryStore store, JSONObject root)
            {
                try { store.SaveExternalLibrary(root, Revision(root)); System.Threading.Interlocked.Increment(ref successes); }
                catch (InvalidOperationException) { System.Threading.Interlocked.Increment(ref conflicts); }
            }
            Assert(successes == 1 && conflicts == 1, "same-process CAS permits exactly one concurrent writer");
            Assert(a.ReadExternalLibrary()["writer"].Value == "a" || a.ReadExternalLibrary()["writer"].Value == "b", "CAS winner leaves valid complete JSON");
        }

        private static void CountBounds()
        {
            var runtime = DirectoryFor("count-bounds"); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            var library = new PresetLibraryStore(runtime, plans); var initial = library.ReadExternalLibrary();
            var root = LibraryRoot(1024); var path = Path.Combine(runtime, "presets.json");
            library.SaveExternalLibrary(root, Revision(initial));
            var bytes = File.ReadAllBytes(path);
            Assert(library.List().Length == 1024 && library.ReadExternalLibrary()["Presets"].Count == 1024, "exactly 1024 presets round trip without truncation");
            Assert(bytes.Length <= 2 * 1024 * 1024, "1024-preset fixture has an actually valid persisted UTF-8 size");
            var read = library.ReadExternalLibrary(); read["Presets"].Add(new CustomLoadoutPreset {Id = "over_limit", Name = "Extra"}.ToJson());
            RejectLimit(() => library.SaveExternalLibrary(read, Revision(read)), "1024", "1025-preset save");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "count-rejected save preserves every original byte");
            read.Remove(CustomPlanStore.ExternalUiRevisionField); File.WriteAllText(path, read.ToString());
            var oversized = File.ReadAllBytes(path);
            Assert(oversized.Length < 2 * 1024 * 1024, "oversized-count file is below the byte limit so the count check is exercised");
            foreach (var action in new Action[] {
                () => library.List(), () => library.Get("limit_0"), () => library.ReadExternalLibrary(),
                () => library.MigratePlan(Plan(), "blocked"),
                () => library.SaveExternalLibrary(root, JsonFiles.ComputeRevision(oversized)),
                () => new PresetLibraryStore(runtime, plans).ReadExternalLibrary()})
            {
                RejectLimit(action, "1024", "persisted oversized-count library read");
                Assert(oversized.SequenceEqual(File.ReadAllBytes(path)), "count-invalid persisted file is reported, not truncated or replaced");
            }
        }

        private static void ByteBounds()
        {
            const int limit = 2 * 1024 * 1024;
            var runtime = DirectoryFor("byte-bounds"); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            var path = Path.Combine(runtime, "presets.json"); var library = new PresetLibraryStore(runtime, plans);
            var initial = library.ReadExternalLibrary(); var root = LibraryRoot(0); root["CapPresetsAdded"] = true;
            root["futureText"] = new string('\u00e9', 100);
            root["futureText"] = root["futureText"].Value + new string('x', limit - Encoding.UTF8.GetByteCount(root.ToString(4)));
            Assert(root.ToString(4).Length < limit && Encoding.UTF8.GetByteCount(root.ToString(4)) == limit, "boundary fixture measures UTF-8 bytes rather than characters");
            library.SaveExternalLibrary(root, Revision(initial));
            var bytes = File.ReadAllBytes(path);
            Assert(bytes.Length == limit && library.ReadExternalLibrary()["futureText"].Value == root["futureText"].Value, "exact 2 MiB file including unknown multibyte data is readable and writable");
            var read = library.ReadExternalLibrary();
            Assert(Encoding.UTF8.GetByteCount(read.ToString(4)) > limit, "transport revision metadata is outside the file-size budget");
            library.SaveExternalLibrary(read, Revision(read));
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "valid maximum-sized snapshot can be resaved with transport revision attached");
            read = library.ReadExternalLibrary(); read["futureText"] = read["futureText"].Value + "x";
            RejectLimit(() => library.SaveExternalLibrary(read, Revision(read)), "2 MiB", "one-byte-over unknown field save");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "byte-rejected save preserves the exact valid boundary file");
            read.Remove(CustomPlanStore.ExternalUiRevisionField);
            var compact = Encoding.UTF8.GetBytes(read.ToString());
            Assert(compact.Length < limit && Encoding.UTF8.GetByteCount(read.ToString(4)) == limit + 1, "compact fixture fits on disk but normalized output exceeds the limit by one byte");
            File.WriteAllBytes(path, compact);
            RejectLimit(() => library.ReadExternalLibrary(), "2 MiB", "oversized normalized read");
            Assert(compact.SequenceEqual(File.ReadAllBytes(path)), "normalization failure does not rewrite or truncate compact source data");
            var expanded = new byte[limit + 1]; Array.Copy(bytes, expanded, bytes.Length); expanded[limit] = (byte) ' ';
            File.WriteAllBytes(path, expanded);
            foreach (var action in new Action[] {
                () => library.List(), () => library.Get("missing"), () => library.ReadExternalLibrary(),
                () => library.MigratePlan(Plan(), "blocked"),
                () => library.SaveExternalLibrary(root, JsonFiles.ComputeRevision(expanded)),
                () => new PresetLibraryStore(runtime, plans).ReadExternalLibrary()})
            {
                RejectLimit(action, "2 MiB", "persisted one-byte-over library read");
                Assert(expanded.SequenceEqual(File.ReadAllBytes(path)), "byte-invalid persisted file remains intact after rejection");
            }
            var bom = new UTF8Encoding(true).GetPreamble().Concat(bytes).ToArray(); File.WriteAllBytes(path, bom);
            RejectLimit(() => library.ReadExternalLibrary(), "2 MiB", "BOM bytes count toward exact persisted size");
            Assert(bom.SequenceEqual(File.ReadAllBytes(path)), "oversized BOM file is not rewritten to squeeze beneath the limit");
            File.WriteAllBytes(path, bytes);
            Assert(library.ReadExternalLibrary()["futureText"].Value == root["futureText"].Value, "restoring a valid file restores access without a silent empty-library fallback");
        }

        private static void MigrationBounds()
        {
            var runtime = DirectoryFor("migration-count-bound"); var plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            var path = Path.Combine(runtime, "presets.json"); var library = new PresetLibraryStore(runtime, plans);
            var initial = library.ReadExternalLibrary(); library.SaveExternalLibrary(LibraryRoot(1023), Revision(initial));
            var bytes = File.ReadAllBytes(path); var plan = Plan(); plan.PresetMigrationIssues = new[] {"original issue"};
            RejectLimit(() => library.MigratePlan(plan, "overflow"), "1024", "partial migration exceeding count limit");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)) && library.List().Length == 1023, "count-limited migration commits no partial imports");
            Assert(plan.ActivePresetId == "" && plan.Stages[0].PresetId == "" && plan.PresetMigrationIssues.SequenceEqual(new[] {"original issue"}), "count-limited migration rolls back in-memory references and issues");
            var sourcePath = Path.Combine(plans, "legacy.json"); File.WriteAllText(sourcePath, Legacy().ToString()); var source = File.ReadAllBytes(sourcePath);
            RejectLimit(() => library.ReadExternalLibrary(), "1024", "initialization migration exceeding count limit");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)) && source.SequenceEqual(File.ReadAllBytes(sourcePath)), "initialization failure preserves both library and legacy plan bytes");
            File.Delete(sourcePath);
            var reduced = library.ReadExternalLibrary(); reduced["Presets"].Remove(0);
            library.SaveExternalLibrary(reduced, Revision(reduced)); library.MigratePlan(plan, "overflow");
            Assert(library.List().Length == 1024 && library.Get(plan.ActivePresetId) != null && library.Get(plan.Stages[0].PresetId) != null, "migration retries successfully at the inclusive count boundary after space is freed");

            runtime = DirectoryFor("migration-byte-bound"); plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            path = Path.Combine(runtime, "presets.json"); library = new PresetLibraryStore(runtime, plans);
            initial = library.ReadExternalLibrary(); var padded = LibraryRoot(0); padded["CapPresetsAdded"] = true; padded["futureText"] = "";
            padded["futureText"] = new string('x', 2 * 1024 * 1024 - Encoding.UTF8.GetByteCount(padded.ToString(4)));
            library.SaveExternalLibrary(padded, Revision(initial)); bytes = File.ReadAllBytes(path); plan = Plan();
            RejectLimit(() => library.MigratePlan(plan, "overflow"), "2 MiB", "migration exceeding final serialized byte limit");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)) && bytes.Length == 2 * 1024 * 1024, "byte-limited migration preserves the actually valid maximum-sized library");
            Assert(plan.ActivePresetId == "" && plan.Stages[0].PresetId == "" && library.List().Length == 0, "byte-limited migration rolls back references without persisting partial definitions");
            sourcePath = Path.Combine(plans, "legacy.json"); File.WriteAllText(sourcePath, Legacy().ToString()); source = File.ReadAllBytes(sourcePath);
            RejectLimit(() => library.ReadExternalLibrary(), "2 MiB", "refresh migration exceeding byte limit");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)) && source.SequenceEqual(File.ReadAllBytes(sourcePath)), "byte-limited refresh preserves existing library and plan data");

            runtime = DirectoryFor("seed-byte-bound"); plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            var legacy = Legacy(); legacy["LoadoutPresets"][0]["futureText"] = new string('x', 2 * 1024 * 1024);
            sourcePath = Path.Combine(plans, "legacy.json"); File.WriteAllText(sourcePath, legacy.ToString()); source = File.ReadAllBytes(sourcePath);
            library = new PresetLibraryStore(runtime, plans);
            RejectLimit(() => library.List(), "2 MiB", "seeding plus legacy imports exceeding byte limit");
            Assert(!File.Exists(Path.Combine(runtime, "presets.json")) && source.SequenceEqual(File.ReadAllBytes(sourcePath)), "failed seed writes neither an oversized nor a partial library and preserves original plan");
            Assert(Directory.GetFiles(runtime, "*.tmp").Length == 0, "limit failures create no abandoned atomic-write files");
        }

        private static void AdventureDynamicPersistence()
        {
            var runtime = DirectoryFor("adventure-dynamic"); var plans = Path.Combine(runtime, "plans");
            Directory.CreateDirectory(plans);
            var library = new PresetLibraryStore(runtime, plans); var root = library.ReadExternalLibrary();
            var path = Path.Combine(runtime, "presets.json"); var bytes = File.ReadAllBytes(path);
            var preset = library.Get("builtin_adventureDynamic");
            var resolved = preset.ResolveAdventure(1, 7);
            Assert(resolved.Zone == 7 && resolved.Weights[5] == 1 && resolved.Weights[10] == 0, "library dynamic seed resolves only the normalized farming task");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)) && library.Get(preset.Id).AdventureDynamicWeight == 1
                && library.Get(preset.Id).Weights.All(weight => weight == 0), "resolution never persists transient task weights");
            var row = root["Presets"].Children.Single(entry => entry["Id"].Value == preset.Id);
            row["AdventureDynamicWeight"] = 2.5; row["Weights"][0] = 3;
            row["future"] = JsonFiles.ParseObject("{\"large\":9007199254740993,\"exponent\":1.2300e+004}");
            library.SaveExternalLibrary(root, Revision(root));
            library = new PresetLibraryStore(runtime, plans); preset = library.Get(preset.Id);
            Assert(preset.AdventureDynamicWeight == 2.5 && preset.Weights[0] == 3 && preset.Weights.Skip(1).All(weight => weight == 0), "edited dynamic/fixed blend survives save and restart");
            Assert(preset.ToJson()["future"].ToString() == row["future"].ToString(), "dynamic library edit preserves unknown numeric tokens");
            root = library.ReadExternalLibrary(); row = root["Presets"].Children.Single(entry => entry["Id"].Value == preset.Id);
            row["AdventureDynamicWeight"] = 0;
            library.SaveExternalLibrary(root, Revision(root));
            var saved = JsonFiles.ParseObject(File.ReadAllText(path))["Presets"].Children.Single(entry => entry["Id"].Value == preset.Id);
            Assert(!saved.Linq.Any(field => field.Key == "AdventureDynamicWeight") && library.Get(preset.Id).Weights[0] == 3, "cleared dynamic source field is removed from disk without dropping fixed goals");
            root = library.ReadExternalLibrary(); row = root["Presets"].Children.Single(entry => entry["Id"].Value == preset.Id);
            bytes = File.ReadAllBytes(path); row["Weights"][0] = 0;
            Reject<FormatException>(() => library.SaveExternalLibrary(root, Revision(root)), "library rejects zero fixed and zero dynamic weight");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), "rejected all-zero edit preserves disk bytes");

            runtime = DirectoryFor("historical-farming-seeds"); plans = Path.Combine(runtime, "plans"); Directory.CreateDirectory(plans);
            path = Path.Combine(runtime, "presets.json");
            File.WriteAllText(path, "{\"Version\":1,\"Presets\":["
                + "{\"Id\":\"builtin_pp\",\"Name\":\"Edited PP\",\"Weights\":[0,0,0,2,0,0]},"
                + "{\"Id\":\"builtin_boosts\",\"Name\":\"Boosts\",\"Weights\":[0,0,0,0,1,0]},"
                + "{\"Id\":\"builtin_zone\",\"Name\":\"Zone\",\"Weights\":[0,0,0,0,0,1]}]}");
            bytes = File.ReadAllBytes(path); library = new PresetLibraryStore(runtime, plans);
            Assert(library.List().Select(entry => entry.Id).SequenceEqual(new[] {"builtin_pp", "builtin_boosts", "builtin_zone", "builtin_energyCap", "builtin_magicCap"})
                && library.Get("builtin_pp").Name == "Edited PP" && library.Get("builtin_pp").Weights[3] == 2,
                "new seed catalog never replaces saved historical farming IDs or user edits");
            Assert(library.Get("builtin_adventureDynamic") == null && !bytes.SequenceEqual(File.ReadAllBytes(path)), "historical catalog receives only the one-time cap addition, not replacement farming seeds");
            bytes = File.ReadAllBytes(path);
            Assert(new PresetLibraryStore(runtime, plans).List().Length == 5 && bytes.SequenceEqual(File.ReadAllBytes(path)), "historical catalog addition is durable and restart is read-only");
            var plan = Plan(0); plan.ActivePresetId = "builtin_pp"; plan.Stages[0].PresetId = "builtin_boosts";
            var store = new CustomPlanStore(plans); store.Save("historical", plan);
            Assert(store.Load("historical").ActivePresetId == "builtin_pp" && store.Load("historical").Stages[0].PresetId == "builtin_boosts", "historical farming references remain valid");
        }

        private static void SharedDefinitions() { PresetDefinitionChecks.Run(Assert); }

        private static int Main()
        {
            _root = Path.Combine(Path.GetTempPath(), "ngu-preset-library-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root);
            try
            {
                GlobalNguSettingsChecks.Run(Assert, DirectoryFor);
                Console.WriteLine("PASS GlobalNguSettingsChecks");
                foreach (var test in new Action[] {SharedDefinitions, SeedOnce, CapCatalogAddition, ResourcePriorityCatalogUpgrade, ResourcePriorityEditsPreserved, SavedBalancedAncestry, CapAdditionBounds, AdventureDynamicPersistence, LosslessCas, SchemaPersistence, MigrationReadOnly, StableCollisionIdentity, PersistedReceiptUpgrade, ExplicitSupportReceiptIsolation, RatioFreeTwelveReceiptIdentity, PreviousSchemaIdentityIsolation, PreserveLegacySemantics, ActivityTemplates, ReferenceProtection, ActiveInheritance, TwelveObjectiveBindings, BuiltInRepairSafety, AtomicLibraryFirst, ConcurrentCas, CountBounds, ByteBounds, MigrationBounds})
                {
                    test(); Console.WriteLine("PASS " + test.Method.Name);
                }
                Console.WriteLine("Preset library and global NGU settings checks passed: " + _assertions + " assertions.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { Directory.Delete(_root, true); }
        }
    }
}
