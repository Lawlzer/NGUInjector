#if PRESET_MODEL_CHECKS
using System;
using System.Linq;
using NGUInjector;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

internal static partial class Program
{
    private static void AdventureDynamicChecks()
    {
        var before = assertions;
        AdventureDynamicSerializationChecks();
        AdventureDynamicValidationChecks();
        AdventureDynamicResolutionChecks();
        AdventureDynamicBoundChecks();
        AdventureDynamicRouteChecks();
        AdventureDynamicDiggerChecks();
        Console.WriteLine("Adventure-dynamic checks: " + (assertions - before));
    }

    private static void AdventureDynamicSerializationChecks()
    {
        var legacy = Model();
        Check(legacy.AdventureDynamicWeight == 0 && new CustomLoadoutPreset().AdventureDynamicWeight == 0, "missing dynamic weight defaults to zero");
        var original = legacy.ToJson().ToString();
        Check(!legacy.ToJson().Linq.Any(row => row.Key == "AdventureDynamicWeight"), "legacy serialization omits dynamic field");
        var zero = Model(",\"AdventureDynamicWeight\":0");
        Check(zero.ToJson().ToString() == original, "explicit zero and absent dynamic field have identical serialized identity");
        Check(zero.SourceJson.Linq.Any(row => row.Key == "AdventureDynamicWeight"), "zero omission never removes the caller's source field");
        var dynamic = Model(",\"AdventureDynamicWeight\":2.5,\"future\":{\"large\":9007199254740993,\"exponent\":1.2300e+004}");
        var source = dynamic.SourceJson.ToString();
        var json = dynamic.ToJson();
        Check(json["AdventureDynamicWeight"].IsNumber && json["AdventureDynamicWeight"].AsDouble == 2.5, "positive dynamic weight emits a number");
        var roundTrip = CustomLoadoutPreset.Parse(json);
        Check(roundTrip.AdventureDynamicWeight == 2.5 && roundTrip.Weights.SequenceEqual(legacy.Weights), "positive dynamic weight round trips without changing fixed goals");
        Check(roundTrip.ToJson()["future"].ToString() == dynamic.SourceJson["future"].ToString(), "dynamic round trip preserves unknown numeric lexemes");
        dynamic.AdventureDynamicWeight = 0;
        Check(!dynamic.ToJson().Linq.Any(row => row.Key == "AdventureDynamicWeight"), "clearing a positive source field removes it from serialized output");
        Check(dynamic.SourceJson.ToString() == source, "serializing a cleared dynamic weight never mutates source JSON");
        foreach (var count in new[] {6, 10, 12, 14})
        {
            var fields = ",\"Weights\":[" + string.Join(",", Enumerable.Repeat("0", count)) + "]";
            InvalidModel(fields);
            InvalidModel(fields + ",\"AdventureDynamicWeight\":0");
            dynamic = Model(fields + ",\"AdventureDynamicWeight\":0.25");
            roundTrip = CustomLoadoutPreset.Parse(dynamic.ToJson());
            Check(roundTrip.Weights.SequenceEqual(new double[14]) && roundTrip.AdventureDynamicWeight == .25, "positive dynamic permits zero fixed goals with legacy padding " + count);
        }
    }

    private static void AdventureDynamicValidationChecks()
    {
        foreach (var value in new[] {"-0.1", "1000000.1", "1e309", "-1e309", "null", "true", "\"1\"", "\"NaN\"", "[]", "{}"})
            InvalidModel(",\"AdventureDynamicWeight\":" + value);
        foreach (var value in new[] {0d, .000001, .5, 1000000d})
            Check(Model(",\"AdventureDynamicWeight\":" + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).AdventureDynamicWeight == value,
                "valid dynamic weight boundary " + value);
        foreach (var value in new[] {-1d, 1000001d, double.NaN, double.NegativeInfinity, double.PositiveInfinity})
        {
            var p = Model(); p.AdventureDynamicWeight = value;
            var rejected = false;
            try { p.ToJson(); }
            catch (FormatException error) { rejected = error.Message.Contains("AdventureDynamicWeight"); }
            Check(rejected, "native invalid dynamic weight is rejected with field name " + value);
        }
        foreach (var weights in new[] {"[]", "null", "[0]", "[0,0,0,0,0,0,0]", "[-1,0,0,0,0,0]", "[1000001,0,0,0,0,0]", "[true,0,0,0,0,0]"})
            InvalidModel(",\"AdventureDynamicWeight\":1,\"Weights\":" + weights);
        foreach (var value in new[] {double.NaN, double.PositiveInfinity, -1d, 1000001d})
        {
            var p = Model(); p.AdventureDynamicWeight = 1; p.Weights[0] = value;
            var rejected = false;
            try { CustomLoadoutPreset.Validate(p); } catch (FormatException) { rejected = true; }
            Check(rejected, "positive dynamic never masks invalid fixed weight " + value);
        }
    }

