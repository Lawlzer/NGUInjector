using System;
using System.IO;
using System.Linq;
using SimpleJSON;

namespace NGUInjector.CustomPlans
{
    // Shared definition data, separate from persisted libraries and migration identities.
    internal static class PresetDefinitions
    {
        internal const string ResourceName = "NGUInjector.PresetDefinitions.json";
        private static readonly Lazy<JSONObject> Definitions = new Lazy<JSONObject>(() => Load(ResourceName));
        private static readonly Lazy<JSONObject> ActivityPolicy = new Lazy<JSONObject>(() => Load("NGUInjector.ActivityPolicies.json"));

        private static JSONObject Load(string resourceName)
        {
            using (var stream = typeof(PresetDefinitions).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded resource '" + resourceName + "'.");
                using (var reader = new StreamReader(stream)) return JsonFiles.ParseObject(reader.ReadToEnd());
            }
        }

        internal static JSONObject DefaultJson()
        {
            return JsonFiles.ParseObject(Definitions.Value["defaults"].ToString());
        }

        internal static string[] ObjectiveKeys { get { return Definitions.Value["objectives"].Children.Select(row => row["key"].Value).ToArray(); } }
        internal static string[] ObjectiveLabels { get { return Definitions.Value["objectives"].Children.Select(row => row["label"].Value).ToArray(); } }
        internal static string[] BeardNames { get { return Definitions.Value["beards"].Children.Select(row => row["name"].Value).ToArray(); } }
        internal static string[] DiggerNames { get { return Definitions.Value["diggers"].Children.Select(row => row["name"].Value).ToArray(); } }

        internal static double[] DefaultDiggerWeights(double[] normalizedWeights, bool minimumAdventure)
        {
            if (normalizedWeights == null || normalizedWeights.Length != 14)
                throw new ArgumentException("Expected fourteen normalized objective weights.", nameof(normalizedWeights));
            return Definitions.Value["diggers"].Children.Select(row =>
                row["objectiveWeights"].Children.Select((coefficient, i) => coefficient.AsDouble * normalizedWeights[i]).Sum()
                + (minimumAdventure ? row["minimumAdventureWeight"].AsDouble : 0)).ToArray();
        }

        internal static JSONArray SeedRows()
        {
            var rows = new JSONArray();
            foreach (var definition in Definitions.Value["presets"].Children) rows.Add(NewDefinition(definition));
            return rows;
        }

        internal static JSONObject NewPresetJson()
        {
            return NewDefinition(Definitions.Value["newPreset"]);
        }

        internal static JSONObject BeforeResourcePriorities(string id, bool explicitBeards)
        {
            // Frozen normalized pre-priority catalog. Do not derive migration eligibility from live defaults or policy.
            var row = JsonFiles.ParseObject("{\"Weights\":[0,0,0,0,0,0,0,0,0,0,0,0,0,0],\"AutoUpdate\":true,"
                + "\"AdvancedTrainingWeights\":[1,1,0,0,0],\"MagicBloodPercent\":-1,\"SupportMode\":\"planned\","
                + "\"DiggerBudgetPercent\":75,\"Zone\":-1,\"MinimumPower\":0,\"MinimumToughness\":0,\"SwapSeconds\":5,"
                + "\"BeardPriority\":[],\"DiggerWeights\":[]}");
            switch (id)
            {
                case "builtin_energyNgu": row["Name"] = "Energy NGU"; row["Weights"][0] = 1; break;
                case "builtin_magicNgu": row["Name"] = "Magic NGU"; row["Weights"][1] = 1; break;
                case "builtin_blood": row["Name"] = "Blood"; row["Weights"][2] = 1; break;
                case "builtin_adventureDynamic": row["Name"] = "adventure-dynamic"; row["AdventureDynamicWeight"] = 1; break;
                case "builtin_timeMachine": row["Name"] = "Time Machine"; row["Weights"][6] = 1; break;
                case "builtin_augments": row["Name"] = "Augments"; row["Weights"][7] = 1; break;
                case "builtin_wandoos": row["Name"] = "Wandoos"; row["Weights"][8] = 1; break;
                case "builtin_advancedTraining": row["Name"] = "Advanced Training"; row["Weights"][9] = 1; break;
                case "builtin_adventurePower": row["Name"] = "Adventure Power"; row["Weights"][10] = 1; break;
                case "builtin_adventureToughness": row["Name"] = "Adventure Toughness"; row["Weights"][11] = 1; break;
                case "builtin_allNgu": row["Name"] = "All NGU"; row["Weights"][0] = row["Weights"][1] = 1; break;
                case "builtin_augmentsBlood": row["Name"] = "Augments + Blood"; row["Weights"][2] = row["Weights"][7] = 1; break;
                case "builtin_atAdventure": row["Name"] = "AT Adventure"; row["Weights"][2] = row["Weights"][9] = 1; break;
                case "builtin_atWandoos":
                    row["Name"] = "AT Wandoos"; row["Weights"][8] = row["Weights"][9] = 1;
                    row["AdvancedTrainingWeights"] = JSON.Parse("[0,0,0,1,1]"); break;
                case "builtin_adventureStats": row["Name"] = "Adventure Stats"; row["Weights"][10] = row["Weights"][11] = 1; break;
                case "builtin_energyCap": row["Name"] = "Energy Cap"; row["Weights"][12] = 1; row["SupportMode"] = "current"; break;
                case "builtin_magicCap": row["Name"] = "Magic Cap"; row["Weights"][13] = 1; row["SupportMode"] = "current"; break;
                default: return null;
            }
            row["Id"] = id;
            if (explicitBeards)
            {
                row["BeardPriority"] = JSON.Parse("[1,3,5,6]");
                row["BeardRotation"] = JSON.Parse("[0,2,4]");
            }
            return row;
        }

        private static JSONObject NewDefinition(JSONNode definition)
        {
            var row = DefaultJson();
            foreach (var field in JsonFiles.ParseObject(definition.ToString())) row[field.Key] = field.Value;
            while (row["Weights"].Count < 14) row["Weights"].Add(0);
            // ActivityPolicies owns the current core/pool; never apply these choices to old rows.
            row["BeardPriority"] = JSON.Parse(ActivityPolicy.Value["coreBeards"].ToString());
            row["BeardRotation"] = JSON.Parse(ActivityPolicy.Value["optionalBeards"].ToString());
            return row;
        }
    }
}
