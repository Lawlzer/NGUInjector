using System;
using System.Globalization;
using SimpleJSON;

namespace NGUInjector
{
    [Serializable]
    public class AdventureRule
    {
        public bool Enabled;
        public double MinPower;
        public double MinToughness;
        public double MinRegen;
        public int Mode;
        public int Zone;

        public bool Matches(double power, double toughness, double regen)
        {
            return Enabled && power >= MinPower && toughness >= MinToughness && regen >= MinRegen;
        }

        public static AdventureRule[] Parse(JSONNode node)
        {
            if (node == null || node.IsNull) return new AdventureRule[0];
            if (!node.IsArray || node.Count > 32)
                throw new FormatException("Adventure rules must be an array of at most 32 records.");
            var rules = new AdventureRule[node.Count];
            for (var i = 0; i < rules.Length; i++)
            {
                var row = node[i];
                if (!row.IsObject || row.Count != 6 || !row["Enabled"].IsBoolean
                    || !row["MinPower"].IsNumber || !row["MinToughness"].IsNumber
                    || !row["MinRegen"].IsNumber || !row["Mode"].IsNumber || !row["Zone"].IsNumber)
                    throw new FormatException("Adventure rules require exactly Enabled, MinPower, MinToughness, MinRegen, Mode and Zone with their declared types.");
                var rule = new AdventureRule {
                    Enabled = row["Enabled"].AsBool,
                    MinPower = row["MinPower"].AsDouble,
                    MinToughness = row["MinToughness"].AsDouble,
                    MinRegen = row["MinRegen"].AsDouble,
                    Mode = ReadInteger(row["Mode"]),
                    Zone = ReadInteger(row["Zone"])
                };
                Validate(rule);
                rules[i] = rule;
            }
            return rules;
        }

        public static JSONArray ToJson(AdventureRule[] rules)
        {
            var result = new JSONArray();
            if (rules == null) return result;
            if (rules.Length > 32)
                throw new FormatException("Adventure rules must contain at most 32 records.");
            foreach (var rule in rules)
            {
                Validate(rule);
                var row = new JSONObject();
                row["Enabled"] = rule.Enabled;
                row["MinPower"] = rule.MinPower;
                row["MinToughness"] = rule.MinToughness;
                row["MinRegen"] = rule.MinRegen;
                row["Mode"] = rule.Mode;
                row["Zone"] = rule.Zone;
                result.Add(row);
            }
            return result;
        }

        private static void Validate(AdventureRule rule)
        {
            // Inclusive finite bounds reject NaN as well as either infinity.
            if (rule == null || !(rule.MinPower >= 0d && rule.MinPower <= double.MaxValue)
                || !(rule.MinToughness >= 0d && rule.MinToughness <= double.MaxValue)
                || !(rule.MinRegen >= 0d && rule.MinRegen <= double.MaxValue))
                throw new FormatException("Adventure rule minimums must be finite nonnegative numbers.");
            if (rule.Mode != 0 && rule.Mode != 1 && rule.Mode != 2 && rule.Mode != 4)
                throw new FormatException("Adventure rule Mode must be 0, 1, 2 or 4.");
            if (rule.Zone != 1000 && (rule.Zone < -1 || rule.Zone > 43))
                throw new FormatException("Adventure rule Zone must be between -1 and 43, or 1000.");
        }

        private static int ReadInteger(JSONNode node)
        {
            var number = node.AsDouble;
            if (double.IsNaN(number) || double.IsInfinity(number) || number < int.MinValue
                || number > int.MaxValue || number != Math.Truncate(number))
                throw new FormatException("Adventure rule Mode and Zone must be whole Int32 numbers.");

            // Check the token too: double conversion can round fractional JSON to an integer.
            var parts = node.Value.Split(new[] {'e', 'E'});
            var point = parts[0].IndexOf('.');
            var digits = parts[0].Replace(".", "").TrimStart('-').TrimStart('0');
            if (digits.Length == 0) return 0;
            var significant = digits.TrimEnd('0');
            var fractional = point < 0 ? 0 : parts[0].Length - point - 1;
            var adjustment = (long) digits.Length - significant.Length - fractional;
            long exponent = 0;
            if (parts.Length > 1 && !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out exponent)
                || exponent < -adjustment || exponent > 10 - significant.Length - adjustment)
                throw new FormatException("Adventure rule Mode and Zone must be whole Int32 numbers.");
            int result;
            var integer = (parts[0][0] == '-' ? "-" : "") + significant + new string('0', (int) (exponent + adjustment));
            if (!int.TryParse(integer, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
                throw new FormatException("Adventure rule Mode and Zone must be whole Int32 numbers.");
            return result;
        }
    }
}
