using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    // Invoke after the runner's existing native Setup/PrepareSupport boundary.
    private static void CheckNguStartupObservation()
    {
        var allocator = Plugin.GetType("NGUInjector.Managers.BlackBarAllocator", true);
        var errors = Plugin.GetType("NGUInjector.AutomationErrorLog", true);
        var character = Get(MainType, "Character"); var settings = Get(MainType, "Settings");
        var previousNgu = Get(Character, "NGU");
        var previousFocus = Get(MainInstance, "_customSustainedFocusGoal");
        var previousEnergyWeights = Get(settings, "_energyNguWeights");
        var previousMagicWeights = Get(settings, "_magicNguWeights");
        var previousAppliedEnergy = Get(allocator, "AppliedEnergyNguWeights");
        var previousAppliedMagic = Get(allocator, "AppliedMagicNguWeights");
        var previousError = InlineObservedError;
        var caps = (IDictionary<string, long>)Get(allocator, "NguBufferedCaps"); var previousCaps = caps.ToArray();
        Invoke(errors, "Clear");
        try
        {
            Set(MainInstance, "_customSustainedFocusGoal", null);
            Set(settings, "_energyNguWeights", new[] { 0, 0, 0, 0, 2, 0, 1, 0, 0 });
            Set(settings, "_magicNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0 });
            Set(allocator, "AppliedEnergyNguWeights", null); Set(allocator, "AppliedMagicNguWeights", null);
            var ngu = Child(Character, "NGU");
            var energy = ListField(ngu, "skills", 9); var magic = ListField(ngu, "magicSkills", 7);
            for (var i = 0; i < energy.Count; i++) Set(energy[i], "energy", i == 0 ? 9007199254740993L : 10L + i);
            for (var i = 0; i < magic.Count; i++) Set(magic[i], "magic", 100L + i);
            caps.Clear(); caps["E0"] = long.MaxValue; caps["E1"] = 11; caps["M0"] = 100; caps["M1"] = 100;

            foreach (var settingsReady in new[] { false, true })
            foreach (var native in new[] { "absent-character", "absent-ngu", "ready" })
            {
                if (settingsReady && native == "ready") continue;
                Set(MainType, "Settings", settingsReady ? settings : null);
                Set(MainType, "Character", native == "absent-character" ? null : Character);
                Set(Character, "NGU", native == "ready" ? ngu : null);
                InlineObservedError = null;
                foreach (var runtime in new[] { Invoke(allocator, "GetNguRuntime"), CallMain("GetCustomNguRuntime") })
                {
                    var scenario = "startup settings=" + settingsReady + ", native=" + native;
                    Equal(false, Property(Json(runtime, "available"), "AsBool"), scenario + " is unavailable");
                    Equal("weighted-ngu-buffered-cap", Property(Json(runtime, "strategy"), "Value"), scenario + " retains strategy metadata");
                    Equal(5, Property(runtime, "Count"), scenario + " contains metadata only, not fabricated weights or allocation rows");
                }
                Equal(null, InlineObservedError, "unavailable startup observation does not invoke the error logger");
                Equal(0, Property(Invoke(errors, "Snapshot"), "Count"), "unavailable startup observation adds no automation error");
            }
            Set(MainType, "Settings", settings); Set(MainType, "Character", Character); Set(Character, "NGU", ngu);
            CheckInitializedNguObservation(allocator, ngu);
            Equal(0, Property(Invoke(errors, "Snapshot"), "Count"), "initialized observation adds no automation error");
            CheckMalformedNguObservation(allocator, settings, ngu, errors);
        }
        finally
        {
            Set(MainType, "Character", character); Set(MainType, "Settings", settings); Set(Character, "NGU", previousNgu);
            Set(MainInstance, "_customSustainedFocusGoal", previousFocus);
            Set(settings, "_energyNguWeights", previousEnergyWeights); Set(settings, "_magicNguWeights", previousMagicWeights);
            Set(allocator, "AppliedEnergyNguWeights", previousAppliedEnergy); Set(allocator, "AppliedMagicNguWeights", previousAppliedMagic);
            caps.Clear(); foreach (var pair in previousCaps) caps.Add(pair.Key, pair.Value);
            Invoke(errors, "Clear"); InlineObservedError = previousError;
        }
    }

    private static void CheckInitializedNguObservation(Type allocator, object ngu)
    {
        foreach (var runtime in new[] { Invoke(allocator, "GetNguRuntime"), CallMain("GetCustomNguRuntime") })
        {
            Equal(true, Property(Json(runtime, "available"), "AsBool"), "complete native model and settings are available");
            Equal("last-allocation-pass", Property(Json(runtime, "capBasis"), "Value"), "telemetry retains the cap observation basis");
            foreach (var energy in new[] { true, false })
            {
                var lane = energy ? "energy" : "magic";
                var rows = Json(runtime, lane + "Targets");
                var skills = (IList)Get(ngu, energy ? "skills" : "magicSkills");
                var configured = (int[])Get(Get(MainType, "Settings"), energy ? "_energyNguWeights" : "_magicNguWeights");
                Equal(energy ? 9 : 7, Property(rows, "Count"), lane + " exposes every native row");
                Equal(configured.Length, Property(Json(runtime, lane + "Weights"), "Count"), lane + " exposes configured array");
                Equal(false, Property(Json(runtime, lane + "WeightsApplied"), "AsBool"), lane + " is not applied before an allocation pass");
                Equal(true, Property(Json(runtime, lane + "AppliedWeights"), "IsNull"), lane + " has no fabricated applied snapshot");
                for (var i = 0; i < configured.Length; i++)
                {
                    var row = Json(rows, i);
                    Equal(i, Property(Json(row, "index"), "AsInt"), lane + " retains row index " + i);
                    Equal(configured[i], Property(Json(row, "weight"), "AsInt"), lane + " retains configured row weight " + i);
                    Equal(configured[i], Property(Json(Json(runtime, lane + "Weights"), i), "AsInt"), lane + " retains configured array weight " + i);
                    var expectedAmount = energy ? i == 0 ? 9007199254740993L : 10L + i : 100L + i;
                    Equal(expectedAmount.ToString(CultureInfo.InvariantCulture), Property(Json(row, "allocated"), "Value"), lane + " reports exact native long allocation " + i);
                    Equal(expectedAmount, Get(skills[i], lane), lane + " observation never changes allocation " + i);
                }
                Equal(true, Property(Json(Json(rows, 2), "bufferedCap"), "IsNull"), lane + " unvisited cap stays unknown");
                Equal(true, Property(Json(Json(rows, 2), "belowBufferedCap"), "IsNull"), lane + " unknown cap has no fabricated shortage flag");
            }
            var energyRows = Json(runtime, "energyTargets"); var magicRows = Json(runtime, "magicTargets");
            Equal("Augments", Property(Json(Json(energyRows, 0), "name"), "Value"), "energy NGU names are preserved");
            Equal("Yggdrasil", Property(Json(Json(magicRows, 0), "name"), "Value"), "magic NGU names are preserved");
            Equal("Adventure Beta", Property(Json(Json(magicRows, 6), "name"), "Value"), "last magic NGU name is preserved");
            Equal("9223372036854775807", Property(Json(Json(energyRows, 0), "bufferedCap"), "Value"), "known cap retains exact long precision");
            Equal(true, Property(Json(Json(energyRows, 0), "belowBufferedCap"), "AsBool"), "positive shortage is observed");
            Equal(false, Property(Json(Json(energyRows, 1), "belowBufferedCap"), "AsBool"), "allocation exactly at cap is not below it");
            Equal(false, Property(Json(Json(magicRows, 1), "belowBufferedCap"), "AsBool"), "allocation above cap is not below it");
        }

        var settings = Get(MainType, "Settings");
        Set(allocator, "AppliedEnergyNguWeights", ((int[])Get(settings, "_energyNguWeights")).ToArray());
        Set(allocator, "AppliedMagicNguWeights", new[] { 0, 0, 1, 0, 0, 0, 0 });
        foreach (var runtime in new[] { Invoke(allocator, "GetNguRuntime"), CallMain("GetCustomNguRuntime") })
        {
            Equal(true, Property(Json(runtime, "energyWeightsApplied"), "AsBool"), "equal applied energy arrays compare by value");
            Equal(false, Property(Json(runtime, "magicWeightsApplied"), "AsBool"), "different applied magic weights remain pending");
            Equal(2, Property(Json(Json(runtime, "energyAppliedWeights"), 4), "AsInt"), "actual applied energy weights are exported");
            Equal(1, Property(Json(Json(runtime, "magicAppliedWeights"), 2), "AsInt"), "historical applied magic weights are exported");
            Equal(1, Property(Json(Json(runtime, "magicWeights"), 0), "AsInt"), "pending telemetry retains current configured magic weights");
        }
    }

    private static void CheckMalformedNguObservation(Type allocator, object settings, object ngu, Type errors)
    {
        foreach (var energy in new[] { true, false })
        {
            var weightField = energy ? "_energyNguWeights" : "_magicNguWeights";
            var skillField = energy ? "skills" : "magicSkills";
            var weights = Get(settings, weightField); var skills = Get(ngu, skillField);
            foreach (var defect in new[] { "null-weights", "null-skills", "short-skills", "null-skill" })
            {
                try
                {
                    if (defect == "null-weights") Set(settings, weightField, null);
                    if (defect == "null-skills") Set(ngu, skillField, null);
                    if (defect == "short-skills") ListField(ngu, skillField, 1);
                    if (defect == "null-skill") ListField(ngu, skillField, energy ? 9 : 7)[0] = null;
                    var expectedError = defect == "short-skills" ? typeof(ArgumentOutOfRangeException) : typeof(NullReferenceException);
                    var scenario = (energy ? "energy " : "magic ") + defect;
                    Throws(() => Invoke(allocator, "GetNguRuntime"), expectedError.Name, scenario + " is not treated as startup unavailability");
                    InlineObservedError = null;
                    var runtime = CallMain("GetCustomNguRuntime");
                    Equal(false, Property(Json(runtime, "available"), "AsBool"), scenario + " wrapper reports unavailable after failure");
                    Equal("error", Property(Json(runtime, "status"), "Value"), scenario + " wrapper retains explicit error status");
                    Equal(expectedError, InlineObservedError?.GetType(), scenario + " retains the actual initialized-state error");
                    Equal("NGU allocation observation", Property(Json(Json(Invoke(errors, "Snapshot"), 0), "context"), "Value"), scenario + " remains visible in automation errors");
                }
                finally { Set(settings, weightField, weights); Set(ngu, skillField, skills); }
            }
        }
    }
}
