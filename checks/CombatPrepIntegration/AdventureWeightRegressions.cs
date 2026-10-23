using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class Program
{
    private static int AdventureWeightSearches, AdventureWeightLegacySearches;
    private static readonly List<double[]> AdventureWeightRequests = new List<double[]>();
    private static bool ObserveAdventureWeightRequests, ObserveAdventureWeightSwaps;
    private static int AdventureWeightUnsafeSwaps;
    private static bool AdventureWeightFailRouteAfterWrite, AdventureWeightFailRetirement, AdventureWeightTrackRoutes;
    private static int AdventureWeightRouteFaults, AdventureWeightRetirementFaults, AdventureWeightFarmingWrites;
    private static int AdventureWeightPartialZone;
    private static double AdventureWeightRoutedPower;

    private static void ConfigureAdventureWeightGameFixture(AssemblyDefinition game)
    {
        // Same recurring-input and native rendering boundaries as PresetIntegration. Keep
        // adventure stats, route validation, input validation and all production searches real.
        var factors = new[] { "totalLevelBonus", "lootBonus", "luckBonus", "totalDropChanceBonus",
            "totalAdventureBonus", "adventureBonus", "adventureBonus2", "PPBonus", "totalPPGainBonus" };
        foreach (var type in game.MainModule.Types)
        foreach (var method in type.Methods.Where(x => x.HasBody))
        {
            if (type.Name != "AllBeardsController" && type.Name != "AllGoldDiggerController"
                && factors.Contains(method.Name) && new[] { "System.Single", "System.Double" }.Contains(method.ReturnType.FullName))
                Constant(method, 1);
            if (type.Name == "AllAdvancedTraining" && new[] { "adventurePowerBonus", "adventureToughnessBonus" }.Contains(method.Name)) Constant(method, 0);
            if (type.Name == "Character" && method.Name == "idleAttackPower") Constant(method, 1.2);
            if (type.Name == "AdventureController" && method.Name == "respawnTime") Constant(method, 4);
            if (type.Name == "ZoneSelector" && method.Name == "changeZone")
            {
                Invoke(typeof(PresetIntegrationChecks), "KeepNativeItopodEntry", method);
                // Inject only at the unavailable native scene boundary, retaining the game's
                // actual zone/floor writes and all production route/cleanup instructions.
                var il = method.Body.GetILProcessor(); var first = method.Body.Instructions[0];
                il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
                il.InsertBefore(first, il.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(Program).GetMethod("AdventureWeightZoneBefore", All))));
                var ret = method.Body.Instructions.Last();
                il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
                il.InsertBefore(ret, il.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(Program).GetMethod("AdventureWeightZoneAfter", All))));
            }
        }
    }

    private static void ConfigureAdventureWeightObservers(AssemblyDefinition plugin)
    {
        var planner = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Managers.LoadoutPresetPlanner");
        var plan = planner.Methods.Single(x => x.Name == "Plan");
        var il = plan.Body.GetILProcessor(); var first = plan.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Call, plugin.MainModule.ImportReference(typeof(Program).GetMethod("ObserveAdventureWeightSearch", All))));
        foreach (var optimize in plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Managers.GearOptimizer").Methods.Where(x => x.Name == "Optimize" && x.HasBody))
        {
            var observer = optimize.Body.GetILProcessor();
            observer.InsertBefore(optimize.Body.Instructions[0], observer.Create(OpCodes.Call,
                plugin.MainModule.ImportReference(typeof(Program).GetMethod("ObserveAdventureWeightLegacySearch", All))));
        }
    }

    public static void ObserveAdventureWeightSearch(object request)
    {
        AdventureWeightSearches++;
        if (ObserveAdventureWeightRequests) AdventureWeightRequests.Add((double[])((double[])Get(request, "Weights")).Clone());
    }

    public static void ObserveAdventureWeightLegacySearch() { AdventureWeightLegacySearches++; }

    public static void ObserveAdventureWeightGear()
    {
        if (ObserveAdventureWeightSwaps && (int)Get(Get(Character, "adventure"), "zone") != -1) AdventureWeightUnsafeSwaps++;
    }

    public static void AdventureWeightZoneBefore(int zone)
    {
        if (zone != -1 || !AdventureWeightFailRetirement) return;
        AdventureWeightRetirementFaults++;
        throw new InvalidOperationException("fixture zone selector failure before safe-zone write");
    }

    public static void AdventureWeightZoneAfter(int zone)
    {
        typeof(Program).GetMethod("CombatRouteCommitted", All)?.Invoke(null, new object[] { zone });
        if (zone < 0 || !AdventureWeightTrackRoutes) return;
        AdventureWeightFarmingWrites++;
        AdventureWeightRoutedPower = Convert.ToDouble(Invoke(Character, "totalAdvAttack"));
        if (!AdventureWeightFailRouteAfterWrite) return;
        AdventureWeightFailRouteAfterWrite = false;
        AdventureWeightRouteFaults++;
        AdventureWeightPartialZone = (int)Get(Get(Character, "adventure"), "zone");
        throw new InvalidOperationException("fixture zone selector rendering failure after native route write");
    }

    private static object AdventureWeightSetup(double weight = 15)
    {
        AdventureWeightFailRouteAfterWrite = AdventureWeightFailRetirement = false;
        AdventureWeightRouteFaults = AdventureWeightRetirementFaults = AdventureWeightFarmingWrites = 0;
        AdventureWeightPartialZone = -1; AdventureWeightRoutedPower = 0;
        AdventureWeightTrackRoutes = true;
        ScheduledSetup(ScheduledRow.Substring(0, ScheduledRow.Length - 1) + ",\"AdventureDynamicWeight\":"
            + weight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
        var path = Path.Combine(Temp, "plans", "fixture.json"); var json = File.ReadAllText(path);
        Invoke(typeof(PresetIntegrationChecks), "SetupAdventureDynamic", "prep-weight-" + Guid.NewGuid().ToString("N"), "[1,0,0,0,0,0]", true);
        Set(Settings, "_savePath", null); // No native Unity JSON persistence in this offline fixture.
        PrepareLiveResourceFixture(); // Preset support construction replaces the native TM controller.
        File.WriteAllText(path, json); Setting("UseCustomRebirthPlan", true); Setting("CustomAdventureEnabled", true);
        Set(Get(Character, "adventure"), "zone", -1);
        Set(Get(Character, "advancedTraining"), "transferredBankedLevels", true);
        ControlledCombatPrepClock = 10000;
        return Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
    }

    private static void CheckAdventureWeightRegressions()
    {
        var before = Assertions;
        var failures = new List<string>();
        foreach (var test in new Action[] { CheckAdventureWeightCapture, CheckAdventureWeightTradeoff,
            CheckAdventureWeightZero, CheckAdventureWeightManualPreview, CheckAdventureWeightStartupRoute, CheckAdventureWeightDeferred,
            CheckAdventureWeightEdits, CheckAdventureWeightAtGate, CheckAdventureWeightRecovery,
            CheckAdventureWeightCompletedRecovery, CheckAdventureWeightThresholdRules,
            CheckAdventureWeightPartialRouteFailure, CheckAdventureWeightRetirementFailure,
            CheckAdventureWeightRetirementExhaustion,
            CheckAdventureWeightCleanup, CheckAdventureWeightForeignRoutes })
        {
            try { test(); Console.WriteLine(test.Method.Name + ": passed"); }
            catch (Exception error) { failures.Add(test.Method.Name); Console.Error.WriteLine(test.Method.Name + ": " + error.GetBaseException()); }
            finally
            {
                ObserveAdventureWeightRequests = ObserveAdventureWeightSwaps = false;
                AdventureWeightFailRouteAfterWrite = AdventureWeightFailRetirement = false;
                AdventureWeightTrackRoutes = false;
                ObserveAutomaticTransfers = false; CombatPrepClockRead = null;
            }
        }
        Console.WriteLine("Adventure weight regressions: " + (Assertions - before) + " assertions, " + failures.Count + " failed groups; total runner assertions=" + Assertions + ".");
        if (failures.Count > 0) throw new Exception("Adventure-weight integration regressions failed: " + string.Join(", ", failures));
        Setup();
    }

    private static void CheckAdventureWeightCapture()
    {
        AdventureWeightSetup();
        var at = Get(Character, "advancedTraining"); var targets = (long[])Get(at, "levelTarget");
        targets[0] = 71; targets[1] = 73; targets[2] = 79; targets[3] = 5; targets[4] = -1;
        var aug = ((Array)Get(Get(Character, "augments"), "augs")).GetValue(0);
        Set(aug, "augmentTarget", 5L); Set(aug, "upgradeTarget", -1L);
        var ngu = Get(Character, "NGU"); Set(ngu, "autoAdvance", true);
        var lane = ((IList)Get(ngu, "skills"))[0]; Set(lane, "target", 2L);
        Set(Get(Get(Character, "wandoos98"), "bootupTime"), "totalseconds", 0d);
        var resources = Resources(); var support = CallMain("CombatPrepSupportKey"); var gear = CallMain("CombatPrepEquipped");
        var recalls = Recalls; var swaps = Swaps;
        AdventureWeightRequests.Clear(); ObserveAdventureWeightRequests = true;
        var capture = CallMain("CaptureAutomaticCombatPrep", 15d, 300);
        ObserveAdventureWeightRequests = false;
        Equal(15d, Get(capture, "AdventureDynamicWeight"), "actual configured capture weight");
        Equal("ITOPOD", Get(capture, "AdventureTask"), "dynamic adventure resolves actual ITOPOD task");
        Equal(true, ((string)Get(capture, "AdventureTaskKey")).StartsWith("0:"), "resolved task identity");
        Equal(4, AdventureWeightRequests.Count, "one real optimizer search per available primary");
        foreach (var goal in new[] { 7, 9, 8, 0 })
        {
            var request = AdventureWeightRequests.Single(x => x[goal] == 100);
            Equal(15d, request[3], "actual ITOPOD secondary request is fifteen for primary " + goal);
            Equal(2, request.Count(x => x > 0), "no unrelated objectives leak into search " + goal);
        }
        var routes = (IDictionary)Get(capture, "AdventureRoutes"); var snapshots = (IDictionary)Get(capture, "AdventureSnapshots");
        var s = Get(capture, "Snapshot");
        Equal(true, Get(s, "RequirePhaseProfiles"), "unscored Current cannot win a weighted phase");
        Equal(4, routes.Count, "four weighted winners"); Equal(4, ((Array)Get(s, "Profiles")).Length, "only one joint support/gear winner per phase");
        object common = null;
        foreach (var key in new[] { "augments-planned", "at-planned", "wandoos-planned", "ngu-planned" })
        {
            var route = routes[key]; var snapshot = snapshots[key];
            Equal(true, Get(route, "Feasible"), "finite feasible real winner " + key);
            var score = (double)Get(route, "Score"); Equal(true, !double.IsNaN(score) && !double.IsInfinity(score), "finite weighted score " + key);
            if (common != null) Equal(true, ReferenceEquals(common, snapshot), "one coherent immutable numeric snapshot"); common = snapshot;
            Equal(false, Get(snapshot, "NguAutoAdvance"), "prep-only numeric NGU ownership adapter");
            Equal(0d, Get(((Array)Get(snapshot, "EnergyNgu")).GetValue(0), "Target"), "positive NGU target ignored only in selection snapshot");
            var bars = (Array)Get(snapshot, "AdvancedTraining");
            Equal(0d, Get(bars.GetValue(3), "Target"), "AT3 positive target cleared numerically");
            Equal(-1d, Get(bars.GetValue(4), "Target"), "AT4 disabled target preserved numerically");
            Equal(0, ((long[])Get(snapshot, "RequiredATTargets")).Length, "no mandatory generic AT targets invented");
            Equal("0,0,0,1,1", string.Join(",", (int[])Get(snapshot, "ATWeights")), "selection scores only native Wandoos AT indices 3 and 4");
            var evaluation = SupportEvaluation(capture, key);
            Equal(string.Join(",", (int[])Get(Get(route, "Support"), "Beards")), string.Join(",", (int[])Get(Get(evaluation, "Selection"), "Beards")), "exact winning beards pinned");
            Equal(string.Join(",", ((Array)Get(Get(route, "Support"), "Diggers")).Cast<object>().Select(x => Get(x, "Id") + ":" + Get(x, "Level"))),
                string.Join(",", ((Array)Get(Get(evaluation, "Selection"), "Diggers")).Cast<object>().Select(x => Get(x, "Id") + ":" + Get(x, "Level"))), "exact winning diggers pinned");
            var items = (Array)Get(route, "Items"); var slots = (int[])Get(snapshot, "Slots"); var map = (Array)((IDictionary)Get(capture, "Gear"))[key];
            for (var i = 0; i < items.Length; i++)
                Equal(Get(items.GetValue(i), "Id"), Get(map.Cast<object>().Single(x => (int)Get(x, "slot") == slots[i]), "id"), "exact winning item destination " + key + "/" + slots[i]);
        }
        Equal(0d, Get(s, "WandoosBootupElapsedSeconds"), "combat forecast retains actual fresh-zero bootup");
        Set(Get(Get(Character, "wandoos98"), "bootupTime"), "totalseconds", 1e9);
        foreach (var energy in new[] { true, false })
        {
            var coefficient = (double)Get(Get(common, energy ? "WandoosEnergy" : "WandoosMagic"), "Coefficient");
            var expected = Convert.ToDouble(Invoke(Character, energy ? "totalWandoosEnergySpeed" : "totalWandoosMagicSpeed"))
                / Convert.ToDouble(Invoke(Get(Character, "wandoos98Controller"), energy ? "baseEnergyTime" : "baseMagicTime"));
            Equal(true, expected > 0 && coefficient > 0, "full-boot coefficient remains positive at zero live bootup " + energy);
            Near(1, coefficient / expected, "selection uses native full-boot coefficient " + energy);
        }
        Set(Get(Get(Character, "wandoos98"), "bootupTime"), "totalseconds", 0d);
        Equal("71,73,79,5,-1", string.Join(",", targets), "native AT0-4 menu targets unchanged by capture");
        Equal(5L, Get(aug, "augmentTarget"), "native augment target unchanged"); Equal(-1L, Get(aug, "upgradeTarget"), "native upgrade disable unchanged");
        Equal(2L, Get(lane, "target"), "native NGU target unchanged"); Equal(true, Get(ngu, "autoAdvance"), "native auto-advance unchanged");
        Equal(resources, Resources(), "capture never allocates"); Equal(support, CallMain("CombatPrepSupportKey"), "capture never applies support");
        Equal(true, CallMain("CombatPrepGearMatches", gear), "capture never equips"); Equal(recalls, Recalls, "capture never recalls"); Equal(swaps, Swaps, "capture never swaps");
        var paid = Invoke(Plugin.GetType("NGUInjector.Managers.CombatPrepPlanner", true), "Plan", s, 300d);
        Equal(true, Get(paid, "Success"), "weighted paid snapshot plans through real scheduler");
        Equal(false, ((Array)Get(paid, "Alternatives")).Cast<object>().SelectMany(x => ((Array)Get(x, "Phases")).Cast<object>())
            .Any(x => (string)Get(x, "GearProfileKey") == "current"), "no weighted executable alternative falls back to unscored Current");
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
        var free = CallMain("CaptureAutomaticCombatPrep", 15d, 300);
        Equal(false, ((IDictionary)Get(free, "AdventureRoutes")).Contains("at-planned"), "Wish190 cannot buy paid AT");
        var result = Invoke(Plugin.GetType("NGUInjector.Managers.CombatPrepPlanner", true), "Plan", Get(free, "Snapshot"), 300d);
        Equal(true, Get(result, "Success"), "fresh-zero boot/Wish190/auto-advance snapshot is executable");
        Equal(false, ((Array)Get(Get(result, "Recommendation"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "Wish190 plan contains no paid AT");
    }

    private static void CheckAdventureWeightTradeoff()
    {
        AdventureWeightSetup();
        PrepareAdventureWeightTradeoffGear();
        var zero = CallMain("CaptureAutomaticCombatPrep", 0d, 300);
        var weighted = CallMain("CaptureAutomaticCombatPrep", 15d, 300);
        Func<object, string, object> headFor = (capture, key) => ((Array)((IDictionary)Get(capture, "Gear"))[key]).Cast<object>().Single(x => (int)Get(x, "slot") == -1);
        Equal(60, Get(headFor(zero, "ngu-planned"), "id"), "zero retains maximum-primary head");
        Equal(61, Get(headFor(weighted, "ngu-planned"), "id"), "100:15 chooses mixed tradeoff, not maximum-primary or pure-adventure head");
        var route = ((IDictionary)Get(weighted, "AdventureRoutes"))["ngu-planned"];
        var snapshot = ((IDictionary)Get(weighted, "AdventureSnapshots"))["ngu-planned"];
        var request = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.PresetRequest", true));
        var weights = new double[((Array)Get(Plugin.GetType("NGUInjector.Managers.LoadoutPresetPlanner", true), "Keys")).Length]; weights[3] = 15;
        Set(request, "Weights", weights); Set(request, "Horizon", 300); Set(request, "Swap", 0); Set(request, "SetupOnly", true); Set(request, "PlannedSupport", true);
        var pure = Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutPresetPlanner", true), "Plan", snapshot, request);
        var winner = ((Array)Get(pure, "Candidates")).GetValue(1);
        Equal(62, Get(((Array)Get(winner, "Items")).GetValue(Array.IndexOf((int[])Get(snapshot, "Slots"), -1)), "Id"), "positive control: pure-adventure search chooses third head");
        Equal(true, (double)Get(route, "GearPower") < (double)Get(winner, "GearPower"), "weighted winner preserves primary instead of maximizing adventure");
        Equal(true, (double)((Array)Get(route, "Metrics")).GetValue(0) > (double)((Array)Get(winner, "Metrics")).GetValue(0),
            "mixed winner retains strictly more actual NGU production than pure adventure");
        Equal(true, (double)((Array)Get(route, "Metrics")).GetValue(3) > (double)((Array)Get(((Array)Get(pure, "Candidates")).GetValue(0), "Metrics")).GetValue(3),
            "mixed winner gains actual ITOPOD throughput over primary-only equipped gear");
    }

    private static void PrepareAdventureWeightTradeoffGear()
    {
        var inventory = Get(Character, "inventory"); var original = Get(inventory, "head");
        // Equal-slot alternatives: tiny primary loss buys a meaningful ITOPOD floor gain;
        // pure adventure sacrifices most primary output for still more adventure power.
        for (var i = 0; i < 3; i++)
        {
            var head = i == 0 ? original : Blank(original.GetType());
            Set(head, "id", 60 + i); SetEnum(head, "type", "Head"); Set(head, "bossRequired", 1); Set(head, "removable", true);
            SetEnum(head, "spec1Type", "EnergyPower"); Set(head, "spec1Cur", new[] { 10000f, 9800f, 0f }[i]);
            Set(head, "curAttack", new[] { 0f, 10000f, 100000f }[i]); Set(head, "capAttack", new[] { 0f, 10000f, 100000f }[i]);
            if (i > 0) ((IList)Get(inventory, "inventory"))[i - 1] = head;
        }
        RefreshBonuses();
    }

    private static void CheckAdventureWeightManualPreview()
    {
        AdventureWeightSetup(); Setting("UseCustomRebirthPlan", false);
        var searches = AdventureWeightSearches; var preview = Preview(60);
        var cache = Get(MainInstance, "_combatPrepPreview"); var capture = Get(cache, "Capture");
        Equal(false, Get(capture, "WeightedProfiles"), "manual preview ignores configured automatic stage weight");
        Equal(8, ((Array)Get(Get(capture, "Snapshot"), "Profiles")).Length, "manual preview retains all legacy support variants");
        var alternatives = ((Array)Get(Get(cache, "Plan"), "Alternatives")).Cast<object>().ToArray();
        var index = Array.FindIndex(alternatives, x => ((Array)Get(x, "Phases")).Length > 0);
        Equal(true, index >= 0, "actual manual preview includes executable candidate");
        Start(JsonValue(preview, "previewId"), "candidate-" + index);
        var run = Get(MainInstance, "_combatPrepRun");
        Equal(false, Get(run, "Automatic"), "manual token dispatch remains manual");
        Equal(searches, AdventureWeightSearches, "manual preview/start never runs weighted search");
        CallMain("RouteCombatPrepAdventure"); Equal(-1, Get(Get(Character, "adventure"), "zone"), "manual run never routes adventure");
        CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
    }

    private static void CheckAdventureWeightZero()
    {
        AdventureWeightSetup(0);
        var searches = AdventureWeightSearches;
        var old = CallMain("CaptureCombatPrep", .03, .03, null); var zero = CallMain("CaptureAutomaticCombatPrep", 0d, 0);
        Equal(searches, AdventureWeightSearches, "zero bypasses weighted planner including irrelevant horizon");
        Equal(false, Get(zero, "WeightedProfiles"), "zero uses legacy profiles"); Equal(false, Get(Get(zero, "Snapshot"), "RequirePhaseProfiles"), "zero permits Current fallback");
        Equal(0, ((IDictionary)Get(zero, "AdventureRoutes")).Count, "zero owns no routes");
        foreach (string key in ((IDictionary)Get(old, "Gear")).Keys)
        {
            Equal(true, EqualMaps(((IDictionary)Get(old, "Gear"))[key], ((IDictionary)Get(zero, "Gear"))[key]), "zero exact legacy gear " + key);
            Equal(Get(Get(SupportEvaluation(old, key), "Selection"), "StableKey"), Get(Get(SupportEvaluation(zero, key), "Selection"), "StableKey"), "zero exact legacy support " + key);
        }
        var planner = Plugin.GetType("NGUInjector.Managers.CombatPrepPlanner", true);
        var a = Get(Invoke(planner, "Plan", Get(old, "Snapshot"), 300d), "Recommendation");
        var b = Get(Invoke(planner, "Plan", Get(zero, "Snapshot"), 300d), "Recommendation");
        Equal(Get(a, "Name"), Get(b, "Name"), "zero identical recommended legacy policy"); Equal(Get(a, "EndWandoosBonus"), Get(b, "EndWandoosBonus"), "zero identical numeric result");
        var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture"); var run = StartScheduled(plan);
        Equal(0d, Get(run, "AdventureConfiguredWeight"), "zero actual automatic startup");
        var zone = Get(Get(Character, "adventure"), "zone"); CallMain("RouteCombatPrepAdventure"); Equal(zone, Get(Get(Character, "adventure"), "zone"), "zero route is unchanged");
        CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
    }

    private static object AdventureWeightActiveRoute(object run)
    {
        var route = ((IDictionary)Get(Get(run, "Inputs"), "AdventureRoutes"))[Get(ScheduledPhase(run), "GearProfileKey")];
        Equal("active", Get(run, "AdventureStatus"), "actual tick activates weighted route: " + Get(run, "Reason")
            + "; warnings=" + string.Join("; ", ((IList)Get(run, "Warnings")).Cast<object>())
            + "; phaseVerified=" + Get(Get(run, "ResourceOutcome"), "Verified")
            + "; normalBaseline=" + (Get(MainInstance, "_executionBaselineResourceIntent") == null ? "null" : "present"));
        Equal(true, ReferenceEquals(route, Get(run, "AdventureRoute")), "executor uses exact selected candidate, not a new preset acquisition");
        Equal(true, CallMain("CombatPrepGearMatches", Get(run, "ExpectedGear")), "routing follows exact phase gear acquisition");
        Equal(true, CallMain("CombatPrepSupportStillOwned", run), "routing follows verified support acquisition");
        Equal(Get(route, "Zone"), Get(Get(Character, "adventure"), "zone"), "actual native adventure zone");
        if ((int)Get(route, "Zone") == 1000)
        {
            Equal(Get(route, "Floor"), Get(Get(Character, "adventure"), "itopodStart"), "actual native start floor");
            Equal(Get(route, "Floor"), Get(Get(Character, "adventure"), "itopodEnd"), "actual native end floor");
            Equal(Get(route, "Floor"), Get(Get(Character, "adventureController"), "itopodLevel"), "actual native entry floor");
        }
        Equal(null, Get(MainInstance, "_globalPresetReceipt"), "combat route never acquires global preset owner");
        return route;
    }

    private static void CheckAdventureWeightStartupRoute()
    {
        var plan = AdventureWeightSetup(); var run = StartScheduled(plan);
        Equal(15d, Get(run, "AdventureConfiguredWeight"), "actual startup reads stage Adventure15"); Equal(true, Get(Get(run, "Inputs"), "WeightedProfiles"), "actual startup uses weighted capture");
        AdventureWeightActiveRoute(run);
        var runtime = CallMain("GetCombatPrepRuntime");
        Equal(15d, Get(Json(runtime, "configuredAdventureWeight"), "AsDouble"), "runtime exposes configured weight");
        Equal(15d, Get(Json(runtime, "adventureDynamicWeight"), "AsDouble"), "runtime exposes positive effective weight");
        Equal("active", JsonValue(runtime, "adventureStatus"), "runtime exposes active route status");
        Equal(Get(Get(run, "Inputs"), "AdventureTaskKey"), JsonValue(runtime, "adventureTaskKey"), "runtime reports exact selected task");
        var searches = AdventureWeightSearches; var legacySearches = AdventureWeightLegacySearches;
        var recalls = Recalls; var swaps = Swaps; var inputs = Get(run, "Inputs"); var route = Get(run, "AdventureRoute");
        for (var i = 0; i < 5; i++)
        { CallMain("GetCombatPrepRuntime"); CallMain("GetCustomPlanProgress"); CallMain("CustomAdventureTick"); CallMain("RouteCombatPrepAdventure"); }
        Equal(searches, AdventureWeightSearches, "telemetry and stable route perform no optimization"); Equal(recalls, Recalls, "telemetry/stable routing never recalls"); Equal(swaps, Swaps, "telemetry/stable routing never equips");
        Equal(legacySearches, AdventureWeightLegacySearches, "telemetry and stable route never invoke legacy gear optimizer either");
        Equal(true, ReferenceEquals(inputs, Get(run, "Inputs")) && ReferenceEquals(route, Get(run, "AdventureRoute")), "telemetry retains exact immutable selection");
        Set(Get(Character, "adventure"), "zone", -1); ControlledCombatPrepClock += 1;
        CallMain("CustomAdventureTick"); AdventureWeightActiveRoute(run);
        Equal(searches, AdventureWeightSearches, "actual custom-adventure callback reuses selected route");
        Equal(legacySearches, AdventureWeightLegacySearches, "actual custom-adventure callback never invokes legacy gear search");
        CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
    }

    private static void CheckAdventureWeightDeferred()
    {
        foreach (var mode in new[] { "manual", "beast", "unsupported", "titan", "disabled", "disabled-itopod-locked" })
        {
            var plan = AdventureWeightSetup();
            if (mode == "manual") Setting("ManualAdventureEnabled", true);
            if (mode == "beast") Set(Get(Character, "adventure"), "beastModeOn", true);
            if (mode == "unsupported") Set(Settings, "_customAdventureMode", 3);
            if (mode == "titan") { Set(Settings, "_customAdventureMode", 1); Set(Settings, "_customAdventureZone", 6); }
            if (mode.StartsWith("disabled")) Setting("CustomAdventureEnabled", false);
            if (mode == "disabled-itopod-locked")
            {
                Set(Get(Character, "adventure"), "highestItopodLevel", 0);
                Set(Get(Get(Character, "inventory"), "itemList"), "itopodKeyComplete", false);
                Exception unavailable = null;
                try { CallMain("CaptureAutomaticCombatPrep", 15d, 300); } catch (Exception error) { unavailable = error; }
                Equal(true, unavailable != null && unavailable.ToString().Contains("Requested combat prep adventure is unavailable"), "positive control: actual weighted capture rejects unavailable ITOPOD");
            }
            var searches = AdventureWeightSearches;
            var run = StartScheduled(plan);
            Equal(15d, Get(run, "AdventureConfiguredWeight"), "configured positive weight remains visible: " + mode);
            Equal(false, Get(Get(run, "Inputs"), "WeightedProfiles"), "temporarily deferred effective zero: " + mode);
            Equal("deferred", Get(run, "AdventureStatus"), "explicit deferred status: " + mode);
            var runtime = CallMain("GetCombatPrepRuntime");
            Equal(15d, Get(Json(runtime, "configuredAdventureWeight"), "AsDouble"), "deferred telemetry retains configured intent: " + mode);
            Equal(0d, Get(Json(runtime, "adventureDynamicWeight"), "AsDouble"), "deferred telemetry reports effective zero: " + mode);
            Equal(-1, Get(Get(Character, "adventure"), "zone"), "deferred startup never routes: " + mode);
            Equal(searches, AdventureWeightSearches, "deferred startup never attempts weighted optimization: " + mode);
            Equal(true, ((Array)Get(Get(run, "Policy"), "Phases")).Length > 0, "deferred adventure does not block actual primary allocation: " + mode);
            CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
        }
    }

    private static void EditAdventureWeight(double weight)
    {
        var store = Get(MainType, "CustomPlanStore"); var plan = Invoke(store, "Load", "fixture");
        Set(((IList)Get(plan, "Stages"))[0], "AdventureDynamicWeight", weight); Invoke(store, "Save", "fixture", plan);
    }

    private static void CheckAdventureWeightEdits()
    {
        var plan = AdventureWeightSetup(); Set(Get(Get(Character, "buttons"), "advancedTraining"), "m_Interactable", false);
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        var head = Get(Get(Character, "inventory"), "head");
        SetEnum(head, "spec1Type", "Wandoos98"); Set(head, "spec1Cur", 1000000f); RefreshBonuses();
        var run = StartScheduled(plan); var deadline = Get(run, "DeadlineRebirthSeconds"); var capture = Get(run, "Capture");
        EditAdventureWeight(16); EditAdventureWeight(20); Set(Settings, "_customAdventureMode", 2);
        var searches = AdventureWeightSearches;
        CallMain("TickCombatPrep"); Equal(0, Get(run, "ReplanCount"), "edits wait for allocation cadence"); Equal(searches, AdventureWeightSearches, "no eager optimization");
        AdventureWeightUnsafeSwaps = 0; ObserveAdventureWeightSwaps = true;
        ControlledCombatPrepClock += 1; ScheduledRefresh(run);
        Equal("running", Get(run, "Status"), "edited task remains executable: " + Get(run, "Reason"));
        Equal(1, Get(run, "ReplanCount"), "task and weight edits coalesce into one replan"); Equal("adventure-intent-change", Get(run, "ReplanReason"), "explicit replan reason");
        Equal(20d, Get(Get(run, "Inputs"), "AdventureDynamicWeight"), "fresh capture uses latest weight");
        Equal(false, Equals(Get(capture, "AdventureTaskKey"), Get(Get(run, "Inputs"), "AdventureTaskKey")), "fresh capture uses edited dynamic task");
        Equal(0, AdventureWeightUnsafeSwaps, "owned farming retired to safe zone before replacement gear");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "edit preserves original deadline"); Equal(true, ReferenceEquals(capture, Get(run, "Capture")), "edit preserves restoration capture");
        searches = AdventureWeightSearches; ControlledCombatPrepClock += 1; ScheduledRefresh(run);
        Equal(1, Get(run, "ReplanCount"), "unchanged observed task does not replan repeatedly"); Equal(searches, AdventureWeightSearches, "stable refresh does not optimize");
        EditAdventureWeight(0); ControlledCombatPrepClock += 1; ScheduledRefresh(run);
        Equal(2, Get(run, "ReplanCount"), "disabling weight causes exactly one further replan");
        Equal("running", Get(run, "Status"), "zero edit remains executable: " + Get(run, "Reason"));
        Equal(false, Get(Get(run, "Inputs"), "WeightedProfiles"), "zero edit returns to actual legacy capture");
        Equal("disabled", Get(run, "AdventureStatus"), "zero edit disables weighted routing");
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "zero edit safely retires old farming route");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "zero edit cannot extend deadline");
        CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
    }

    private static object AdventureWeightAtFixture(out object plan)
    {
        plan = AdventureWeightSetup(); Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" }) Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        var menus = (long[])Get(Get(Character, "advancedTraining"), "levelTarget"); menus[3] = 5000; menus[4] = 6000;
        var run = StartScheduled(plan); Equal("AT", Get(ScheduledPhase(run), "Kind"), "positive control: actual weighted planner acquires paid AT"); return run;
    }

    private static void CheckAdventureWeightAtGate()
    {
        object plan; var run = AdventureWeightAtFixture(out plan); var phase = ScheduledPhase(run);
        var energyTarget = Get(phase, "AtEnergyTarget"); var magicTarget = Get(phase, "AtMagicTarget"); var searches = AdventureWeightSearches;
        EditAdventureWeight(20); Set(Settings, "_customAdventureMode", 2);
        Set(run, "PhaseDeadline", 9999d); ControlledCombatPrepClock += 1; ScheduledRefresh(run);
        Equal(0, Get(run, "ReplanCount"), "intent edit cannot discard unfinished paid AT"); Equal(searches, AdventureWeightSearches, "unfinished AT defers optimization");
        Equal(energyTarget, Get(ScheduledPhase(run), "AtEnergyTarget"), "AT3 acquired target unchanged"); Equal(magicTarget, Get(ScheduledPhase(run), "AtMagicTarget"), "AT4 acquired target unchanged");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "unfinished AT keeps W energy zero"); Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "unfinished AT keeps W magic zero");
        Equal(true, Get(run, "AdventureReplanPending"), "intent remains pending behind gate"); Equal(-1, Get(Get(Character, "adventure"), "zone"), "old route retired while gate holds");
        var levels = (long[])Get(Get(Character, "advancedTraining"), "level"); levels[3] = (long)energyTarget;
        ControlledCombatPrepClock += 1; ScheduledRefresh(run);
        Equal(0, Get(run, "ReplanCount"), "AT3 completion alone cannot discard unfinished AT4 gate");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "AT4 still blocks W energy"); Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "AT4 still blocks W magic");
        levels[4] = (long)magicTarget;
        ControlledCombatPrepClock += 1; ScheduledRefresh(run);
        Equal(1, Get(run, "ReplanCount"), "gate completion and pending edit cause one replan");
        Equal(false, ((Array)Get(Get(run, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "completed paid AT never purchased again");
        Equal(20d, Get(Get(run, "Inputs"), "AdventureDynamicWeight"), "gate completion adopts latest weight");
        CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
        Equal(5000L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[3], "cleanup restores native AT3 menu");
        Equal(6000L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[4], "cleanup restores native AT4 menu");
    }

    private static void CheckAdventureWeightRecovery()
    {
        object plan; var old = AdventureWeightAtFixture(out plan); var phase = ScheduledPhase(old);
        var e = Get(phase, "AtEnergyTarget"); var m = Get(phase, "AtMagicTarget");
        CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true); ReplaceAutomaticCoordinator(plan);
        Set(Character, "energyPower", 50f); // Live rates may change, but a persisted paid gate must not.
        var changedDuringAcquisition = false; var searches = AdventureWeightSearches;
        CombatPrepClockRead = () => {
            var candidate = Get(MainInstance, "_combatPrepRun");
            if (changedDuringAcquisition || candidate == null || ReferenceEquals(candidate, old) || (int)Get(candidate, "PhaseIndex") != -1) return;
            changedDuringAcquisition = true;
            var head = Get(Get(Character, "inventory"), "head"); Set(head, "curAttack", (float)Get(head, "curAttack") + 10f);
            Set(Get(Character, "machine"), "realBaseGold", (double)Get(Get(Character, "machine"), "realBaseGold") * 2);
            RefreshBonuses();
        };
        CallMain("RunCustomPlanAutomation", plan); var run = Get(MainInstance, "_combatPrepRun");
        CombatPrepClockRead = null;
        Equal(true, changedDuringAcquisition, "positive control: native gear and GPS changed before recovered gate acquisition");
        Equal("running", Get(run, "Status"), "recovery actual automatic dispatch is running");
        Equal("gear", Get(run, "BlockedDomain"), "foreign material callback before recovered acquisition cannot be adopted as fresh authority");
        Equal(-1, Get(run, "PhaseIndex"), "unreviewed pre-acquisition material does not acquire or credit a phase");
        Equal(0, Get(run, "ReplanCount"), "recovery cannot optimize before acquiring persisted gate");
        Equal(searches, AdventureWeightSearches, "recovery ignores pre-acquisition GPS/equipment changes until persisted gate completes");
        var retained = ((Array)Get(Get(run, "Policy"), "Phases")).GetValue(0);
        Equal("AT", Get(retained, "Kind"), "recovery retains unfinished paid gate independently from application authority");
        Equal(false, Get(Get(run, "Inputs"), "WeightedProfiles"), "recovery defers weighted setup");
        Equal(e, Get(retained, "AtEnergyTarget"), "persisted AT3 exact target"); Equal(m, Get(retained, "AtMagicTarget"), "persisted AT4 exact target");
        var levels = (long[])Get(Get(Character, "advancedTraining"), "level"); levels[3] = (long)e; levels[4] = (long)m;
        Set(run, "PhaseDeadline", 9999d); ControlledCombatPrepClock += 1; ScheduledRefresh(run);
        Equal(false, Get(Get(run, "Inputs"), "WeightedProfiles"), "native target completion cannot grant missing material authority");
        Equal(0, Get(run, "ReplanCount"), "unreviewed acquisition cannot trigger a replacement weighted search");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "unreviewed recovery never funds later W progression");
        CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
    }

    private static void CheckAdventureWeightCompletedRecovery()
    {
        object plan; var old = AdventureWeightAtFixture(out plan); var phase = ScheduledPhase(old);
        var entry = Get(old, "OwnerEntry"); var deadline = Get(old, "DeadlineRebirthSeconds");
        CallMain("EndCombatPrep", "cancelled", "Injector unloaded.", true); ReplaceAutomaticCoordinator(plan);
        var levels = (long[])Get(Get(Character, "advancedTraining"), "level");
        levels[3] = (long)Get(phase, "AtEnergyTarget"); levels[4] = (long)Get(phase, "AtMagicTarget");
        Set(Character, "energyPower", 50f);
        AutomaticPaidAtTransfers = 0; ObserveAutomaticTransfers = true;
        CallMain("RunCustomPlanAutomation", plan); var run = Get(MainInstance, "_combatPrepRun");
        Equal("running", Get(run, "Status"), "already-completed recovered gate dispatches primary continuation: " + Get(run, "Reason"));
        Equal(true, Get(Get(MainInstance, "_protectedScheduler"), "AutoAtCompleted"), "first recovered dispatch records native completed AT gate");
        Equal(0, AutomaticPaidAtTransfers, "first recovered dispatch never funds paid AT again");
        Equal(false, ((Array)Get(Get(run, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "completed pre-acquisition gate cannot be repurchased by fresh planning");
        Equal(null, Get(MainInstance, "_automaticAtContinuation"), "completed pre-acquisition continuation is retired");
        Equal(true, Get(Get(run, "Inputs"), "WeightedProfiles"), "completed pre-acquisition gate allows fresh weighted capture");
        Equal(entry, Get(run, "OwnerEntry"), "completed recovery preserves entry"); Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "completed recovery preserves original deadline");
        ControlledCombatPrepClock += 1; ScheduledRefresh(run);
        Equal(0, AutomaticPaidAtTransfers, "subsequent dispatch cannot resurrect completed paid gate");
        ObserveAutomaticTransfers = false; CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
    }

    private static object AdventureWeightThresholdSetup(bool gear)
    {
        var plan = AdventureWeightSetup(); Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" }) Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        if (gear)
        {
            var inventory = Get(Character, "inventory"); var head = Blank(Get(inventory, "head").GetType());
            Set(head, "id", 70); SetEnum(head, "type", "Head"); Set(head, "removable", true); Set(head, "bossRequired", 1);
            SetEnum(head, "spec1Type", "AdvTraining"); Set(head, "spec1Cur", 10000f);
            Set(head, "curAttack", 10000f); Set(head, "capAttack", 10000f); ((IList)Get(inventory, "inventory"))[0] = head;
        }
        else Set(((IList)Get(Get(Character, "beards"), "beards"))[5], "beardLevel", 1000000000000L);
        return plan;
    }

    private static void CheckAdventureWeightThresholdRules()
    {
        foreach (var gear in new[] { true, false })
        foreach (var mode in new[] { 4, 1 })
        {
            var plan = AdventureWeightThresholdSetup(gear); var power = Convert.ToDouble(Invoke(Character, "totalAdvAttack"));
            var ruleType = Plugin.GetType("NGUInjector.AdventureRule", true); var rule = Activator.CreateInstance(ruleType);
            Set(rule, "Enabled", true); Set(rule, "MinPower", power * 2); Set(rule, "Mode", mode); Set(rule, "Zone", mode == 4 ? 0 : 6);
            var rules = Array.CreateInstance(ruleType, 1); rules.SetValue(rule, 0); Set(Settings, "_customAdventureRules", rules);
            var before = CallMain("CombatPrepEquipped");
            Equal(false, CallMain("CombatPrepAdventureDeferred"), "threshold rule does not match before phase acquisition");
            var run = StartScheduled(plan); var phase = ScheduledPhase(run); var inputs = Get(run, "Inputs");
            Equal("AT", Get(phase, "Kind"), "threshold fixture acquires real paid AT");
            Equal(true, Get(inputs, "WeightedProfiles"), "threshold starts with weighted ITOPOD capture");
            Equal(true, Convert.ToDouble(Invoke(Character, "totalAdvAttack")) >= power * 2, "real phase " + (gear ? "gear" : "support") + " crosses rule threshold");
            Equal(!gear, CallMain("CombatPrepGearMatches", before), "positive control distinguishes actual gear and support threshold paths");
            object[] resolved = { 0, 0, 0 }; CallMain("ResolveConfiguredAdventureGoal", resolved);
            Equal(mode, resolved[0], "native post-acquisition stats resolve deferred rule mode");
            Equal(true, CallMain("CombatPrepAdventureDeferred"), "resolved threshold rule is actually deferred");
            Equal(0, AdventureWeightFarmingWrites, "post-swap deferred rule prevents even initial old farming route write");
            Equal(-1, Get(Get(Character, "adventure"), "zone"), "post-swap manual/titan rule never enters old ITOPOD");
            Equal(false, Equals(Get(run, "AdventureIntent"), Get(run, "AdventureObservation")), "positive control: post-swap anti-feedback baseline contains deferred rule");
            var e = Get(phase, "AtEnergyTarget"); var m = Get(phase, "AtMagicTarget"); var searches = AdventureWeightSearches;
            ControlledCombatPrepClock += 1; ScheduledRefresh(run);
            Equal(true, Get(run, "AdventureReplanPending"), "deferred rule overrides post-swap anti-feedback baseline and queues recapture");
            Equal(0, Get(run, "ReplanCount"), "deferred rule cannot disrupt unfinished paid gate"); Equal(searches, AdventureWeightSearches, "deferred rule does not optimize during paid gate");
            Equal(e, Get(ScheduledPhase(run), "AtEnergyTarget"), "threshold retains acquired AT3 target"); Equal(m, Get(ScheduledPhase(run), "AtMagicTarget"), "threshold retains acquired AT4 target");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "threshold gate keeps W energy zero"); Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "threshold gate keeps W magic zero");
            CallMain("CustomAdventureTick"); Equal(0, AdventureWeightFarmingWrites, "custom adventure callback also refuses deferred old route");
            var levels = (long[])Get(Get(Character, "advancedTraining"), "level"); levels[3] = (long)e; levels[4] = (long)m;
            Set(run, "PhaseDeadline", 9999d); ControlledCombatPrepClock += 1; ScheduledRefresh(run);
            Equal("running", Get(run, "Status"), "completed threshold gate continues primary work: " + Get(run, "Reason"));
            Equal(1, Get(run, "ReplanCount"), "completed gate coalesces queued deferred recapture once");
            Equal(false, Get(Get(run, "Inputs"), "WeightedProfiles"), "queued threshold recapture uses effective adventure zero");
            Equal("deferred", Get(run, "AdventureStatus"), "queued recapture exposes deferred status");
            Equal(false, ((Array)Get(Get(run, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "threshold continuation cannot repurchase paid AT");
            Equal(null, Get(MainInstance, "_globalPresetReceipt"), "deferred rule never acquires competing global preset");
            CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
        }
    }

    private static void CheckAdventureWeightPartialRouteFailure()
    {
        var plan = AdventureWeightThresholdSetup(true); AdventureWeightFailRouteAfterWrite = true;
        var run = StartScheduled(plan);
        Equal(1, AdventureWeightRouteFaults, "positive control: native route writes then rendering boundary throws");
        Equal(1000, AdventureWeightPartialZone, "partial failure really left native ITOPOD route written before catch");
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "partial route is safely retired after native rendering failure");
        Equal(false, Get(run, "AdventureRouteOwned"), "successful retirement releases partial route receipt");
        Equal("blocked", Get(run, "AdventureStatus"), "partial route failure is explicit rather than claiming active route");
        Equal(true, CallMain("CombatPrepGearMatches", Get(run, "ExpectedGear")), "partial route failure retains acquired primary phase gear");
        Equal(null, Get(MainInstance, "_globalPresetReceipt"), "partial route failure creates no global equipment owner");
        var searches = AdventureWeightSearches; ControlledCombatPrepClock += 1; ScheduledRefresh(run);
        AdventureWeightActiveRoute(run); Equal(searches, AdventureWeightSearches, "route retry reuses existing candidate without optimization");
        CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
    }

    private static void CheckAdventureWeightRetirementFailure()
    {
        var unsafeSupport = new List<string>();
        foreach (var partial in new[] { false, true })
        {
            var plan = AdventureWeightThresholdSetup(true); var original = CallMain("CombatPrepEquipped");
            if (partial) AdventureWeightFailRouteAfterWrite = AdventureWeightFailRetirement = true;
            CallMain("RunCustomPlanAutomation", plan); var run = Get(MainInstance, "_combatPrepRun");
            if (!partial)
            {
                AdventureWeightActiveRoute(run); AdventureWeightFailRetirement = true;
                CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
            }
            Equal(true, AdventureWeightRetirementFaults > 0, "positive control: native retirement boundary throws before safe-zone write");
            Equal(partial ? 1 : 0, AdventureWeightRouteFaults, "partial and ordinary retirement faults use intended route-entry path");
            Equal(1000, Get(Get(Character, "adventure"), "zone"), "failed safe-zone write actually leaves farming alive");
            Equal(true, Get(run, "AdventureRouteOwned"), "failed retirement retains exact live route receipt");
            Equal(true, Get(run, "CleanupPending"), "failed retirement keeps cleanup/equipment ownership blocked");
            Equal(false, CallMain("CombatPrepGearMatches", original), "failed retirement cannot restore weaker original gear while farming survives");
            var phaseGear = CallMain("CombatPrepEquipped"); var swaps = Swaps;
            var args = PrepareInterruptionPreset();
            RejectInterruptionEquip(() => CallMain("EquipGlobalPreset", args, null), "Combat prep owns resources");
            Equal(null, Get(MainInstance, "_globalPresetReceipt"), "failed retirement blocks competing global owner");
            Equal(swaps, Swaps, "blocked global Equip cannot replace farming gear"); Equal(true, CallMain("CombatPrepGearMatches", phaseGear), "farming gear retained across competing acquisition attempt");
            var power = Convert.ToDouble(Invoke(Character, "totalAdvAttack"));
            if (power < AdventureWeightRoutedPower) unsafeSupport.Add("partial=" + partial + ", zone=" + Get(Get(Character, "adventure"), "zone")
                + ", native power " + AdventureWeightRoutedPower + " -> " + power + ", support=" + Get(run, "SupportStatus"));
            AdventureWeightFailRetirement = false; ControlledCombatPrepClock += .6; CallMain("TickCombatPrep");
            Equal(-1, Get(Get(Character, "adventure"), "zone"), "repaired native boundary retires route before cleanup retry");
            Equal(false, Get(run, "CleanupPending"), "safe retirement retry releases cleanup blocker");
            Equal(true, CallMain("CombatPrepGearMatches", phaseGear), "safe retirement releases the route without a historical weaker-gear detour");
        }
        Equal("", string.Join("; ", unsafeSupport), "retirement failure must not weaken native farming support while owned route survives");
    }

    private static void CheckAdventureWeightRetirementExhaustion()
    {
        var plan = AdventureWeightThresholdSetup(true); var run = StartScheduled(plan); AdventureWeightActiveRoute(run);
        var phaseGear = CallMain("CombatPrepEquipped"); AdventureWeightFailRetirement = true;
        CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
        for (var i = 0; i < 5; i++) { ControlledCombatPrepClock += .6; CallMain("TickCombatPrep"); }
        Equal(true, AdventureWeightRetirementFaults >= 4, "positive control: persistent native retirement fault exhausts bounded cleanup attempts");
        Equal(1000, Get(Get(Character, "adventure"), "zone"), "exhausted cleanup still has actual farming route alive");
        Equal(true, CallMain("CombatPrepGearMatches", phaseGear), "exhausted cleanup cannot restore weaker historical gear");
        var inventory = Get(Character, "inventory"); var head = Blank(Get(inventory, "head").GetType());
        Set(head, "id", 71); SetEnum(head, "type", "Head"); Set(head, "bossRequired", 1); Set(head, "removable", true);
        SetEnum(head, "spec1Type", "EnergyCap"); Set(head, "spec1Cur", 1000000f); ((IList)Get(inventory, "inventory"))[1] = head;
        var args = PrepareInterruptionPreset(); var swaps = Swaps; Exception blocked = null;
        try { CallMain("EquipGlobalPreset", args, null); } catch (Exception error) { blocked = error; }
        Equal(true, blocked != null && blocked.ToString().Contains("Combat prep owns resources"),
            "persistent route retirement failure must block competing Equip even after retry exhaustion: zone=" + Get(Get(Character, "adventure"), "zone")
            + ", retired=" + Get(run, "Retired") + ", globalReceipt=" + (Get(MainInstance, "_globalPresetReceipt") != null)
            + ", swaps=" + (Swaps - swaps) + ", head=" + Get(Get(inventory, "head"), "id") + ", failure=" + blocked?.Message);
        Equal(null, Get(MainInstance, "_globalPresetReceipt"), "unretired farming prevents competing global receipt after cleanup exhaustion");
        Equal(swaps, Swaps, "unretired farming prevents competing equipment writes after cleanup exhaustion");
    }

    private static void CheckAdventureWeightCleanup()
    {
        var plan = AdventureWeightSetup(); PrepareAdventureWeightTradeoffGear();
        var inventory = Get(Character, "inventory"); var bag = (IList)Get(inventory, "inventory"); var originalHead = Get(inventory, "head");
        Set(inventory, "head", bag[1]); bag[1] = originalHead; RefreshBonuses();
        var gear = CallMain("CombatPrepEquipped"); var support = CallMain("CombatPrepSupportKey"); var before = Swaps;
        var run = StartScheduled(plan); AdventureWeightActiveRoute(run);
        Equal(true, Swaps > before, "positive control: cleanup fixture actually acquired different phase gear");
        var phaseGear = CallMain("CombatPrepEquipped"); var phaseSupport = CallMain("CombatPrepSupportKey");
        before = Swaps; AdventureWeightUnsafeSwaps = 0; ObserveAdventureWeightSwaps = true;
        CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
        Equal(before, Swaps, "claim release performs zero historical gear swaps");
        Equal(0, AdventureWeightUnsafeSwaps, "owned route retired before cleanup gear replacement");
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "owned route retires to safe zone on cleanup"); Equal(false, Get(run, "AdventureRouteOwned"), "cleanup releases route ownership");
        Equal(true, CallMain("CombatPrepGearMatches", phaseGear), "release preserves actual phase gear without an explicit baseline");
        Equal(phaseSupport, CallMain("CombatPrepSupportKey"), "release preserves actual phase support without a historical return");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "cleanup recalls W energy"); Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "cleanup recalls W magic");
    }

    private static void CheckAdventureWeightForeignRoutes()
    {
        foreach (var owner in new[] { "manual", "titan", "quest", "foreign-zone", "foreign-floor" })
        {
            var plan = AdventureWeightSetup(); var run = StartScheduled(plan); AdventureWeightActiveRoute(run);
            var adventure = Get(Character, "adventure");
            if (owner == "manual") Setting("ManualAdventureEnabled", true);
            if (owner == "titan") Set(MainInstance, "_customTitanFightActive", true);
            if (owner == "quest")
            {
                Setting("AutoQuest", true); Set(Get(Character, "beastQuest"), "inQuest", true); Set(Get(Character, "beastQuest"), "questID", 278);
                Set(Child(Character, "beastQuestController"), "character", Character);
                var manager = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.QuestManager", true), true);
                Set(MainInstance, "_questManager", manager); Equal(1, Invoke(manager, "IsQuesting"), "positive control: real quest manager owns a regular zone");
            }
            if (owner == "foreign-zone") Set(adventure, "zone", 0);
            if (owner == "foreign-floor") Set(adventure, "itopodStart", (int)Get(adventure, "itopodStart") + 1);
            var zone = Get(adventure, "zone"); var floor = Get(adventure, "itopodStart");
            CallMain("RetireCombatPrepAdventureRoute", run);
            Equal(zone, Get(adventure, "zone"), "retirement cannot overwrite new route owner " + owner); Equal(floor, Get(adventure, "itopodStart"), "retirement preserves foreign floor " + owner);
            Equal(false, Get(run, "AdventureRouteOwned"), "foreign route ownership released " + owner);
            CallMain("EndCombatPrep", "cancelled", "weight regression cleanup", true);
            Equal(zone, Get(adventure, "zone"), "later cleanup cannot overwrite retired foreign route " + owner);
        }
    }
}
