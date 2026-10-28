using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SimpleJSON;

namespace NGUInjector.CustomPlans
{
    [Serializable]
    public class CustomLoadoutPreset
    {
        [NonSerialized] internal JSONObject SourceJson;
        public string Id;
        public string Name;
        // energyNgu, magicNgu, blood, pp, boosts, zone, timeMachine, augments, wandoos, advancedTraining, adventurePower, adventureToughness, energyCap, magicCap.
        public double[] Weights;
        public string[] EnergyPriorities, MagicPriorities;
        public bool ResourcePrioritiesReviewed;
        public double AdventureDynamicWeight;
        public bool AutoUpdate;
        public int[] AdvancedTrainingWeights;
        public int MagicBloodPercent;
        public string SupportMode;
        public int DiggerBudgetPercent;
        public int Zone;
        public double MinimumPower;
        public double MinimumToughness;
        public int SwapSeconds;
        public int[] BeardPriority;
        // Null is the legacy support policy; even an empty array opts into explicit selection.
        public int[] BeardRotation;
        public double[] DiggerWeights;

        public CustomLoadoutPreset()
        {
            var defaults = PresetDefinitions.DefaultJson();
            Weights = ReadWeights(defaults["Weights"], "Weights");
            AutoUpdate = defaults["AutoUpdate"].AsBool;
            AdvancedTrainingWeights = ReadIntegerWeights(defaults["AdvancedTrainingWeights"], "AdvancedTrainingWeights");
            MagicBloodPercent = defaults["MagicBloodPercent"].AsInt;
            SupportMode = defaults["SupportMode"].Value;
            DiggerBudgetPercent = defaults["DiggerBudgetPercent"].AsInt;
            Zone = defaults["Zone"].AsInt;
            MinimumPower = defaults["MinimumPower"].AsDouble;
            MinimumToughness = defaults["MinimumToughness"].AsDouble;
            SwapSeconds = defaults["SwapSeconds"].AsInt;
            BeardPriority = ReadIntegerWeights(defaults["BeardPriority"], "BeardPriority");
            DiggerWeights = ReadWeights(defaults["DiggerWeights"], "DiggerWeights");
        }

        public static CustomLoadoutPreset Parse(JSONNode node)
        {
            if (ReferenceEquals(node, null) || !node.IsObject)
                throw new FormatException("Loadout preset must be a JSON object.");

            var preset = new CustomLoadoutPreset {SourceJson = node.AsObject};
            foreach (var item in node.Linq)
            {
                var value = item.Value;
                switch (item.Key)
                {
                    case "Id": preset.Id = ReadString(value, item.Key); break;
                    case "Name": preset.Name = ReadString(value, item.Key); break;
                    case "Weights": preset.Weights = ReadWeights(value, item.Key); break;
                    case "EnergyPriorities": preset.EnergyPriorities = ReadPriorities(value, item.Key); break;
                    case "MagicPriorities": preset.MagicPriorities = ReadPriorities(value, item.Key); break;
                    case "ResourcePrioritiesReviewed":
                        if (!value.IsBoolean) throw new FormatException("ResourcePrioritiesReviewed must be a boolean.");
                        preset.ResourcePrioritiesReviewed = value.AsBool;
                        break;
                    case "AdventureDynamicWeight": preset.AdventureDynamicWeight = ReadNumber(value, item.Key); break;
                    case "AutoUpdate":
                        if (!value.IsBoolean) throw new FormatException("AutoUpdate must be a boolean.");
                        preset.AutoUpdate = value.AsBool;
                        break;
                    case "AdvancedTrainingWeights": preset.AdvancedTrainingWeights = ReadIntegerWeights(value, item.Key); break;
                    case "MagicBloodPercent": preset.MagicBloodPercent = ReadInt(value, item.Key); break;
                    case "SupportMode": preset.SupportMode = ReadString(value, item.Key); break;
                    case "DiggerBudgetPercent": preset.DiggerBudgetPercent = ReadInt(value, item.Key); break;
                    case "Zone": preset.Zone = ReadInt(value, item.Key); break;
                    case "MinimumPower": preset.MinimumPower = ReadNumber(value, item.Key); break;
                    case "MinimumToughness": preset.MinimumToughness = ReadNumber(value, item.Key); break;
                    case "SwapSeconds": preset.SwapSeconds = ReadInt(value, item.Key); break;
                    case "BeardPriority":
                        if (!value.IsArray)
                            throw new FormatException("BeardPriority must be an array.");
                        preset.BeardPriority = value.Children.Select(entry => ReadInt(entry, item.Key)).ToArray();
                        break;
                    case "BeardRotation":
                        preset.BeardRotation = value.IsNull ? null : ReadIntegerWeights(value, item.Key);
                        break;
                    case "DiggerWeights": preset.DiggerWeights = ReadWeights(value, item.Key); break;
                }
            }
            Validate(preset);
            return preset;
        }