    private static void AdventureDynamicResolutionChecks()
    {
        var p = Model(",\"AdventureDynamicWeight\":2,\"Weights\":[0,0,0,0,0,0,0,0,0,0,0,0],\"Zone\":2,\"AutoUpdate\":false,"
            + "\"AdvancedTrainingWeights\":[0,0,0,2,3],\"BeardPriority\":[1,3],\"BeardRotation\":[0,4],\"future\":{\"large\":9007199254740993}");
        var original = p.ToJson().ToString(); var source = p.SourceJson.ToString();
        foreach (var mode in new[] {0, 1, 2, -1, 99})
        {
            var resolved = p.ResolveAdventure(mode, 7);
            var lane = mode == 1 ? 5 : mode == 2 ? 4 : 3;
            var expected = new double[14]; expected[lane] = 2;
            Check(resolved.Weights.SequenceEqual(expected), "dynamic resolution uses only the resolved farming lane for mode " + mode);
            Check(resolved.Zone == (mode == 1 ? 7 : -1), "only idle-zone task pins runtime zone " + mode);
            Check(resolved.AdventureDynamicWeight == 0 && !resolved.ToJson().Linq.Any(row => row.Key == "AdventureDynamicWeight"), "resolved preset cannot retain dynamic work " + mode);
            Check(resolved.ResolveAdventure(mode, 7).ToJson().ToString() == resolved.ToJson().ToString(), "same-task resolution is idempotent " + mode);
            Check(!ReferenceEquals(resolved.Weights, p.Weights) && !ReferenceEquals(resolved.AdvancedTrainingWeights, p.AdvancedTrainingWeights)
                && !ReferenceEquals(resolved.BeardPriority, p.BeardPriority) && !ReferenceEquals(resolved.BeardRotation, p.BeardRotation)
                && !ReferenceEquals(resolved.SourceJson, p.SourceJson), "resolver deep-clones model arrays and source " + mode);
            Check(!resolved.AutoUpdate && resolved.AdvancedTrainingWeights.SequenceEqual(p.AdvancedTrainingWeights)
                && resolved.BeardPriority.SequenceEqual(p.BeardPriority) && resolved.BeardRotation.SequenceEqual(p.BeardRotation)
                && resolved.ToJson()["future"].ToString() == p.SourceJson["future"].ToString(), "resolver retains unrelated policy and unknown data " + mode);
            Check(p.ToJson().ToString() == original && p.SourceJson.ToString() == source, "resolving never mutates the persisted definition " + mode);
            resolved.Weights[0] = 99; resolved.AdvancedTrainingWeights[0] = 99; resolved.BeardPriority[0] = 2;
            resolved.BeardRotation[0] = 6; resolved.SourceJson["future"]["large"] = 42;
            Check(p.ToJson().ToString() == original && p.SourceJson.ToString() == source, "mutating resolved clone cannot affect original " + mode);
        }
        var fallback = p.ResolveAdventure(0, 43);
        Check(fallback.Weights[3] == 2 && fallback.Weights[5] == 0 && fallback.Zone == -1, "normalized locked-zone fallback uses PP, not configured zone farming");
        foreach (var count in new[] {6, 10, 12})
        {
            var native = new CustomLoadoutPreset {Id = "native", Name = "Native legacy", AdventureDynamicWeight = 1, Weights = new double[count]};
            var weights = native.Weights;
            var resolved = native.ResolveAdventure(0, -1);
            Check(ReferenceEquals(native.Weights, weights) && native.Weights.Length == count && native.Weights.All(weight => weight == 0), "pure resolution never pads the original native legacy array " + count);
            Check(resolved.Weights.Length == 14 && resolved.Weights[3] == 1 && resolved.Weights[10] == 0, "native legacy clone pads before dynamic resolution " + count);
        }
        var blended = Model(",\"AdventureDynamicWeight\":10,\"Weights\":[1,2,3,4,5,6,7,8,9,10,11,12]");
        foreach (var mode in new[] {0, 1, 2})
        {
            var resolved = blended.ResolveAdventure(mode, 7);
            for (var lane = 0; lane < 12; lane++)
                Near(resolved.Weights[lane], lane + 1 + (lane == (mode == 1 ? 5 : mode == 2 ? 4 : 3) ? 10 : 0), "additive fixed-goal blend " + mode + "/" + lane);
            Near(resolved.Weights.Sum(), blended.Weights.Sum() + 10, "blend conserves total objective weight " + mode);
        }
        var consumed = p.ResolveAdventure(0, -1).ResolveAdventure(2, 7);
        Check(consumed.Weights[3] == 2 && consumed.Weights[4] == 0 && consumed.Weights[10] == 0, "resolved clone cannot add dynamic contribution under a second task");
        foreach (var mode in new[] {3, 4})
        foreach (var weight in new[] {0d, 1d})
        {
            var unsupported = Model(); unsupported.AdventureDynamicWeight = weight;
            Rejected(() => unsupported.ResolveAdventure(mode, 7), mode == 3 ? "push" : "manual", "unsupported task identifies its mode " + mode);
            Rejected(() => unsupported.ResolveAdventure(mode, 7), "deferred", "unsupported task explicitly reports deferred resolution " + mode);
        }
    }

