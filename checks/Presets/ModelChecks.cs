#if PRESET_MODEL_CHECKS
using System;
using System.Linq;
using NGUInjector;
using NGUInjector.CustomPlans;

internal static partial class Program
{
    private static CustomLoadoutPreset Model(string fields = "")
    { return CustomLoadoutPreset.Parse(JsonFiles.ParseObject("{\"Id\":\"p\",\"Name\":\"Preset\"" + fields + "}")); }

    private static void InvalidModel(string fields)
    {
        try { Model(fields); }
        catch (FormatException) { Check(true, "invalid model rejected " + fields); return; }
        Check(false, "invalid model accepted " + fields);
    }

    private static void ModelChecks()
    {
        var before = assertions;
        var p = Model();
        Check(p.AutoUpdate && p.Weights.Length == 14 && p.Weights.Take(2).All(x => x == 1) && p.Weights.Skip(2).All(x => x == 0), "absent AutoUpdate true and fourteen-lane defaults");
        Check(p.AdvancedTrainingWeights.SequenceEqual(new[] { 1, 1, 0, 0, 0 })
            && typeof(CustomLoadoutPreset).GetField("EnergyNguWeights") == null
            && typeof(CustomLoadoutPreset).GetField("MagicNguWeights") == null, "AT5 defaults and no preset-owned NGU ratios");
        p = Model(",\"AutoUpdate\":false,\"Weights\":[0,0,0,0,0,1],\"EnergyNguWeights\":[0,1,2,3,4,5,6,7,2147483647],\"MagicNguWeights\":[7,6,5,4,3,2,1],\"AdvancedTrainingWeights\":[0,0,0,1,3]");
        var roundTrip = CustomLoadoutPreset.Parse(p.ToJson());
        Check(!roundTrip.AutoUpdate && roundTrip.Weights.Length == 14 && roundTrip.Weights[5] == 1 && roundTrip.Weights.Skip(6).All(x => x == 0), "explicit false retained and persisted old six padded to fourteen");
        Check(!roundTrip.ToJson().Linq.Any(field => field.Key == "EnergyNguWeights" || field.Key == "MagicNguWeights")
            && roundTrip.AdvancedTrainingWeights.SequenceEqual(p.AdvancedTrainingWeights), "NGU ratios retired while AT weights round trip exactly");
        p = Model(",\"Weights\":[0,0,0,0,0,0,1,2,3,4]");
        Check(p.Weights.SequenceEqual(new double[] {0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 0, 0, 0, 0}) && p.ToJson()["Weights"].Count == 14, "ten-goal persisted weights padded with zero adventure and cap weights");
        p = Model(",\"Weights\":[0,0,0,0,0,0,0,0,0,0,2,3]");
        Check(CustomLoadoutPreset.Parse(p.ToJson()).Weights.SequenceEqual(p.Weights) && p.Weights[10] == 2 && p.Weights[11] == 3
            && p.Weights.Skip(12).All(x => x == 0), "adventure power and toughness keep their lanes with zero cap weights");
        foreach (var count in new[] {6, 10, 12, 14})
        {
            p.Weights = Enumerable.Repeat(1d, count).ToArray();
            Check(p.ToJson()["Weights"].Count == 14 && p.Weights.Take(count).All(x => x == 1) && p.Weights.Skip(count).All(x => x == 0), "native weights normalize on serialization " + count);
            var fields = ",\"Weights\":[" + string.Join(",", Enumerable.Range(1, count)) + "]";
            var parsed = Model(fields);
            Check(parsed.Weights.Length == 14 && parsed.Weights.Take(count).SequenceEqual(Enumerable.Range(1, count).Select(x => (double)x))
                && parsed.Weights.Skip(count).All(x => x == 0), "persisted weights normalize without reordering " + count);
            parsed = Model(",\"AdventureDynamicWeight\":0.25,\"Weights\":[" + string.Join(",", Enumerable.Repeat("0", count)) + "]");
            Check(parsed.Weights.SequenceEqual(new double[14]) && parsed.AdventureDynamicWeight == .25, "dynamic-only weights pad to fourteen " + count);
        }
        foreach (var lane in new[] {12, 13})
        {
            foreach (var weight in new[] {0.5, 1000000d})
            {
                p = Model(); p.Weights = new double[14]; p.Weights[lane] = weight;
                roundTrip = CustomLoadoutPreset.Parse(p.ToJson());
                Check(roundTrip.Weights.SequenceEqual(p.Weights), "exclusive cap weight round trips " + lane + "/" + weight);
            }
            foreach (var value in new[] {-1d, 1000001d, double.NaN, double.PositiveInfinity})
            {
                p = Model(); p.Weights[lane] = value;
                var rejected = false;
                try { p.ToJson(); } catch (FormatException) { rejected = true; }
                Check(rejected, "invalid native cap weight rejected " + lane + "/" + value);
            }
            foreach (var token in new[] {"-1", "1000001", "1e309", "null", "true", "\"1\""})
            {
                var weights = Enumerable.Repeat("0", 14).ToArray(); weights[lane] = token;
                InvalidModel(",\"Weights\":[" + string.Join(",", weights) + "]");
            }
        }
        p = Model(",\"DiggerWeights\":[1,2,3,4,5,6,7,8,9,10,11,12]");
        Check(p.DiggerWeights.Length == 12 && CustomLoadoutPreset.Parse(p.ToJson()).DiggerWeights.SequenceEqual(p.DiggerWeights), "digger weights remain twelve lanes");
        InvalidModel(",\"DiggerWeights\":[" + string.Join(",", Enumerable.Repeat("1", 14)) + "]");
        InvalidModel(",\"Weights\":[" + string.Join(",", Enumerable.Repeat("0", 14)) + "]");
        InvalidModel(",\"Weights\":[" + string.Join(",", Enumerable.Repeat("1", 15)) + "]");
        foreach (var value in new[] {"3600", "0", "-1", "604801", "1.5", "null", "\"invalid\"", "true", "[]", "{\"old\":true}"})
        {
            p = Model(",\"HorizonSeconds\":" + value);
            Check(!p.ToJson().Linq.Any(row => row.Key == "HorizonSeconds") && p.SourceJson["HorizonSeconds"].ToString() == value,
                "legacy horizon ignored and removed without mutating source " + value);
        }
        foreach (var value in new[] { "null", "0", "1", "\"false\"", "[]", "{}" }) InvalidModel(",\"AutoUpdate\":" + value);
        foreach (var field in new[] { "EnergyNguWeights", "MagicNguWeights", "AdvancedTrainingWeights" })
        {
            var count = field == "EnergyNguWeights" ? 9 : field == "MagicNguWeights" ? 7 : 5;
            foreach (var value in new[] { "null", "[]", "{}", "[1]" }.Concat(new[] { "-1", "2147483648", "1.5", "1.0000000000000000001", "true", "\"1\"", "null" }
                .Select(token => "[" + token + "," + string.Join(",", Enumerable.Repeat("0", count - 1)) + "]")))
            {
                var fields = ",\"" + field + "\":" + value;
                if (field == "AdvancedTrainingWeights") InvalidModel(fields);
                else Check(!Model(fields).ToJson().Linq.Any(row => row.Key == field), "retired NGU field ignored and stripped even when malformed " + field + "=" + value);
            }
        }
        foreach (var weights in new[] { "[]", "[1,0,0,0,0,0,0]", "[1,0,0,0,0,0,0,0,0,0,0]", "[1,0,0,0,0,0,0,0,0,0,0,0,0]",
            "[0,0,0,0,0,0]", "[0,0,0,0,0,0,0,0,0,0]", "[0,0,0,0,0,0,0,0,0,0,0,0]", "[1,0,0,0,0,0,0,0,0,-1]",
            "[0,0,0,0,0,0,0,0,0,0,-1,1]", "[0,0,0,0,0,0,0,0,0,0,1,1000001]" })
            InvalidModel(",\"Weights\":" + weights);
        var source = JsonFiles.ParseObject("{\"Id\":\"p\",\"Name\":\"Preset\",\"future\":{\"large\":9223372036854775807,\"decimal\":1.234567890123456789,\"exponent\":1.2300e+004,\"nested\":[true,null,{\"x\":\"unchanged\"}]}}");
        var original = source.ToString(); var future = source["future"].ToString();
        p = CustomLoadoutPreset.Parse(source); p.AutoUpdate = false;
        Check(p.ToJson()["future"].ToString() == future && source.ToString() == original, "unknown nested data and numeric lexemes preserved without mutating source");
        Check(p.ToJson()["AutoUpdate"].IsBoolean && !p.ToJson()["AutoUpdate"].AsBool, "serialization emits strict boolean false");
        var legacy = Model().ToJson();
        var nullRotation = Model(",\"BeardRotation\":null").ToJson();
        Check(Model().BeardRotation == null && !nullRotation.Linq.Any(row => row.Key == "BeardRotation")
            && nullRotation.Count == legacy.Count && legacy.Linq.All(row => nullRotation[row.Key].ToString() == row.Value.ToString()),
            "missing and null rotation keep legacy identity and omit the new field");
        p = Model(",\"BeardPriority\":[],\"BeardRotation\":[]");
        Check(p.BeardRotation.Length == 0 && p.ToJson()["BeardRotation"].ToString() == "[]", "explicit no-beards survives serialization");
        p = Model(",\"BeardPriority\":[1,3,5,6],\"BeardRotation\":[0,2,4]");
        Check(CustomLoadoutPreset.Parse(p.ToJson()).BeardRotation.SequenceEqual(new[] { 0, 2, 4 })
            && p.BeardPriority.SequenceEqual(new[] { 1, 3, 5, 6 }), "required and optional order round trips");
        p.SupportMode = "current";
        roundTrip = CustomLoadoutPreset.Parse(p.ToJson());
        Check(roundTrip.SupportMode == "current" && roundTrip.BeardPriority.SequenceEqual(p.BeardPriority)
            && roundTrip.BeardRotation.SequenceEqual(p.BeardRotation), "Keep current retains inactive beard settings for switching back");
        foreach (var value in new[] { "{}", "0", "true", "[0,0]", "[-1]", "[7]", "[1.5]", "[true]", "[\"1\"]", "[null]" })
            InvalidModel(",\"BeardRotation\":" + value);
        InvalidModel(",\"BeardPriority\":[1],\"BeardRotation\":[1]");
        Console.WriteLine("Preset model checks: " + (assertions - before));
        AdventureDynamicChecks();
        LanePriorityModelChecks();
    }