        public JSONObject ToJson()
        {
            Validate(this);
            var root = JsonFiles.ParseObject(SourceJson?.ToString() ?? "{}");
            root.Remove("HorizonSeconds");
            root.Remove("EnergyNguWeights");
            root.Remove("MagicNguWeights");
            root.Remove("AdventureDynamicWeight");
            root["Id"] = Id;
            root["Name"] = Name;
            var weights = new JSONArray();
            foreach (var weight in Weights) weights.Add(weight);
            root["Weights"] = weights;
            root.Remove("EnergyPriorities"); root.Remove("MagicPriorities"); root.Remove("ResourcePrioritiesReviewed");
            if (EnergyPriorities != null)
            {
                var energy = new JSONArray(); var magic = new JSONArray();
                foreach (var activity in EnergyPriorities) energy.Add(activity);
                foreach (var activity in MagicPriorities) magic.Add(activity);
                root["EnergyPriorities"] = energy; root["MagicPriorities"] = magic;
                root["ResourcePrioritiesReviewed"] = ResourcePrioritiesReviewed;
            }
            else if (SourceJson != null && SourceJson.Linq.Any(row => row.Key == "ResourcePrioritiesReviewed"))
                root["ResourcePrioritiesReviewed"] = false;
            if (AdventureDynamicWeight > 0) root["AdventureDynamicWeight"] = AdventureDynamicWeight;
            root["AutoUpdate"] = AutoUpdate;
            var training = new JSONArray();
            foreach (var weight in AdvancedTrainingWeights) training.Add(weight);
            root["AdvancedTrainingWeights"] = training;
            root["MagicBloodPercent"] = MagicBloodPercent;
            root["SupportMode"] = SupportMode;
            root["DiggerBudgetPercent"] = DiggerBudgetPercent;
            root["Zone"] = Zone;
            root["MinimumPower"] = MinimumPower;
            root["MinimumToughness"] = MinimumToughness;
            root["SwapSeconds"] = SwapSeconds;
            var beards = new JSONArray();
            foreach (var beard in BeardPriority) beards.Add(beard);
            root["BeardPriority"] = beards;
            root.Remove("BeardRotation");
            if (BeardRotation != null)
            {
                var rotation = new JSONArray();
                foreach (var beard in BeardRotation) rotation.Add(beard);
                root["BeardRotation"] = rotation;
            }
            var diggers = new JSONArray();
            foreach (var weight in DiggerWeights) diggers.Add(weight);
            root["DiggerWeights"] = diggers;
            return root;
        }

        // Call with the runtime-normalized task, after rules and locked-zone fallback.
        // A positive dynamic goal overrides Zone for the shared route, even with fixed farming mixtures.
        // Zero dynamic weight leaves the cloned policy, including Zone, unchanged.
        public CustomLoadoutPreset ResolveAdventure(int mode, int zone)
        {
            if (mode == 3)
                throw new InvalidOperationException("Adventure-dynamic resolution for max ITOPOD push is deferred and unsupported.");
            if (mode == 4)
                throw new InvalidOperationException("Adventure-dynamic resolution for manual adventure is deferred and unsupported.");

            // Validation pads legacy arrays; serialize a shallow copy without resizing this instance.
            var resolved = (CustomLoadoutPreset) MemberwiseClone();
            resolved = Parse(resolved.ToJson());
            if (resolved.AdventureDynamicWeight == 0) return resolved;

            var farmingMetric = mode == 1 ? 5 : mode == 2 ? 4 : 3;
            resolved.Weights[farmingMetric] += AdventureDynamicWeight;
            // Keep execution weights serializable without changing normalized objective ratios.
            var maximum = resolved.Weights.Max();
            if (maximum > 1000000)
                for (var i = 0; i < resolved.Weights.Length; i++)
                    resolved.Weights[i] = resolved.Weights[i] / maximum * 1000000;
            resolved.Zone = mode == 1 ? zone : -1;
            resolved.AdventureDynamicWeight = 0;
            return resolved;
        }

