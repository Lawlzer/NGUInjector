using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SimpleJSON;

namespace NGUInjector.Managers
{
    // Pure metadata: no game objects or plan types. Returned arrays never expose registry storage.
    internal sealed class ActivityPolicy
    {
        internal const string ResourceName = "NGUInjector.ActivityPolicies.json";
        private static readonly Lazy<ActivityPolicy> Embedded = new Lazy<ActivityPolicy>(LoadEmbedded);
        internal static ActivityPolicy Current { get { return Embedded.Value; } }

        internal sealed class Focus
        {
            internal readonly int GearGoal, EnergyFocus, MagicFocus;
            internal Focus(int gear, int energy, int magic) { GearGoal = gear; EnergyFocus = energy; MagicFocus = magic; }
        }

        internal sealed class StageDefinition
        {
            internal readonly int GearGoal, EnergyFocus, MagicFocus;
            internal readonly string[] EnergyPriorities, MagicPriorities;
            internal StageDefinition(int gear, int energy, int magic, string[] energyPriorities, string[] magicPriorities)
            {
                GearGoal = gear; EnergyFocus = energy; MagicFocus = magic;
                EnergyPriorities = energyPriorities.ToArray(); MagicPriorities = magicPriorities.ToArray();
            }
        }

        internal sealed class Digger
        {
            internal readonly int Id, Weight;
            internal readonly string Name;
            internal Digger(int id, int weight, string name) { Id = id; Weight = weight; Name = name; }
        }

        internal sealed class Support
        {
            internal readonly int[] Beards;
            internal readonly Digger[] Diggers;
            internal Support(int[] beards, Digger[] diggers) { Beards = beards; Diggers = diggers; }
        }