    private static void LanePriorityModelChecks()
    {
        var before = assertions;
        var p = Model();
        Check(p.EnergyPriorities == null && p.MagicPriorities == null && !p.ResourcePrioritiesReviewed,
            "missing priority schema remains legacy and unreviewed");
        Check(!p.ToJson().Linq.Any(row => row.Key == "EnergyPriorities" || row.Key == "MagicPriorities" || row.Key == "ResourcePrioritiesReviewed"),
            "legacy serialization omits all new fields");
        p = Model(",\"EnergyPriorities\":null,\"MagicPriorities\":null,\"ResourcePrioritiesReviewed\":false,\"futurePriority\":{\"keep\":[1,true]}");
        var source = p.SourceJson.ToString();
        Check(p.ToJson()["futurePriority"].ToString() == "{\"keep\":[1,true]}" && p.SourceJson.ToString() == source
            && !p.ToJson().Linq.Any(row => row.Key == "EnergyPriorities" || row.Key == "MagicPriorities")
            && !p.ToJson()["ResourcePrioritiesReviewed"].AsBool, "null legacy arrays preserve unknown fields and source without activation");
        foreach (var reviewed in new[] { false, true })
        {
            p = Model(",\"EnergyPriorities\":[\"NGU\",\"TM\",\"AUG\",\"WAN\",\"AT\"],\"MagicPriorities\":[\"RIT\",\"NGU\",\"TM\",\"WAN\"],\"ResourcePrioritiesReviewed\":" + reviewed.ToString().ToLowerInvariant());
            var copy = CustomLoadoutPreset.Parse(p.ToJson());
            Check(copy.ResourcePrioritiesReviewed == reviewed && copy.EnergyPriorities.SequenceEqual(p.EnergyPriorities)
                && copy.MagicPriorities.SequenceEqual(p.MagicPriorities), "full ordered arrays preserve review state " + reviewed);
            p = Model(",\"EnergyPriorities\":[],\"MagicPriorities\":[],\"ResourcePrioritiesReviewed\":" + reviewed.ToString().ToLowerInvariant());
            Check(p.ToJson()["EnergyPriorities"].ToString() == "[]" && p.ToJson()["MagicPriorities"].ToString() == "[]",
                "explicit controlled-empty arrays persist " + reviewed);
        }
        foreach (var fields in new[] { ",\"EnergyPriorities\":[]", ",\"MagicPriorities\":[]", ",\"EnergyPriorities\":[],\"MagicPriorities\":null",
            ",\"ResourcePrioritiesReviewed\":true", ",\"EnergyPriorities\":null,\"MagicPriorities\":null,\"ResourcePrioritiesReviewed\":true" }) InvalidModel(fields);
        foreach (var field in new[] { "EnergyPriorities", "MagicPriorities" })
        foreach (var value in new[] { "{}", "0", "true", "\"NGU\"", "[null]", "[1]", "[true]", "[\"ngu\"]", "[\"NGU \"]", "[\"NGU\",\"NGU\"]", "[\"HACK\"]", "[\"R3\"]" })
            InvalidModel(",\"EnergyPriorities\":[],\"MagicPriorities\":[],\"" + field + "\":" + value);
        InvalidModel(",\"EnergyPriorities\":[\"RIT\"],\"MagicPriorities\":[]");
        InvalidModel(",\"EnergyPriorities\":[],\"MagicPriorities\":[\"AT\"]");
        InvalidModel(",\"EnergyPriorities\":[],\"MagicPriorities\":[\"AUG\"]");
        foreach (var value in new[] { "null", "0", "1", "\"true\"", "[]", "{}" }) InvalidModel(",\"ResourcePrioritiesReviewed\":" + value);
        p = Model(",\"Weights\":[2,2,4,0,0,0,3,2,3,0,1,1,9,9]");
        var original = p.ToJson().ToString();
        Check(p.ProposeEnergyPriorities().SequenceEqual(new[] { "TM", "WAN", "NGU", "AUG" })
            && p.ProposeMagicPriorities().SequenceEqual(new[] { "RIT", "TM", "WAN", "NGU" }), "proposals sort descending positive weights with stable documented tie order");
        Check(p.ToJson().ToString() == original && !p.ResourcePrioritiesReviewed && p.EnergyPriorities == null && p.MagicPriorities == null,
            "proposal never mutates serialized data or activates strict order");
        Console.WriteLine("Lane priority model checks: " + (assertions - before));
    }
}
#endif
