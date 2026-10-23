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