    private static void AdventureDynamicBoundChecks()
    {
        var p = Model(",\"AdventureDynamicWeight\":1000000,\"Weights\":[0,0,0,1000000,0,0,0,0,0,0,0,0]");
        var resolved = p.ResolveAdventure(0, -1);
        var roundTrip = CustomLoadoutPreset.Parse(resolved.ToJson());
        Check(roundTrip.Weights[3] == 1000000 && roundTrip.AdventureDynamicWeight == 0, "maximum fixed PP plus maximum dynamic weight remains serializable");
        Check(roundTrip.Weights[10] == 0, "maximum PP blend never introduces immediate power");
        var normalized = LoadoutPresetPlanner.NormalizeWeights(roundTrip.Weights);
        Near(normalized[3], 1, "maximum PP blend retains 100 percent PP");
        Near(normalized[10], 0, "maximum PP blend retains zero immediate power");
        Check(roundTrip.Weights.Where((weight, lane) => lane != 3).All(weight => weight == 0), "rescaling keeps excluded lanes zero");

        foreach (var mode in new[] {0, 1, 2})
        foreach (var fixedFarming in new[] {0d, 1000000d})
        {
            var lane = mode == 1 ? 5 : mode == 2 ? 4 : 3;
            p = Model(); p.AdventureDynamicWeight = 1000000;
            p.Weights = Enumerable.Range(1, 12).Select(i => i * 1000d).ToArray();
            p.Weights[0] = p.Weights[10] = 1000000; p.Weights[lane] = fixedFarming;
            var original = p.ToJson().ToString();
            var raw = p.Weights.ToArray(); raw[lane] += 1000000;
            var expected = LoadoutPresetPlanner.NormalizeWeights(raw);
            resolved = p.ResolveAdventure(mode, 7);
            Check(resolved.Weights.Max() == 1000000 && resolved.Weights.All(weight => weight >= 0 && weight <= 1000000), "all execution weights fit schema after overflow " + mode + "/" + fixedFarming);
            normalized = LoadoutPresetPlanner.NormalizeWeights(resolved.Weights);
            for (var i = 0; i < 12; i++)
                Near(normalized[i], expected[i], "uniform scaling preserves every normalized goal " + mode + "/" + fixedFarming + "/" + i);
            roundTrip = CustomLoadoutPreset.Parse(resolved.ToJson());
            Check(roundTrip.Weights.SequenceEqual(resolved.Weights), "scaled farming or power maximum round trips " + mode + "/" + fixedFarming);
            Check(resolved.ResolveAdventure(mode, 7).ToJson().ToString() == resolved.ToJson().ToString(), "scaled execution clone is idempotent " + mode + "/" + fixedFarming);
            Check(p.ToJson().ToString() == original, "rescaling never changes source definition " + mode + "/" + fixedFarming);
        }
    }

