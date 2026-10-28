using System;
using System.IO;
using System.Linq;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    internal static class PresetDefinitionChecks
    {
        internal static void Run(Action<bool, string> assert)
        {
            JSONObject definitions, policy;
            using (var reader = new StreamReader(typeof(PresetDefinitions).Assembly.GetManifestResourceStream(PresetDefinitions.ResourceName)))
                definitions = JsonFiles.ParseObject(reader.ReadToEnd());
            using (var reader = new StreamReader(typeof(PresetDefinitions).Assembly.GetManifestResourceStream("NGUInjector.ActivityPolicies.json")))
                policy = JsonFiles.ParseObject(reader.ReadToEnd());

            assert(definitions["version"].AsInt == 1, "shared definition version");
            var defaults = PresetDefinitions.DefaultJson();
            assert(defaults.Count == 12 && defaults["SupportMode"].Value == "planned", "ratio-free defaults use planned support");
            assert(defaults["Weights"].Count == 14 && defaults["Weights"].Children.Skip(12).All(weight => weight.AsDouble == 0), "fourteen-lane defaults leave caps opt-in");
            assert(!defaults.Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights"), "preset definitions have no NGU ratio defaults");
            assert(!defaults.Linq.Any(field => new[] {"ResourcePrioritiesReviewed", "EnergyPriorities", "MagicPriorities"}.Contains(field.Key)),
                "resource priorities are not global defaults");
            assert(!defaults.Linq.Any(field => field.Key == "BeardRotation" || field.Key == "HorizonSeconds") && defaults["BeardPriority"].Count == 0,
                "defaults do not change legacy beard identity or add a horizon");
            var minimal = CustomLoadoutPreset.Parse(JsonFiles.ParseObject("{\"Id\":\"minimal\",\"Name\":\"Minimal\"}")).ToJson();
            assert(!minimal.Linq.Any(field => new[] {"ResourcePrioritiesReviewed", "EnergyPriorities", "MagicPriorities"}.Contains(field.Key)),
                "resource priorities stay opt-in for legacy normalization");
            foreach (var field in defaults)
                assert(minimal[field.Key].ToString() == field.Value.ToString(), "model default parity: " + field.Key);
            defaults["Weights"][0] = 99;
            assert(PresetDefinitions.DefaultJson()["Weights"][0].AsDouble == 1, "defaults are deep clones");

            assert(PresetDefinitions.ObjectiveKeys.SequenceEqual(new[] {"energyNgu", "magicNgu", "blood", "pp", "boosts", "zone", "timeMachine", "augments", "wandoos", "advancedTraining", "adventurePower", "adventureToughness", "energyCap", "magicCap"}), "stable objective order with appended caps");
            assert(PresetDefinitions.ObjectiveLabels.SequenceEqual(definitions["objectives"].Children.Select(row => row["label"].Value)), "objective labels match shared data");
            assert(PresetDefinitions.DiggerNames.SequenceEqual(new[] {"Drop", "Wandoos", "Stats", "Adventure", "Energy NGU", "Magic NGU", "Energy beards", "Magic beards", "PP", "Daycare", "Blood", "EXP"}), "stable digger names");
            assert(PresetDefinitions.BeardNames.SequenceEqual(new[] {"Fu Manchu", "Drop chance", "NUMBER", "NGU", "Ladybeard", "Adventure", "Gold"}), "stable beard names");
            assert(PresetDefinitions.BeardNames.SequenceEqual(definitions["beards"].Children.Select(row => row["name"].Value)), "beard names match shared data");
            var keys = PresetDefinitions.ObjectiveKeys; keys[0] = "changed";
            var labels = PresetDefinitions.ObjectiveLabels; labels[0] = "changed";
            var names = PresetDefinitions.DiggerNames; names[0] = "changed";
            var beardNames = PresetDefinitions.BeardNames; beardNames[1] = "changed";
            assert(PresetDefinitions.BeardNames[1] == "Drop chance", "beard names are defensive copies");
            assert(PresetDefinitions.ObjectiveKeys[0] == "energyNgu" && PresetDefinitions.ObjectiveLabels[0] == "Energy NGU" && PresetDefinitions.DiggerNames[0] == "Drop", "metadata arrays are defensive copies");
            assert(definitions["beards"].Children.Select(row => row["id"].AsInt).SequenceEqual(Enumerable.Range(0, 7))
                && definitions["beards"].Children.Select(row => row["name"].Value).SequenceEqual(new[] {"Fu Manchu", "Drop chance", "NUMBER", "NGU", "Ladybeard", "Adventure", "Gold"}), "ordered beard catalog");
            assert(definitions["diggers"].Children.Select(row => row["id"].AsInt).SequenceEqual(Enumerable.Range(0, 12))
                && definitions["diggers"].Children.All(row => row["objectiveWeights"].Count == 14
                    && row["objectiveWeights"].Children.Skip(12).All(weight => weight.AsDouble == 0)), "ordered twelve-by-fourteen digger coefficients with no cap weights");

            for (var lane = 0; lane < 14; lane++)
            foreach (var minimum in new[] {false, true})
            {
                var weights = new double[14]; weights[lane] = 1;
                var expected = new[] {weights[4] + weights[5], weights[8], 0,
                    weights[3] + weights[4] + weights[5] + weights[10] + weights[11] + (minimum ? 1 : 0),
                    weights[0], weights[1], weights[0] * .2, weights[1] * .2, weights[3], 0, weights[2], 0};
                assert(PresetDefinitions.DefaultDiggerWeights(weights, minimum).SequenceEqual(expected), "legacy digger formula for lane " + lane + ", minimum=" + minimum);
            }
            var mixed = Enumerable.Range(1, 14).Select(value => value / 105d).ToArray();
            var actual = PresetDefinitions.DefaultDiggerWeights(mixed, true);
            assert(Math.Abs(actual[3] - (mixed[3] + mixed[4] + mixed[5] + mixed[10] + mixed[11] + 1)) < 1e-14
                && actual[6] == mixed[0] * .2 && actual[7] == mixed[1] * .2, "mixed normalized objectives retain additive digger formula");

            var seeds = PresetDefinitions.SeedRows();
            assert(seeds.Count == 17, "shared catalog ships exactly seventeen seeds");
            foreach (var i in new[] {0, 1, 2, 6, 7, 8, 9, 10, 11, 12, 13})
            {
                var seed = seeds.Children.Single(row => row["Id"].Value == "builtin_" + definitions["objectives"][i]["key"].Value);
                assert(seed["Name"].Value == definitions["objectives"][i]["seedName"].Value, "specialist seed identity " + i);
            }
            assert(seeds.Children.Select(row => row["Id"].Value).Distinct().Count() == 17, "seed IDs are unique");
            foreach (var i in new[] {12, 13})
            {
                var id = "builtin_" + PresetDefinitions.ObjectiveKeys[i];
                var seed = seeds.Children.Single(row => row["Id"].Value == id);
                var raw = definitions["presets"].Children.Single(row => row["Id"].Value == id);
                assert(seed["Weights"].Count == 14 && seed["Weights"][i].AsDouble == 1
                    && seed["Weights"].Children.Where((weight, index) => index != i).All(weight => weight.AsDouble == 0), "exclusive cap seed " + id);
                assert(raw["AutoUpdate"].IsBoolean && raw["AutoUpdate"].AsBool && raw["SupportMode"].Value == "current"
                    && seed["AutoUpdate"].AsBool && seed["SupportMode"].Value == "current", "cap seeds explicitly auto-update and keep current support " + id);
                assert(seed["DiggerWeights"].Count == 0 && !seed.Linq.Any(field => field.Key == "AdventureDynamicWeight"), "cap seeds do not add digger or dynamic goals " + id);
            }
            assert(seeds.Children.Where(row => row["Id"].Value != "builtin_energyCap" && row["Id"].Value != "builtin_magicCap")
                .All(row => row["SupportMode"].Value == "planned" && row["Weights"].Children.Skip(12).All(weight => weight.AsDouble == 0)), "non-cap seeds retain support and exclude cap goals");
            var dynamicSeed = seeds.Children.Single(row => row["Id"].Value == "builtin_adventureDynamic");
            assert(dynamicSeed["Name"].Value == "adventure-dynamic" && dynamicSeed["AdventureDynamicWeight"].AsDouble == 1
                && dynamicSeed["Weights"].Count == 14 && dynamicSeed["Weights"].Children.All(weight => weight.AsDouble == 0), "dynamic seed has fourteen zero fixed lanes and unit dynamic weight");
            assert(!seeds.Children.Any(row => new[] {"builtin_pp", "builtin_boosts", "builtin_zone", "builtin_balanced"}.Contains(row["Id"].Value)), "fresh catalog excludes historical farming and Balanced seeds");
            assert(seeds.Children.Single(row => row["Id"].Value == "builtin_augmentsBlood")["Weights"].ToString() == "[0,0,1,0,0,0,0,1,0,0,0,0,0,0]"
                && seeds.Children.Single(row => row["Id"].Value == "builtin_atAdventure")["Weights"].ToString() == "[0,0,1,0,0,0,0,0,0,1,0,0,0,0]"
                && seeds.Children.Single(row => row["Id"].Value == "builtin_atWandoos")["Weights"].ToString() == "[0,0,0,0,0,0,0,0,1,1,0,0,0,0]", "composite seeds retain blood/AT/Wandoos objective semantics");
            foreach (var row in definitions["presets"].Children.Concat(new[] {definitions["newPreset"]}))
            {
                var energy = "NGU,WAN,TM,AUG,AT"; var magic = "NGU,RIT,WAN,TM";
                switch (row["Id"].Value)
                {
                    case "builtin_timeMachine": energy = "TM,NGU,WAN,AUG,AT"; magic = "TM,RIT,NGU,WAN"; break;
                    case "builtin_augments": energy = "AUG,NGU,WAN,TM,AT"; break;
                    case "builtin_blood": magic = "RIT,NGU,WAN,TM"; break;
                    case "builtin_wandoos": energy = "WAN,NGU,TM,AUG,AT"; magic = "WAN,RIT,NGU,TM"; break;
                    case "builtin_advancedTraining": energy = "AT,NGU,WAN,TM,AUG"; break;
                    case "builtin_augmentsBlood": energy = "AUG,NGU,WAN,TM,AT"; magic = "RIT,NGU,WAN,TM"; break;
                    case "builtin_atAdventure": energy = "AT,NGU,WAN,TM,AUG"; magic = "RIT,NGU,WAN,TM"; break;
                    case "builtin_atWandoos": energy = "AT,WAN,NGU,TM,AUG"; magic = "RIT,NGU,TM"; break;
                }
                assert(row["ResourcePrioritiesReviewed"].IsBoolean && row["ResourcePrioritiesReviewed"].AsBool
                    && row["EnergyPriorities"].Children.Select(token => token.Value).SequenceEqual(energy.Split(','))
                    && row["MagicPriorities"].Children.Select(token => token.Value).SequenceEqual(magic.Split(',')), "explicit reviewed catalog lane order: " + row["Id"].Value);
            }
            foreach (var prior in BeforeResourcePriorityRows(true, true).Children)
            {
                var current = JsonFiles.ParseObject(seeds.Children.Single(row => row["Id"].Value == prior["Id"].Value).ToString());
                foreach (var field in new[] {"ResourcePrioritiesReviewed", "EnergyPriorities", "MagicPriorities"}) current.Remove(field);
                assert(current.Count == prior.Count && prior.Linq.All(field => current[field.Key].ToString() == field.Value.ToString()),
                    "catalog setup, weights and all non-priority fields remain exactly unchanged: " + prior["Id"].Value);
            }
            foreach (var row in definitions["presets"].Children.Concat(new[] {definitions["newPreset"]}))
                assert(!row.Linq.Any(field => field.Key == "BeardPriority" || field.Key == "BeardRotation"), "raw definitions do not duplicate authoritative beard policy");
            foreach (var row in seeds.Children.Concat(new[] {PresetDefinitions.NewPresetJson()}))
                assert(row["BeardPriority"].ToString() == policy["coreBeards"].ToString()
                    && row["BeardRotation"].ToString() == policy["optionalBeards"].ToString(), "new definitions agree with authoritative beard policy");
            foreach (var row in seeds.Children)
            {
                var parsed = CustomLoadoutPreset.Parse(row).ToJson();
                foreach (var field in row) assert(parsed[field.Key].ToString() == field.Value.ToString(), "seed serialization parity: " + row["Id"].Value + "/" + field.Key);
            }
            seeds[0]["Weights"][0] = 99;
            seeds[0]["BeardPriority"][0] = 0;
            assert(PresetDefinitions.SeedRows()[0]["Weights"][0].AsDouble == 1 && PresetDefinitions.SeedRows()[1]["BeardPriority"][0].AsInt == 1, "seed rows are independent deep clones");
            var fresh = PresetDefinitions.NewPresetJson();
            assert(fresh["Name"].Value == "All NGU" && fresh["Weights"].ToString() == PresetDefinitions.DefaultJson()["Weights"].ToString()
                && !fresh.Linq.Any(field => field.Key == "Id"), "new preset uses All NGU ratios and leaves ID generation to caller");
            fresh["BeardRotation"][0] = 6;
            assert(PresetDefinitions.NewPresetJson()["BeardRotation"].ToString() == policy["optionalBeards"].ToString(), "new preset beard pools are deep clones");
        }

        internal static JSONArray BeforeResourcePriorityRows(bool full, bool explicitBeards)
        {
            // Independent historical fixture, not reconstructed from the live catalog or migration matcher.
            var rows = JSON.Parse(@"[
                {""Id"":""builtin_energyNgu"",""Name"":""Energy NGU"",""Weights"":[1,0,0,0,0,0,0,0,0,0,0,0]},
                {""Id"":""builtin_magicNgu"",""Name"":""Magic NGU"",""Weights"":[0,1,0,0,0,0,0,0,0,0,0,0]},
                {""Id"":""builtin_blood"",""Name"":""Blood"",""Weights"":[0,0,1,0,0,0,0,0,0,0,0,0]},
                {""Id"":""builtin_adventureDynamic"",""Name"":""adventure-dynamic"",""AdventureDynamicWeight"":1,""Weights"":[0,0,0,0,0,0,0,0,0,0,0,0]},
                {""Id"":""builtin_timeMachine"",""Name"":""Time Machine"",""Weights"":[0,0,0,0,0,0,1,0,0,0,0,0]},
                {""Id"":""builtin_augments"",""Name"":""Augments"",""Weights"":[0,0,0,0,0,0,0,1,0,0,0,0]},
                {""Id"":""builtin_wandoos"",""Name"":""Wandoos"",""Weights"":[0,0,0,0,0,0,0,0,1,0,0,0]},
                {""Id"":""builtin_advancedTraining"",""Name"":""Advanced Training"",""Weights"":[0,0,0,0,0,0,0,0,0,1,0,0]},
                {""Id"":""builtin_adventurePower"",""Name"":""Adventure Power"",""Weights"":[0,0,0,0,0,0,0,0,0,0,1,0]},
                {""Id"":""builtin_adventureToughness"",""Name"":""Adventure Toughness"",""Weights"":[0,0,0,0,0,0,0,0,0,0,0,1]},
                {""Id"":""builtin_allNgu"",""Name"":""All NGU"",""Weights"":[1,1,0,0,0,0,0,0,0,0,0,0]},
                {""Id"":""builtin_augmentsBlood"",""Name"":""Augments + Blood"",""Weights"":[0,0,1,0,0,0,0,1,0,0,0,0]},
                {""Id"":""builtin_atAdventure"",""Name"":""AT Adventure"",""Weights"":[0,0,1,0,0,0,0,0,0,1,0,0],""AdvancedTrainingWeights"":[1,1,0,0,0]},
                {""Id"":""builtin_atWandoos"",""Name"":""AT Wandoos"",""Weights"":[0,0,0,0,0,0,0,0,1,1,0,0],""AdvancedTrainingWeights"":[0,0,0,1,1]},
                {""Id"":""builtin_adventureStats"",""Name"":""Adventure Stats"",""Weights"":[0,0,0,0,0,0,0,0,0,0,1,1]},
                {""Id"":""builtin_energyCap"",""Name"":""Energy Cap"",""Weights"":[0,0,0,0,0,0,0,0,0,0,0,0,1,0],""AutoUpdate"":true,""SupportMode"":""current""},
                {""Id"":""builtin_magicCap"",""Name"":""Magic Cap"",""Weights"":[0,0,0,0,0,0,0,0,0,0,0,0,0,1],""AutoUpdate"":true,""SupportMode"":""current""}
            ]").AsArray;
            var result = new JSONArray();
            foreach (var definition in rows.Children)
            {
                var row = full ? JsonFiles.ParseObject("{\"Weights\":[1,1,0,0,0,0,0,0,0,0,0,0,0,0],\"AutoUpdate\":true,"
                    + "\"AdvancedTrainingWeights\":[1,1,0,0,0],\"MagicBloodPercent\":-1,\"SupportMode\":\"planned\",\"DiggerBudgetPercent\":75,"
                    + "\"Zone\":-1,\"MinimumPower\":0,\"MinimumToughness\":0,\"SwapSeconds\":5,\"BeardPriority\":[],\"DiggerWeights\":[]}") : new JSONObject();
                foreach (var field in definition.Linq) row[field.Key] = field.Value;
                if (full) while (row["Weights"].Count < 14) row["Weights"].Add(0);
                if (explicitBeards)
                {
                    row["BeardPriority"] = JSON.Parse("[1,3,5,6]");
                    row["BeardRotation"] = JSON.Parse("[0,2,4]");
                }
                result.Add(row);
            }
            return result;
        }
    }
}