        public static void Validate(CustomLoadoutPreset preset)
        {
            if (preset == null)
                throw new FormatException("Loadout preset must not be null.");
            if (preset.Id == null || !Regex.IsMatch(preset.Id, @"\A[A-Za-z0-9_-]{1,64}\z"))
                throw new FormatException("Loadout preset Id must contain 1 to 64 ASCII letters, digits, underscores or hyphens.");
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 100)
                throw new FormatException("Loadout preset Name must be nonempty and at most 100 characters.");
            if (InvalidWeight(preset.AdventureDynamicWeight))
                throw new FormatException("AdventureDynamicWeight must be a finite number from 0 to 1000000.");
            if (preset.Weights == null || preset.Weights.Length != 6 && preset.Weights.Length != 10 && preset.Weights.Length != 12 && preset.Weights.Length != 14 || preset.Weights.Any(InvalidWeight)
                || !preset.Weights.Any(weight => weight > 0) && preset.AdventureDynamicWeight <= 0)
                throw new FormatException("Weights must contain six, ten, twelve or fourteen finite numbers from 0 to 1000000, with at least one positive weight or a positive AdventureDynamicWeight.");
            if (preset.Weights.Length < 14) Array.Resize(ref preset.Weights, 14);
            ValidateResourcePriorities(preset.EnergyPriorities, preset.MagicPriorities, preset.ResourcePrioritiesReviewed);
            ValidateIntegerWeights(preset.AdvancedTrainingWeights, 5, "AdvancedTrainingWeights");
            if (preset.MagicBloodPercent < -1 || preset.MagicBloodPercent > 100)
                throw new FormatException("MagicBloodPercent must be -1 (auto) or between 0 and 100.");
            if (preset.SupportMode != "current" && preset.SupportMode != "planned")
                throw new FormatException("SupportMode must be current or planned.");
            if (preset.DiggerBudgetPercent < 0 || preset.DiggerBudgetPercent > 95)
                throw new FormatException("DiggerBudgetPercent must be between 0 and 95.");
            // Regular game zone IDs end at 43; titan gaps mirror ZoneHelpers without a runtime dependency.
            if (preset.Zone != -1 && (preset.Zone < 0 || preset.Zone > 43
                || new[] {6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42}.Contains(preset.Zone)))
                throw new FormatException("Zone must be -1 (auto) or a regular non-titan game zone ID.");
            if (InvalidNonnegative(preset.MinimumPower) || InvalidNonnegative(preset.MinimumToughness))
                throw new FormatException("MinimumPower and MinimumToughness must be finite nonnegative numbers.");
            if (preset.SwapSeconds < 0 || preset.SwapSeconds > 3600)
                throw new FormatException("SwapSeconds must be between 0 and 3600.");
            if (preset.BeardPriority == null || preset.BeardPriority.Any(id => id < 0 || id > 6)
                || new HashSet<int>(preset.BeardPriority).Count != preset.BeardPriority.Length)
                throw new FormatException("BeardPriority must be empty (auto) or contain unique IDs from 0 to 6.");
            if (preset.BeardRotation != null && (preset.BeardRotation.Any(id => id < 0 || id > 6)
                || new HashSet<int>(preset.BeardRotation).Count != preset.BeardRotation.Length
                || preset.BeardRotation.Intersect(preset.BeardPriority).Any()))
                throw new FormatException("BeardRotation must contain unique IDs from 0 to 6, disjoint from BeardPriority.");
            if (preset.DiggerWeights == null || preset.DiggerWeights.Length != 0 && preset.DiggerWeights.Length != 12
                || preset.DiggerWeights.Any(InvalidWeight))
                throw new FormatException("DiggerWeights must be empty (auto) or contain twelve finite numbers from 0 to 1000000.");
        }

        private static bool InvalidNonnegative(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0;
        }