        private readonly Dictionary<string, int[]> features = new Dictionary<string, int[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> prioritySets = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly Dictionary<int, string[][]> defaults = new Dictionary<int, string[][]>();
        private readonly Dictionary<string, int> focusIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, Focus> focuses = new Dictionary<string, Focus>(StringComparer.Ordinal);
        private readonly Dictionary<Tuple<int, int, int>, StageDefinition> stagePresets = new Dictionary<Tuple<int, int, int>, StageDefinition>();
        private readonly Dictionary<string, int[]> beards = new Dictionary<string, int[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, Digger[]> diggers = new Dictionary<string, Digger[]>(StringComparer.Ordinal);
        private int[] coreBeards, optionalBeards;
        private string[][] preparation;
        internal double DiggerBudgetFraction { get; private set; }

        private ActivityPolicy() { }

        private static ActivityPolicy LoadEmbedded()
        {
            using (var stream = typeof(ActivityPolicy).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("Missing embedded activity policy resource '" + ResourceName + "'. Embed Data/ActivityPolicies.json with this logical name; no fallback is allowed.");
                using (var reader = new StreamReader(stream)) return Parse(reader.ReadToEnd());
            }
        }

        internal static ActivityPolicy Parse(string json)
        {
            try
            {
                var root = JsonFiles.ParseObject(json);
                Integer(root["version"], 1, 1);
                var policy = new ActivityPolicy();
                foreach (var row in Array(root["features"]))
                {
                    var token = Text(row["token"]);
                    // These are executable handler limits, not another copy of policy priority orders.
                    int[] limits;
                    switch (token)
                    {
                        case "WAN": case "TM": limits = new[] {0, 0}; break;
                        case "NGU": limits = new[] {9, 7}; break;
                        case "AUG": limits = new[] {14, -1}; break;
                        case "AT": limits = new[] {5, -1}; break;
                        case "RIT": limits = new[] {-1, 8}; break;
                        default: throw new FormatException("Unsupported feature handler: " + token);
                    }
                    var counts = new[] {Integer(row["energyTargets"], -1, limits[0]), Integer(row["magicTargets"], -1, limits[1])};
                    for (var lane = 0; lane < 2; lane++)
                        if (limits[lane] <= 0 && counts[lane] != limits[lane] || limits[lane] > 0 && counts[lane] < 1)
                            throw new FormatException("Unsupported target limits for " + token);
                    policy.features.Add(token, counts);
                }
                if (policy.features.Count != 6) throw new FormatException("All six supported feature handlers are required.");
                if (!root["prioritySets"].IsObject) throw new FormatException("prioritySets must be an object.");
                var sequences = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in root["prioritySets"])
                {
                    var key = Text(new JSONString(row.Key));
                    var priorities = Array(row.Value).Select(value => {
                        var token = Text(value);
                        if (token != token.Trim().ToUpperInvariant() || !(policy.IsValidPriority(token, true) || policy.IsValidPriority(token, false)))
                            throw new FormatException("Invalid priority in set '" + key + "': " + token);
                        return token;
                    }).ToArray();
                    if (!sequences.Add(string.Join(",", priorities))) throw new FormatException("Duplicate priority-set sequence: " + key);
                    policy.prioritySets.Add(key, priorities);
                }
                foreach (var row in Array(root["defaults"]))
                {
                    var id = Integer(row["id"], 0, 7);
                    policy.focusIds.Add(Text(row["name"]), id);
                    policy.defaults.Add(id, new[] {policy.Priorities(row["energy"], true), policy.Priorities(row["magic"], false)});
                }
                var names = new[] {"None", "TimeMachine", "Augments", "Wandoos", "BloodMagic", "EnergyNgu", "MagicNgu", "AdvancedTraining"};
                for (var id = 0; id < names.Length; id++)
                    if (!policy.focusIds.ContainsKey(names[id]) || policy.focusIds[names[id]] != id)
                        throw new FormatException("Missing or mismatched stage focus: " + names[id]);
                policy.preparation = new[] {policy.Priorities(root["preparation"]["energy"], true), policy.Priorities(root["preparation"]["magic"], false)};
                if (!root["focuses"].IsObject) throw new FormatException("focuses must be an object.");
                foreach (var row in root["focuses"])
                    policy.focuses.Add(row.Key, new Focus(Integer(row.Value["gearGoal"], 0, 13),
                        Integer(row.Value["energyFocus"], 0, 7), Integer(row.Value["magicFocus"], 0, 7)));
                foreach (var name in new[] {"TimeMachine", "AugmentsBloodMagic", "Wandoos", "EnergyNgu", "Ngu", "MagicNgu", "AdvancedTraining", "Adventure", "Fight", "ManualTitans"})
                    if (!policy.focuses.ContainsKey(name)) throw new FormatException("Missing quick focus: " + name);
                foreach (var row in Array(root["stagePresets"]))
                {
                    var label = Text(row["label"]);
                    var gear = Integer(row["GearGoal"], 0, 13);
                    var energy = Integer(row["EnergyFocus"], 0, 7);
                    var magic = Integer(row["MagicFocus"], 0, 7);
                    var tuple = Tuple.Create(gear, energy, magic);
                    if (policy.stagePresets.ContainsKey(tuple))
                        throw new FormatException("Duplicate stage-preset tuple (GearGoal, EnergyFocus, MagicFocus) " + tuple + ": " + label);
                    policy.stagePresets.Add(tuple, new StageDefinition(gear, energy, magic,
                        policy.Priorities(row["EnergyPriorities"], true), policy.Priorities(row["MagicPriorities"], false)));
                }
                policy.coreBeards = Ids(root["coreBeards"], 6);
                policy.optionalBeards = Ids(root["optionalBeards"], 6);
                if (policy.coreBeards.Concat(policy.optionalBeards).Distinct().Count() != 7
                    || policy.coreBeards.Length + policy.optionalBeards.Length != 7)
                    throw new FormatException("Core and optional beards must partition IDs 0 through 6.");
                foreach (var name in new[] {"baseline", "bossFight", "manualTitans", "wandoos"})
                    policy.beards.Add(name, Ids(root["support"]["beards"][name], 6));
                foreach (var name in new[] {"baseline", "energyNgu", "magicNgu", "blood", "wandoos", "bossFight", "manualTitans", "manualAdventure", "imminentTitan"})
                    policy.diggers.Add(name, Array(root["support"]["diggers"][name]).Select(row =>
                        new Digger(Integer(row["id"], 0, 11), Integer(row["weight"], 1, int.MaxValue), Text(row["name"]))).ToArray());
                var fraction = root["support"]["diggerBudgetFraction"];
                if (!fraction.IsNumber || double.IsNaN(fraction.AsDouble) || double.IsInfinity(fraction.AsDouble) || fraction.AsDouble < 0 || fraction.AsDouble > 1)
                    throw new FormatException("support.diggerBudgetFraction must be a finite number between 0 and 1.");
                policy.DiggerBudgetFraction = fraction.AsDouble;
                return policy;
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("Invalid activity policy '" + ResourceName + "': " + error.Message, error);
            }
        }

        internal bool IsValidPriority(string token, bool energy)
        {
            token = (token ?? string.Empty).Trim().ToUpperInvariant();
            var parts = token.Split('-');
            int[] targets;
            if (!features.TryGetValue(parts[0], out targets) || targets[energy ? 0 : 1] < 0) return false;
            if (parts.Length == 1) return true;
            int index;
            return parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out index)
                && parts[1] == index.ToString(CultureInfo.InvariantCulture) && index < targets[energy ? 0 : 1];
        }

        internal string[] DefaultPriorities(int focus, bool energy)
        {
            string[][] priorities;
            return defaults.TryGetValue(focus, out priorities) ? priorities[energy ? 0 : 1].ToArray() : new string[0];
        }

        internal string[] DefaultPriorities(string focus, bool energy)
        {
            int id;
            return focus != null && focusIds.TryGetValue(focus, out id) ? DefaultPriorities(id, energy) : new string[0];
        }

