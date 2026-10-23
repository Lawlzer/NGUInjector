using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NGUInjector;
using NGUInjector.Managers;
using SimpleJSON;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message)
    { Check(Equals(expected, actual), message + ": expected " + expected + ", got " + actual); }
    private static void Reject(string json, string diagnostic)
    {
        try { ActivityPolicy.Parse(json); }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains(ActivityPolicy.ResourceName) && error.Message.Contains(diagnostic), "Clear policy diagnostic: " + error.Message);
            return;
        }
        throw new InvalidOperationException("Expected malformed policy rejection: " + diagnostic);
    }
    private static string SourcePath([CallerFilePath] string path = null) { return path; }
    private static void Main(string[] args)
    {
        if (args.Contains("--missing-resource"))
        {
            try { var policy = ActivityPolicy.Current; }
            catch (InvalidOperationException error)
            {
                Check(error.Message.Contains("Missing embedded activity policy resource 'NGUInjector.ActivityPolicies.json'")
                    && error.Message.Contains("no fallback"), "Missing resource fails closed with integration instructions");
                Console.WriteLine("PASS missing-resource check: " + assertions + " assertion.");
                return;
            }
            throw new InvalidOperationException("Expected no embedded policy; run with -p:OmitPolicyResource=true.");
        }
        var source = File.ReadAllText(Path.Combine(Path.GetDirectoryName(SourcePath()), "../../NGUInjector/Data/ActivityPolicies.json"));
        using (var resource = typeof(ActivityPolicy).Assembly.GetManifestResourceStream(ActivityPolicy.ResourceName))
        using (var reader = new StreamReader(resource))
            Equal(source, reader.ReadToEnd(), "Embedded policy is the exact actual JSON source, not a fixture copy");
        Defaults();
        Registry(source);
        Grammar();
        Focuses();
        FocusStages(source);
        Support();
        Copies();
        Manifest(source);
        Console.WriteLine("PASS pure activity policy: " + assertions + " assertions; actual embedded JSON and production SimpleJSON/strict parser, no game or plan types.");
    }

    private static void Registry(string source)
    {
        var root = JsonFiles.ParseObject(source);
        var sets = root["prioritySets"];
        Equal(sets.Count, sets.Children.Select(row => row.ToString()).Distinct().Count(), "Each ordered sequence is defined under exactly one priority-set key");
        var references = root["defaults"].Children.SelectMany(row => new[] {row["energy"], row["magic"]})
            .Concat(root["stagePresets"].Children.SelectMany(row => new[] {row["EnergyPriorities"], row["MagicPriorities"]}))
            .Concat(new[] {root["preparation"]["energy"], root["preparation"]["magic"]}).ToArray();
        foreach (var reference in references)
            Check(reference.IsString && sets[reference.Value].IsArray, "Every default, preset and preparation reference resolves to a named array");
        Equal(sets.Count, references.Select(row => row.Value).Distinct().Count(), "Every unique priority set has a consumer");
        var expectedPresets = new[] {
            new[] {"TM,NGU,WAN,AUG,AT", "TM,RIT,NGU,WAN"},
            new[] {"AUG,NGU,WAN,TM,AT", "RIT,NGU,WAN,TM"},
            new[] {"WAN,NGU,TM,AUG,AT", "WAN,RIT,NGU,TM"},
            new[] {"NGU,WAN,TM,AUG,AT", "NGU,RIT,WAN,TM"},
            new[] {"", "NGU,RIT,WAN,TM"},
            new[] {"NGU,WAN,TM,AUG,AT", "NGU,RIT,WAN,TM"},
            new[] {"AT,NGU,WAN,TM,AUG", "RIT,NGU,WAN,TM"},
            new[] {"AT-3,AT-4,WAN,NGU,TM,AUG,AT", "RIT,NGU,TM"}
        };
        Equal(expectedPresets.Length, root["stagePresets"].Count, "Preset count unchanged");
        for (var id = 0; id < expectedPresets.Length; id++)
        foreach (var energy in new[] {true, false})
        {
            var reference = root["stagePresets"][id][energy ? "EnergyPriorities" : "MagicPriorities"].Value;
            Equal(expectedPresets[id][energy ? 0 : 1], string.Join(",", sets[reference].Children.Select(row => row.Value)), "Preset resolves to exact previous order");
        }
        Equal("AT-3,AT-4,NGU,TM", string.Join(",", ActivityPolicy.Current.PreparationPriorities(true)), "Implicit Wandoos AT energy preparation preserved");
        Equal("RIT,NGU,TM", string.Join(",", ActivityPolicy.Current.PreparationPriorities(false)), "Implicit Wandoos AT magic preparation preserved");
        Equal(root["defaults"][7]["magic"].Value, root["preparation"]["magic"].Value, "Preparation magic shares the existing matching default set");
        Equal(root["stagePresets"][7]["MagicPriorities"].Value, root["preparation"]["magic"].Value, "Preparation magic shares the existing matching preset set");
    }

    private static void Defaults()
    {
        var expected = new[] {
            new[] {"None", "", ""},
            new[] {"TimeMachine", "TM,NGU,WAN,AT", "TM,RIT,NGU,WAN"},
            new[] {"Augments", "AUG,NGU,WAN,TM", "RIT,NGU,WAN,TM"},
            new[] {"Wandoos", "WAN,NGU,TM,AT", "WAN,RIT,NGU,TM"},
            new[] {"BloodMagic", "", "RIT,NGU,WAN,TM"},
            new[] {"EnergyNgu", "NGU,WAN,TM,AT", ""},
            new[] {"MagicNgu", "", "NGU,RIT,WAN,TM"},
            new[] {"AdvancedTraining", "AT,NGU,TM", "RIT,NGU,TM"}
        };
        for (var id = 0; id < expected.Length; id++)
        foreach (var energy in new[] {true, false})
        {
            var policy = ActivityPolicy.Current;
            Equal(expected[id][energy ? 1 : 2], string.Join(",", policy.DefaultPriorities(id, energy)), "Old switch exact order by ID");
            Equal(expected[id][energy ? 1 : 2], string.Join(",", policy.DefaultPriorities(expected[id][0], energy)), "Old switch exact order by name");
        }
        Equal(0, ActivityPolicy.Current.DefaultPriorities(999, true).Length, "Unknown numeric focus remains empty");
        Equal(0, ActivityPolicy.Current.DefaultPriorities("unknown", false).Length, "Unknown named focus remains empty");
        Equal("TM", ActivityPolicy.Current.PrimaryPriority(null, 1, true), "Null overrides use defaults");
        Equal("TM", ActivityPolicy.Current.PrimaryPriority(new string[0], 1, true), "Empty overrides use defaults");
        Equal("NGU", ActivityPolicy.Current.PrimaryPriority(new[] {"RIT", "invalid", " ngu-8 ", "WAN"}, 1, true), "First valid override normalized to primary kind");
        Equal(null, ActivityPolicy.Current.PrimaryPriority(new[] {"RIT", "NGU-9"}, 1, true), "Invalid nonempty overrides do not invent default intent");
    }

    private static void Grammar()
    {
        var policy = ActivityPolicy.Current;
        foreach (var energy in new[] {true, false})
        foreach (var kind in new[] {"WAN", "TM", "NGU", "AUG", "AT", "RIT"})
        {
            var count = kind == "NGU" ? energy ? 9 : 7 : kind == "AUG" ? energy ? 14 : -1
                : kind == "AT" ? energy ? 5 : -1 : kind == "RIT" ? energy ? -1 : 8 : 0;
            Equal(count >= 0, policy.IsValidPriority(kind, energy), "Recognized group lane: " + kind);
            for (var id = -2; id <= 15; id++)
            {
                var token = kind + "-" + id;
                Equal(id >= 0 && id < count, policy.IsValidPriority(token, energy), "Exact handler index boundary: " + token);
                Equal(id >= 0 && id < count, policy.IsValidPriority(" " + token.ToLowerInvariant() + "\t", energy), "Normalized handler index boundary: " + token);
            }
            foreach (var suffix in new[] {"", "00", "01", "+1", " 1", "1.0", "1e0", "-1", "2147483648", "0-1", "x"})
                Equal(false, policy.IsValidPriority(kind + "-" + suffix, energy), "Noncanonical index rejected: " + kind + "-" + suffix);
        }
        foreach (var token in new[] {null, "", " ", "UNKNOWN", "HACK", "WAN-0", "TM-0", "NGU_1", "NGU -1", "NGU-\u0661"})
        foreach (var energy in new[] {true, false})
            Equal(false, policy.IsValidPriority(token, energy), "Invalid token: " + token);
    }

    private static void Focuses()
    {
        var names = new[] {"TimeMachine", "AugmentsBloodMagic", "Wandoos", "EnergyNgu", "Ngu", "MagicNgu", "AdvancedTraining", "Adventure", "Fight", "ManualTitans"};
        var expected = new[] {"5,1,1", "10,2,4", "12,3,3", "2,5,6", "4,5,6", "3,0,6", "11,7,4", "9,7,4", "9,0,0", "9,0,0"};
        for (var id = 0; id < names.Length; id++)
        {
            var focus = ActivityPolicy.Current.FocusDefinition(names[id]);
            Equal(expected[id], focus.GearGoal + "," + focus.EnergyFocus + "," + focus.MagicFocus, "Exact quick gear/resource mapping: " + names[id]);
        }
        try { ActivityPolicy.Current.FocusDefinition("unknown"); }
        catch (ArgumentOutOfRangeException) { assertions++; return; }
        throw new InvalidOperationException("Unknown quick focus must fail clearly.");
    }

    private static string Requests(ActivityPolicy.Support support)
    { return string.Join(",", support.Diggers.Select(row => row.Id + ":" + row.Weight + ":" + row.Name)); }

    private static void FocusStages(string source)
    {
        var root = JsonFiles.ParseObject(source);
        var policy = ActivityPolicy.Current;
        var matched = 0;
        foreach (var row in root["focuses"])
        {
            var tuple = policy.FocusDefinition(row.Key);
            var stage = policy.FocusStageDefinition(row.Key);
            Equal(tuple.GearGoal, stage.GearGoal, row.Key + " stage keeps declared gear");
            Equal(tuple.EnergyFocus, stage.EnergyFocus, row.Key + " stage keeps declared energy");
            Equal(tuple.MagicFocus, stage.MagicFocus, row.Key + " stage keeps declared magic");
            var preset = root["stagePresets"].Children.SingleOrDefault(p => p["GearGoal"].AsInt == tuple.GearGoal
                && p["EnergyFocus"].AsInt == tuple.EnergyFocus && p["MagicFocus"].AsInt == tuple.MagicFocus);
            if (preset != null) matched++;
            foreach (var energy in new[] {true, false})
            {
                var expected = preset == null ? policy.DefaultPriorities(energy ? tuple.EnergyFocus : tuple.MagicFocus, energy)
                    : root["prioritySets"][preset[energy ? "EnergyPriorities" : "MagicPriorities"].Value].Children.Select(p => p.Value).ToArray();
                var priorities = energy ? stage.EnergyPriorities : stage.MagicPriorities;
                Equal(string.Join(",", expected), string.Join(",", priorities), row.Key + " full explicit lane order from preset or declared defaults");
                Check(priorities.Length == 0 || !ReferenceEquals(priorities, energy ? policy.FocusStageDefinition(row.Key).EnergyPriorities
                    : policy.FocusStageDefinition(row.Key).MagicPriorities), row.Key + " mutable lane arrays are caller-owned");
                if (priorities.Length > 0) priorities[0] = "MUTATED";
                Equal(string.Join(",", expected), string.Join(",", energy ? policy.FocusStageDefinition(row.Key).EnergyPriorities
                    : policy.FocusStageDefinition(row.Key).MagicPriorities), row.Key + " caller mutation cannot alter registry");
            }
        }
        Equal(7, matched, "Every matching Quick uses a standard preset; three special variants fall back");
        var changed = JsonFiles.ParseObject(source);
        foreach (var preset in changed["stagePresets"].Children) preset["label"] = "Renamed display label";
        changed["focuses"]["TimeMachine"]["gearGoal"] = 11;
        changed["focuses"]["TimeMachine"]["energyFocus"] = 7;
        changed["focuses"]["TimeMachine"]["magicFocus"] = 3;
        changed["stagePresets"][7]["EnergyPriorities"] = "NguPresetEnergy";
        var parsed = ActivityPolicy.Parse(changed.ToString());
        Equal("NGU,WAN,TM,AUG,AT", string.Join(",", parsed.FocusStageDefinition("TimeMachine").EnergyPriorities), "Tuple matching follows edited intents and priority references, not Quick name or preset label");
        Equal("RIT,NGU,TM", string.Join(",", parsed.FocusStageDefinition("TimeMachine").MagicPriorities), "Tuple matching retains the matching preset's explicit magic, not Wandoos defaults");
        changed = JsonFiles.ParseObject(source);
        changed["stagePresets"].AsArray.Remove(0);
        parsed = ActivityPolicy.Parse(changed.ToString());
        Equal("TM,NGU,WAN,AT", string.Join(",", parsed.FocusStageDefinition("TimeMachine").EnergyPriorities), "Removed matching preset safely uses declared focus defaults");
        changed = JsonFiles.ParseObject(source);
        changed["focuses"]["MagicNgu"]["gearGoal"] = 4;
        parsed = ActivityPolicy.Parse(changed.ToString());
        var magic = parsed.FocusStageDefinition("MagicNgu");
        Equal(4, magic.GearGoal, "Unmatched custom Magic tuple retains actual metadata gear goal");
        Equal(0, magic.EnergyFocus, "Unmatched custom Magic keeps energy unowned");
        Equal(0, magic.EnergyPriorities.Length, "Unmatched custom Magic has explicit empty energy");
        Equal("NGU,RIT,WAN,TM", string.Join(",", magic.MagicPriorities), "Unmatched custom Magic uses declared magic default");
        foreach (var alteredOrder in new[] {false, true})
        {
            changed = JsonFiles.ParseObject(source);
            var duplicate = JSON.Parse(changed["stagePresets"][7].ToString());
            duplicate["label"] = "Ambiguous preset";
            if (alteredOrder) duplicate["EnergyPriorities"] = "NguPresetEnergy";
            changed["stagePresets"].AsArray.Add(duplicate);
            Reject(changed.ToString(), "Duplicate stage-preset tuple (GearGoal, EnergyFocus, MagicFocus) (11, 7, 3): Ambiguous preset");
        }
    }
    private static void Support()
    {
        var policy = ActivityPolicy.Current;
        var baseline = "0:20:drop,3:20:adventure,6:15:energy beard,7:15:magic beard,8:15:PP,9:15:daycare";
        Equal(baseline, Requests(policy.BuildSupport(null, null, false, false, false)), "Exact baseline digger requests");
        Equal("1,3,5", string.Join(",", policy.BuildSupport(null, null, false, false, false).Beards), "Baseline request beards intentionally exclude manager-added Gold");
        Equal(baseline + ",4:50:energy NGU,5:50:magic NGU", Requests(policy.BuildSupport(" ngu-8 ", "ngu-6", false, false, false)), "Resource NGU support normalized");
        Equal(baseline + ",10:50:blood", Requests(policy.BuildSupport("AUG", "RIT-7", false, false, false)), "Blood support by resource intent");
        var wandoos = policy.BuildSupport("WAN", "WAN", false, false, false);
        Equal(baseline + ",1:100:wandoos", Requests(wandoos), "Two Wandoos lanes request one digger");
        Equal("4,1,3,5", string.Join(",", wandoos.Beards), "Wandoos optional request ordering");
        Equal(baseline + ",2:100:stat", Requests(policy.BuildSupport(null, null, true, false, false)), "Explicit boss request");
        Equal("0,1,3,5", string.Join(",", policy.BuildSupport(null, null, true, false, false).Beards), "Boss optional request ordering");
        var titans = policy.BuildSupport("WAN", "RIT", true, true, true);
        Equal("3:10000:adventure,0:1000:drop", Requests(titans), "Manual titan overrides all digger requests and wins manual adventure tie");
        Equal("0,6,4,1,3,5", string.Join(",", titans.Beards), "Titan beard requests preserve optional ordering and distinctness");
        Equal("3:800:adventure,0:400:drop", Requests(policy.BuildSupport("NGU", "RIT", false, false, true)), "Explicit manual adventure replaces resource digger requests");
        Equal("11:1000:EXP titan", string.Join(",", policy.ImminentTitanDiggers().Select(row => row.Id + ":" + row.Weight + ":" + row.Name)), "Exact imminent-titan request");
        Equal(.75d, policy.DiggerBudgetFraction, "Existing gross-GPS fraction retained in policy");
    }

    private static void Copies()
    {
        var policy = ActivityPolicy.Current;
        var defaults = policy.DefaultPriorities(1, true); defaults[0] = "WAN";
        Equal("TM", policy.DefaultPriorities(1, true)[0], "Defaults defensive copy");
        var core = policy.CoreBeards(); core[0] = 0;
        Equal("1,3,5,6", string.Join(",", policy.CoreBeards()), "Core copy retains current Gold policy");
        var all = policy.DefaultBeardPriority(); all[0] = 0;
        Equal("1,3,5,6,0,2,4", string.Join(",", policy.DefaultBeardPriority()), "Global core plus optional composition copied");
        var support = policy.BuildSupport(null, null, false, false, false);
        support.Beards[0] = 0; support.Diggers[0] = null;
        Equal(1, policy.BuildSupport(null, null, false, false, false).Beards[0], "Support beards isolated");
        Equal(0, policy.BuildSupport(null, null, false, false, false).Diggers[0].Id, "Support digger arrays isolated; entries immutable");
        var preparation = policy.PreparationPriorities(true); preparation[0] = "WAN";
        Equal("AT-3", policy.PreparationPriorities(true)[0], "Preparation energy defensive copy");
        var magicPreparation = policy.PreparationPriorities(false); magicPreparation[0] = "TM";
        Equal("RIT", policy.DefaultPriorities(7, false)[0], "Shared preparation/default storage not exposed");
        var imminent = policy.ImminentTitanDiggers(); imminent[0] = null;
        Equal(11, policy.ImminentTitanDiggers()[0].Id, "Imminent titan request array isolated");
    }

    private static void Manifest(string source)
    {
        Func<JSONObject> fresh = () => JsonFiles.ParseObject(source);
        var changed = fresh();
        changed["prioritySets"]["TimeMachineEnergy"] = JSON.Parse("[\"NGU-8\",\"AUG\",\"TM\"]");
        changed["support"]["diggers"]["energyNgu"][0]["weight"] = 321;
        var parsed = ActivityPolicy.Parse(changed.ToString());
        Equal("NGU-8,AUG,TM", string.Join(",", parsed.DefaultPriorities(1, true)), "Existing executable token can be added/reordered in manifest without code");
        Equal(321, parsed.BuildSupport(parsed.PrimaryPriority(null, 1, true), null, false, false, false).Diggers.Last().Weight, "Manifest weight change drives support");
        changed["features"][2]["energyTargets"] = 8;
        Reject(changed.ToString(), "Invalid priority in set 'TimeMachineEnergy': NGU-8");
        changed = fresh(); changed["features"][2]["energyTargets"] = 8;
        Equal(false, ActivityPolicy.Parse(changed.ToString()).IsValidPriority("NGU-8", true), "Supported manifest may narrow executable limits");
        changed = fresh(); changed["features"][0]["token"] = "SYNTHETIC";
        Reject(changed.ToString(), "Unsupported feature handler: SYNTHETIC");
        changed = fresh(); changed["features"][2]["energyTargets"] = 10;
        Reject(changed.ToString(), "integer between -1 and 9");
        changed = fresh(); changed["features"][3]["magicTargets"] = 1;
        Reject(changed.ToString(), "integer between -1 and -1");
        changed = fresh(); changed["defaults"].AsArray.Remove(0);
        Reject(changed.ToString(), "stage focus: None");
        changed = fresh(); changed["focuses"].Remove("Wandoos");
        Reject(changed.ToString(), "Missing quick focus: Wandoos");
        changed = fresh(); changed["support"]["diggers"]["baseline"][0]["weight"] = 0;
        Reject(changed.ToString(), "integer between 1 and");
        changed = fresh(); changed["coreBeards"] = JSON.Parse("[1,3,5,5]");
        Reject(changed.ToString(), "Duplicate support ID");
        changed = fresh(); changed["optionalBeards"] = JSON.Parse("[0,2]");
        Reject(changed.ToString(), "partition IDs");
        changed = fresh(); changed["stagePresets"][0]["EnergyPriorities"] = "UNKNOWN";
        Reject(changed.ToString(), "Unknown priority-set key: UNKNOWN");
        changed = fresh(); changed["prioritySets"]["TimeMachineEnergy"] = JSON.Parse("[\"tm\"]");
        Reject(changed.ToString(), "Invalid priority in set 'TimeMachineEnergy': tm");
        changed = fresh(); changed["prioritySets"]["TimeMachineEnergy"] = JSON.Parse("[\"UNKNOWN\"]");
        Reject(changed.ToString(), "Invalid priority in set 'TimeMachineEnergy': UNKNOWN");
        changed = fresh(); changed["prioritySets"]["Duplicate"] = JSON.Parse(changed["prioritySets"]["BloodMagic"].ToString());
        Reject(changed.ToString(), "Duplicate priority-set sequence: Duplicate");
        changed = fresh(); changed["prioritySets"]["TimeMachineEnergy"] = "TM";
        Reject(changed.ToString(), "activity policy array");
        changed = fresh(); changed.Remove("prioritySets");
        Reject(changed.ToString(), "prioritySets must be an object");
        foreach (var section in new[] {"defaults", "stagePresets", "preparation"})
        {
            foreach (var replacement in new[] {"null", "42", "[]", "[\"TM\"]", "\"unknown\""})
            {
                changed = fresh();
                var reference = section == "defaults" ? changed[section][1] : section == "stagePresets" ? changed[section][0] : changed[section];
                reference[section == "stagePresets" ? "EnergyPriorities" : "energy"] = JsonFiles.ParseObject("{\"value\":" + replacement + "}")["value"];
                Reject(changed.ToString(), replacement == "\"unknown\"" ? "Unknown priority-set key" : "Expected a priority-set key string");
            }
        }
        changed = fresh(); changed["defaults"][1]["magic"] = "AugmentsEnergy";
        Reject(changed.ToString(), "Invalid magic priority in set 'AugmentsEnergy': AUG");
        changed = fresh(); changed["prioritySets"]["BloodMagic"] = JSON.Parse("[\"TM\",\"NGU\",\"RIT\",\"WAN\"]");
        parsed = ActivityPolicy.Parse(changed.ToString());
        foreach (var row in changed["defaults"].Children.Where(row => row["magic"].Value == "BloodMagic"))
            Equal("TM,NGU,RIT,WAN", string.Join(",", parsed.DefaultPriorities(row["id"].AsInt, false)), "Shared set edit reaches every referencing default");
        changed = fresh(); changed["prioritySets"]["WandoosAtMagic"] = JSON.Parse("[\"NGU\",\"TM\",\"RIT\"]");
        parsed = ActivityPolicy.Parse(changed.ToString());
        Equal("NGU,TM,RIT", string.Join(",", parsed.PreparationPriorities(false)), "Shared set edit reaches preparation");
        Equal("NGU,TM,RIT", string.Join(",", parsed.DefaultPriorities(7, false)), "Same edit reaches implicit AT default");
        var saved = new[] {"WAN", "RIT-7"};
        Equal("WAN", parsed.PrimaryPriority(saved, 7, false), "Explicit saved arrays override registry sets");
        Equal("WAN,RIT-7", string.Join(",", saved), "Explicit saved arrays are never rewritten");
        changed = fresh(); changed["support"]["diggerBudgetFraction"] = .5;
        Equal(.5d, ActivityPolicy.Parse(changed.ToString()).DiggerBudgetFraction, "Budget fraction follows policy metadata");
        foreach (var fraction in new[] {"null", "\"0.75\"", "-0.1", "1.01"})
        {
            changed = fresh(); changed["support"]["diggerBudgetFraction"] = JsonFiles.ParseObject("{\"value\":" + fraction + "}")["value"];
            Reject(changed.ToString(), "support.diggerBudgetFraction must be a finite number between 0 and 1");
        }
        changed = fresh(); changed["support"]["diggers"]["imminentTitan"][0]["weight"] = 1234;
        Equal(1234, ActivityPolicy.Parse(changed.ToString()).ImminentTitanDiggers()[0].Weight, "Imminent titan request follows policy metadata");
        changed = fresh(); changed["support"]["diggers"].Remove("imminentTitan");
        Reject(changed.ToString(), "activity policy array");
        Reject(source.Substring(0, source.LastIndexOf('}')), "Unterminated JSON container");
        Reject(source + " trailing", "one complete JSON object");
        Reject("{\"version\":1,\"version\":1}", "Duplicate JSON property");
        Reject("{}", "policy integer");
        Reject("[]", "one complete JSON object");
    }
}