        public static void ValidateResourcePriorities(string[] energy, string[] magic, bool reviewed)
        {
            if ((energy == null) != (magic == null) || reviewed && energy == null)
                throw new FormatException("Resource priorities require both lane arrays; reviewed priorities cannot be null.");
            ValidatePriorityOrder(energy, new[] { "NGU", "TM", "AUG", "WAN", "AT" }, "EnergyPriorities");
            ValidatePriorityOrder(magic, new[] { "NGU", "RIT", "TM", "WAN" }, "MagicPriorities");
        }

        internal bool ControlsResourceLane(bool energy)
        {
            var objectives = energy ? new[] { 0, 6, 7, 8, 9 } : new[] { 1, 2, 6, 8 };
            var priorities = energy ? EnergyPriorities : MagicPriorities;
            return objectives.Any(i => Weights != null && i < Weights.Length && Weights[i] > 0)
                || ResourcePrioritiesReviewed && priorities != null && priorities.Length > 0;
        }

        private static void ValidatePriorityOrder(string[] order, string[] allowed, string key)
        {
            if (order != null && (order.Length > allowed.Length || order.Any(token => !allowed.Contains(token))
                || order.Distinct(StringComparer.Ordinal).Count() != order.Length))
                throw new FormatException(key + " must contain unique uppercase activity tokens: " + string.Join(", ", allowed) + ".");
        }

        // Review proposals only. Ties follow the documented token order above; this never activates a policy.
        public string[] ProposeEnergyPriorities() { return ProposePriorities(new[] { "NGU", "TM", "AUG", "WAN", "AT" }, new[] { 0, 6, 7, 8, 9 }); }
        public string[] ProposeMagicPriorities() { return ProposePriorities(new[] { "NGU", "RIT", "TM", "WAN" }, new[] { 1, 2, 6, 8 }); }

        private string[] ProposePriorities(string[] tokens, int[] indices)
        {
            return tokens.Select((token, i) => new { Token = token, Weight = Weights != null && indices[i] < Weights.Length ? Weights[indices[i]] : 0 })
                .Where(row => row.Weight > 0 && !InvalidWeight(row.Weight)).OrderByDescending(row => row.Weight).Select(row => row.Token).ToArray();
        }

        private static string[] ReadPriorities(JSONNode node, string key)
        {
            if (node.IsNull) return null;
            if (!node.IsArray) throw new FormatException(key + " must be an array or null.");
            return node.Children.Select(entry => ReadString(entry, key)).ToArray();
        }

        private static bool InvalidWeight(double value)
        {
            return InvalidNonnegative(value) || value > 1000000;
        }

        private static string ReadString(JSONNode node, string key)
        {
            if (!node.IsString) throw new FormatException(key + " must be a string.");
            return node.Value;
        }

        private static double ReadNumber(JSONNode node, string key)
        {
            if (!node.IsNumber || double.IsNaN(node.AsDouble) || double.IsInfinity(node.AsDouble))
                throw new FormatException(key + " must be a finite number.");
            return node.AsDouble;
        }

        private static int ReadInt(JSONNode node, string key)
        {
            var number = ReadNumber(node, key);
            if (number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number))
                throw new FormatException(key + " must be a whole Int32 number.");

            // Reject fractional tokens even when converting to double rounds them to an integer.
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
                throw new FormatException(key + " must be a whole Int32 number.");
            var integer = (parts[0][0] == '-' ? "-" : "") + significant + new string('0', (int) (exponent + adjustment));
            int result;
            if (!int.TryParse(integer, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
                throw new FormatException(key + " must be a whole Int32 number.");
            return result;
        }

        private static double[] ReadWeights(JSONNode node, string key)
        {
            if (!node.IsArray) throw new FormatException(key + " must be an array.");
            return node.Children.Select(entry => ReadNumber(entry, key)).ToArray();
        }

        private static int[] ReadIntegerWeights(JSONNode node, string key)
        {
            if (!node.IsArray) throw new FormatException(key + " must be an array.");
            return node.Children.Select(entry => ReadInt(entry, key)).ToArray();
        }

        private static void ValidateIntegerWeights(int[] weights, int count, string key)
        {
            if (weights == null || weights.Length != count || weights.Any(x => x < 0))
                throw new FormatException(key + " must contain exactly " + count + " integers from 0 to Int32.MaxValue.");
        }
    }
}