        internal string PrimaryPriority(string[] configured, int focus, bool energy)
        {
            var priorities = configured == null || configured.Length == 0 ? DefaultPriorities(focus, energy) : configured;
            return NormalizePrimary(priorities.FirstOrDefault(token => IsValidPriority(token, energy)), energy);
        }

        private string NormalizePrimary(string token, bool energy)
        {
            return IsValidPriority(token, energy) ? token.Trim().ToUpperInvariant().Split('-')[0] : null;
        }

        internal Focus FocusDefinition(string goal)
        {
            Focus definition;
            if (goal == null || !focuses.TryGetValue(goal, out definition)) throw new ArgumentOutOfRangeException(nameof(goal), goal, "Unknown quick focus.");
            return definition;
        }

        internal StageDefinition FocusStageDefinition(string goal)
        {
            var focus = FocusDefinition(goal);
            StageDefinition preset;
            // Match intent, not display labels. Unmatched/manual variants keep their declared focus defaults.
            return stagePresets.TryGetValue(Tuple.Create(focus.GearGoal, focus.EnergyFocus, focus.MagicFocus), out preset)
                ? new StageDefinition(focus.GearGoal, focus.EnergyFocus, focus.MagicFocus, preset.EnergyPriorities, preset.MagicPriorities)
                : new StageDefinition(focus.GearGoal, focus.EnergyFocus, focus.MagicFocus,
                    DefaultPriorities(focus.EnergyFocus, true), DefaultPriorities(focus.MagicFocus, false));
        }

        internal int[] CoreBeards() { return coreBeards.ToArray(); }
        internal int[] DefaultBeardPriority() { return coreBeards.Concat(optionalBeards).ToArray(); }
        internal string[] PreparationPriorities(bool energy) { return preparation[energy ? 0 : 1].ToArray(); }
        internal Digger[] ImminentTitanDiggers() { return diggers["imminentTitan"].ToArray(); }

        internal Support BuildSupport(string primaryEnergy, string primaryMagic, bool bossFight, bool manualTitans, bool manualAdventure)
        {
            var energy = NormalizePrimary(primaryEnergy, true);
            var magic = NormalizePrimary(primaryMagic, false);
            var wandoos = energy == "WAN" || magic == "WAN";
            var selectedBeards = new List<int>();
            if (bossFight) selectedBeards.AddRange(beards["bossFight"]);
            if (manualTitans) selectedBeards.AddRange(beards["manualTitans"]);
            if (wandoos) selectedBeards.AddRange(beards["wandoos"]);
            selectedBeards.AddRange(beards["baseline"]);
            var selectedDiggers = new List<Digger>(diggers["baseline"]);
            if (energy == "NGU") selectedDiggers.AddRange(diggers["energyNgu"]);
            if (magic == "NGU") selectedDiggers.AddRange(diggers["magicNgu"]);
            if (magic == "RIT") selectedDiggers.AddRange(diggers["blood"]);
            if (wandoos) selectedDiggers.AddRange(diggers["wandoos"]);
            if (bossFight) selectedDiggers.AddRange(diggers["bossFight"]);
            if (manualTitans || manualAdventure)
                selectedDiggers = new List<Digger>(diggers[manualTitans ? "manualTitans" : "manualAdventure"]);
            return new Support(selectedBeards.Distinct().ToArray(), selectedDiggers.ToArray());
        }

        private string[] Priorities(JSONNode node, bool energy)
        {
            if (node == null || !node.IsString) throw new FormatException("Expected a priority-set key string; inline arrays are not supported.");
            string[] priorities;
            if (!prioritySets.TryGetValue(node.Value, out priorities)) throw new FormatException("Unknown priority-set key: " + node.Value);
            foreach (var token in priorities)
                if (!IsValidPriority(token, energy)) throw new FormatException("Invalid " + (energy ? "energy" : "magic") + " priority in set '" + node.Value + "': " + token);
            return priorities;
        }

        private static IEnumerable<JSONNode> Array(JSONNode node)
        {
            if (node == null || !node.IsArray) throw new FormatException("Expected an activity policy array.");
            return node.Children;
        }

        private static int[] Ids(JSONNode node, int max)
        {
            var ids = Array(node).Select(row => Integer(row, 0, max)).ToArray();
            if (ids.Distinct().Count() != ids.Length) throw new FormatException("Duplicate support ID.");
            return ids;
        }

        private static string Text(JSONNode node)
        {
            if (node == null || !node.IsString || string.IsNullOrWhiteSpace(node.Value)) throw new FormatException("Expected nonempty policy text.");
            return node.Value;
        }

        private static int Integer(JSONNode node, int min, int max)
        {
            if (node == null || !node.IsNumber || double.IsNaN(node.AsDouble) || double.IsInfinity(node.AsDouble)
                || node.AsDouble != Math.Truncate(node.AsDouble) || node.AsDouble < min || node.AsDouble > max)
                throw new FormatException("Expected a policy integer between " + min + " and " + max + ".");
            return node.AsInt;
        }
    }
}