    private static void AdventureDynamicRouteChecks()
    {
        var fixedOnly = Model(",\"Zone\":2,\"Weights\":[0,0,0,1,2,3,0,0,0,0,0,0]");
        var original = fixedOnly.ToJson().ToString();
        foreach (var mode in new[] {0, 1, 2, -1, 99})
        {
            var resolved = fixedOnly.ResolveAdventure(mode, 7);
            Check(resolved.Zone == 2 && resolved.Weights.SequenceEqual(fixedOnly.Weights), "zero dynamic leaves fixed farming goals and pinned Zone untouched " + mode);
            Check(!ReferenceEquals(resolved, fixedOnly) && !ReferenceEquals(resolved.Weights, fixedOnly.Weights), "zero dynamic still returns an independent clone " + mode);
        }
        fixedOnly.AdventureDynamicWeight = 1;
        foreach (var mode in new[] {0, 1, 2})
        {
            var resolved = fixedOnly.ResolveAdventure(mode, 7);
            Check(resolved.Zone == (mode == 1 ? 7 : -1), "positive dynamic overrides the shared pinned route even with fixed farming mixtures " + mode);
            var lane = mode == 1 ? 5 : mode == 2 ? 4 : 3;
            foreach (var farming in new[] {3, 4, 5})
                Near(resolved.Weights[farming], fixedOnly.Weights[farming] + (farming == lane ? 1 : 0), "route override retains fixed farming mix " + mode + "/" + farming);
            Check(resolved.ResolveAdventure(1, 9).Zone == resolved.Zone, "consumed dynamic goal cannot override the route a second time " + mode);
        }
        fixedOnly.AdventureDynamicWeight = 0;
        Check(fixedOnly.ToJson().ToString() == original, "route resolution never changes the source definition");
    }

    private static void AdventureDynamicDiggerChecks()
    {
        var p = Model(",\"AdventureDynamicWeight\":1,\"Weights\":[0,0,0,0,0,0,0,0,0,0,0,0]");
        foreach (var mode in new[] {0, 1, 2})
        {
            var resolved = p.ResolveAdventure(mode, 0);
            var weights = LoadoutPresetPlanner.NormalizeWeights(resolved.Weights);
            var diggers = PresetDefinitions.DefaultDiggerWeights(weights, false);
            var expected = new double[12]; expected[3] = 1;
            if (mode == 0) expected[8] = 1; else expected[0] = 1;
            for (var id = 0; id < 12; id++) Near(diggers[id], expected[id], "shared coefficients select task diggers " + mode + "/" + id);
            var minimum = PresetDefinitions.DefaultDiggerWeights(weights, true);
            Near(minimum[3], 2, "shared minimum-adventure coefficient stays additive " + mode);

            var s = Snapshot(); s.Available = Enumerable.Repeat(true, 12).ToArray();
            s.PpPerKill = Enumerable.Repeat(200d, 100).ToArray();
            s.Zones = new[] {new PresetZone {Id = 0, IdlePower = 10, IdleToughness = 10, OneHitPower = 1000,
                NormalRatio = 1, BoostValues = new[] {10d}, Chances = new[] {.5}, Caps = new[] {1d}}};
            var request = Request(); request.Weights = resolved.Weights; request.Zone = resolved.Zone;
            var candidate = Evaluate(s, request);
            Check(candidate.Feasible && candidate.Score.HasValue && candidate.Metrics[10] > 0
                && candidate.Metrics[mode == 1 ? 5 : mode == 2 ? 4 : 3] > 0, "resolved weights are usable by the real planner " + mode);
        }
        p.Weights[0] = 2; p.Weights[1] = 3; p.Weights[2] = 4;
        var mixed = p.ResolveAdventure(0, -1);
        var mixedDiggers = PresetDefinitions.DefaultDiggerWeights(LoadoutPresetPlanner.NormalizeWeights(mixed.Weights), false);
        Near(mixedDiggers[4], .2, "blended energy digger keeps fixed goal");
        Near(mixedDiggers[5], .3, "blended magic digger keeps fixed goal");
        Near(mixedDiggers[10], .4, "blended blood digger keeps fixed goal");
        Near(mixedDiggers[6], .04, "energy beard digger uses shared 0.2 coefficient");
        Near(mixedDiggers[7], .06, "magic beard digger uses shared 0.2 coefficient");
        Near(mixedDiggers[3], .1, "blended adventure digger reflects farming share");
        Near(mixedDiggers[8], .1, "blended PP digger reflects full farming share");
        p.DiggerWeights = Enumerable.Range(1, 12).Select(i => (double)i).ToArray();
        mixed = p.ResolveAdventure(2, -1);
        Check(mixed.DiggerWeights.SequenceEqual(p.DiggerWeights) && !ReferenceEquals(mixed.DiggerWeights, p.DiggerWeights), "explicit digger preferences survive resolution as an independent array");
    }
}
#endif
